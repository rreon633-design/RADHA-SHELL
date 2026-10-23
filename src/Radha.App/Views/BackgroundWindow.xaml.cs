using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Radha.App.Interop;
using Radha.Core.Config;
using Radha.Core.Services;
using Radha.Core.Theming;
using Radha.Core.Wallpaper;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace Radha.App.Views;

/// <summary>
/// The shell's own background layer: the current wallpaper, an optional dim, a clock and an audio
/// spectrum — drawn behind the desktop icons by reparenting into Explorer's WorkerW window.
///
/// This is also the only place a wallpaper *transition* can exist. Windows paints SPI_SETDESKWALLPAPER
/// instantly and offers no hook to animate it, so the cross-fade happens in our layer while the
/// system wallpaper changes underneath it (which is what remains visible if the shell exits).
/// </summary>
public sealed partial class BackgroundWindow : Window
{
    private readonly IWallpaperService _wallpaper;
    private readonly IAudioService _audio;
    private readonly BackgroundConfig _config;
    private readonly MotionTokens _motion;

    private DispatcherQueueTimer? _clockTimer;
    private DispatcherQueueTimer? _spectrumTimer;
    private int _silentFrames;
    private bool _frontVisible;
    private bool _attached;

    public BackgroundWindow(ShellConfig config, IWallpaperService wallpaper, IAudioService audio)
    {
        _wallpaper = wallpaper;
        _audio = audio;
        _config = config.Background;
        _motion = new MotionTokens(config.Appearance.AnimationScale).Normalized();

        InitializeComponent();
        Title = "RADHA SHELL — Background";

        Root.ActualThemeChanged += (_, _) => ApplyPalette();
        _wallpaper.Changed += OnWallpaperChanged;
        Closed += (_, _) => Shutdown();

        ConfigureWindow();
        ApplyPalette();
        LoadCurrentWallpaper();
        StartClock();

        if (_config.Visualiser) _spectrumTimer = StartTimer(TimeSpan.FromMilliseconds(33), OnSpectrumTick);
    }

    private void ConfigureWindow()
    {
        var handle = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(handle));

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var display = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        appWindow.MoveAndResize(new RectInt32(display.OuterBounds.X, display.OuterBounds.Y, display.OuterBounds.Width, display.OuterBounds.Height));

        // Click-through and non-activating: the desktop must stay the desktop.
        NativeMethods.MakeOverlay(handle, clickThrough: true);

        // Behind the icons or not at all. Showing it as a normal window would hide the user's
        // desktop icons and eat every desktop click.
        _attached = DesktopWallpaperLayer.Attach(handle);
        if (!_attached) Close();
    }

    private void ApplyPalette()
    {
        DimLayer.Opacity = _config.Dim;
        ClockPanel.Visibility = _config.Clock ? Visibility.Visible : Visibility.Collapsed;
        Spectrum.Visibility = _config.Visualiser ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadCurrentWallpaper()
    {
        var path = _wallpaper.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        WallpaperBack.Source = new BitmapImage(new Uri(path));
    }

    private void OnWallpaperChanged(object? sender, string path)
    {
        // Called from the service on whatever thread applied the change.
        DispatcherQueue.TryEnqueue(() => CrossFade(path));
    }

    /// <summary>
    /// Fades the incoming image over the outgoing one. With reduced motion the swap is immediate,
    /// which is the whole point of the setting.
    /// </summary>
    private void CrossFade(string path)
    {
        if (!File.Exists(path)) return;

        var incoming = new BitmapImage(new Uri(path));
        var target = _frontVisible ? WallpaperBack : WallpaperFront;
        var outgoing = _frontVisible ? WallpaperFront : WallpaperBack;

        target.Source = incoming;
        target.Opacity = 0;

        var plan = WallpaperLibrary.PlanTransition(
            _config.Visualiser ? TransitionMode.CrossFade : TransitionMode.CrossFade,
            _motion);

        if (!plan.Blend)
        {
            target.Opacity = 1;
            outgoing.Opacity = 0;
            _frontVisible = !_frontVisible;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(plan.Duration),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            outgoing.Opacity = 0;
            outgoing.Source = null;
            _frontVisible = !_frontVisible;
        };
        storyboard.Begin();
    }

    private void StartClock()
    {
        TickClock();
        _clockTimer = StartTimer(TimeSpan.FromSeconds(1), (_, _) => TickClock());
    }

    private void TickClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dddd, d MMMM");
    }

    private void OnSpectrumTick(DispatcherQueueTimer timer, object args)
    {
        if (!_config.Visualiser) return;

        var spectrum = _audio.GetSpectrum();
        if (spectrum.IsEmpty)
        {
            StopAfterSilence(timer);
            return;
        }

        var values = new double[spectrum.Length];
        var peak = 0.0;
        for (var i = 0; i < spectrum.Length; i++)
        {
            values[i] = spectrum[i];
            if (values[i] > peak) peak = values[i];
        }

        if (peak < 0.002)
        {
            StopAfterSilence(timer);
            return;
        }

        _silentFrames = 0;
        Spectrum.Values = values;
    }

    /// <summary>
    /// A full-screen layer redrawing 30 times a second while nothing plays is a battery bug, so the
    /// timer parks itself after a second of silence. The clock tick re-arms it, which costs one
    /// comparison per second instead of thirty frames per second forever.
    /// </summary>
    private void StopAfterSilence(DispatcherQueueTimer timer)
    {
        if (++_silentFrames < 30) return;
        timer.Stop();
        Spectrum.Values = null;
    }

    private DispatcherQueueTimer StartTimer(TimeSpan interval, TypedEventHandler<DispatcherQueueTimer, object> handler)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += handler;
        timer.Start();
        return timer;
    }

    private void Shutdown()
    {
        _clockTimer?.Stop();
        _spectrumTimer?.Stop();
        _wallpaper.Changed -= OnWallpaperChanged;
        if (_attached) DesktopWallpaperLayer.Detach(WindowNative.GetWindowHandle(this));
    }
}
