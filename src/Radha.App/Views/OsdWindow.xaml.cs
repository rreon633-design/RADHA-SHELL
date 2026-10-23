using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Radha.App.Interop;
using Radha.App.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace Radha.App.Views;

/// <summary>Click-through volume/brightness pill anchored to the bottom of the primary display.</summary>
public sealed partial class OsdWindow : Window
{
    private readonly ShellState _state;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private AppWindow? _appWindow;

    public OsdWindow(ShellState state)
    {
        _state = state;
        InitializeComponent();
        if (Content is FrameworkElement root) root.DataContext = state;

        Title = "RADHA SHELL — OSD";
        TryEnableAcrylic();
        ConfigureWindow();

        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _state.OsdVisible = false;
        };
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellState.OsdVisible)) return;
            if (state.OsdVisible)
            {
                Show();
                _timer.Stop();
                _timer.Start();
            }
            else
            {
                Hide();
            }
        };
        state.ScheduleOsdHide = () =>
        {
            _timer.Stop();
            _timer.Start();
            Show();
        };
    }

    private void ConfigureWindow()
    {
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        const int width = 280;
        const int height = 72;
        _appWindow.MoveAndResize(new RectInt32(
            display.WorkArea.X + (display.WorkArea.Width - width) / 2,
            display.WorkArea.Y + display.WorkArea.Height - height - 24,
            width, height));

        NativeMethods.MakeOverlay(WindowNative.GetWindowHandle(this), clickThrough: true);
        NativeMethods.ApplyDwmHints(WindowNative.GetWindowHandle(this), _state.Theme.IsDark, backdropType: 1);
    }

    private void Show()
    {
        _appWindow?.Show();
    }

    private void Hide()
    {
        _appWindow?.Hide();
    }

    /// <summary>Acrylic blurs the desktop behind our translucent surfaces (Win10 1809+).</summary>
    private void TryEnableAcrylic()
    {
        try { SystemBackdrop = new DesktopAcrylicBackdrop(); }
        catch { /* composition backdrops unavailable: brushes stay semi-opaque */ }
    }
}
