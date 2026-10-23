using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Radha.App.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace Radha.App.Views;

/// <summary>
/// Full-screen transparent window that hosts every pop-out panel.
/// Shown only while a panel is open; closes on scrim tap or Escape.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    private readonly ShellState _state;
    private AppWindow? _appWindow;

    public OverlayWindow(
        ShellState state,
        DashboardViewModel dashboard,
        LauncherViewModel launcher,
        ControlCenterViewModel controlCenter)
    {
        _state = state;
        InitializeComponent();
        Root.DataContext = state;

        Dashboard.DataContext = dashboard;
        Launcher.DataContext = launcher;
        ControlCenter.DataContext = controlCenter;
        NotificationCenter.DataContext = state;
        SessionMenu.DataContext = state;

        Title = "RADHA SHELL — Overlay";
        TryEnableAcrylic();
        ConfigureWindow();

        state.OverlayChanged += (_, _) => SyncVisibility();
        SyncVisibility();
    }

    private void ConfigureWindow()
    {
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        _appWindow.MoveAndResize(new RectInt32(
            display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height));

        var hwnd = WindowNative.GetWindowHandle(this);
        Interop.NativeMethods.MakeOverlay(hwnd, clickThrough: false);
        Interop.NativeMethods.ApplyDwmHints(hwnd, _state.Theme.IsDark, backdropType: 1);
    }

    private void SyncVisibility()
    {
        if (_state.IsOverlayOpen)
        {
            AppWindow.Show();
            Activate();
        }
        else
        {
            AppWindow.Hide();
        }
    }

    private void OnScrimTapped(object sender, TappedRoutedEventArgs e) => _state.CloseAllCommand.Execute(null);

    /// <summary>Acrylic blurs the desktop behind our translucent surfaces (Win10 1809+).</summary>
    private void TryEnableAcrylic()
    {
        try { SystemBackdrop = new DesktopAcrylicBackdrop(); }
        catch { /* composition backdrops unavailable: brushes stay semi-opaque */ }
    }
}
