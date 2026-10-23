using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Radha.App.Interop;
using Radha.App.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace Radha.App.Views;

/// <summary>
/// The always-on-top status bar. Unpackaged WinUI 3 window placed manually at the top of the
/// primary display and registered as an application desktop toolbar (AppBar) when possible,
/// so maximised windows stop below it.
/// </summary>
public sealed partial class BarWindow : Window
{
    private readonly ShellState _state;
    private AppWindow? _appWindow;

    public BarWindow(ShellState state)
    {
        _state = state;
        InitializeComponent();
        Root.DataContext = state;

        Title = "RADHA SHELL — Bar";
        TryEnableAcrylic();
        ConfigureWindow();

        _state.Workspaces.CollectionChanged += (_, _) => RebuildWorkspaces();
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellState.Theme)) RebuildWorkspaces();
        };
        RebuildWorkspaces();
    }

    /// <summary>Rebuilds the workspace strip; the active pill is wider and tinted with primary.</summary>
    private void RebuildWorkspaces()
    {
        WorkspaceStrip.Children.Clear();
        var theme = _state.Theme;

        foreach (var workspace in _state.Workspaces)
        {
            var pill = new Button
            {
                Height = 26,
                MinWidth = workspace.Active ? 44 : 26,
                Padding = new Thickness(8, 0, 8, 0),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                Background = workspace.Active
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, theme.PrimaryColor.R, theme.PrimaryColor.G, theme.PrimaryColor.B))
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Content = new TextBlock
                {
                    Text = workspace.Label,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = workspace.Active ? theme.Primary : theme.OnSurfaceVariant
                },
                Tag = workspace
            };
            pill.Click += (_, _) => _state.SwitchWorkspaceCommand.Execute(workspace);
            WorkspaceStrip.Children.Add(pill);
        }
    }

    private void ConfigureWindow()
    {
        _appWindow = GetAppWindowForCurrentWindow();
        if (_appWindow is null) return;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var width = display.WorkArea.Width;
        var scale = GetDpiScale();
        var height = (int)Math.Round(42 * scale);
        _appWindow.MoveAndResize(new RectInt32(display.WorkArea.X, display.WorkArea.Y, width, height));

        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.MakeOverlay(hwnd, clickThrough: false);
        NativeMethods.ApplyDwmHints(hwnd, _state.Theme.IsDark, backdropType: 3);
        NativeMethods.RegisterAppBar(hwnd, height, width);
    }

    private AppWindow? GetAppWindowForCurrentWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(id);
    }

    private double GetDpiScale()
    {
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        return dpi <= 0 ? 1.0 : dpi / 96.0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void OnDashboardClick(object sender, RoutedEventArgs e) => _state.ToggleDashboardCommand.Execute(null);
    private void OnControlCenterClick(object sender, RoutedEventArgs e) => _state.ToggleControlCenterCommand.Execute(null);


    /// <summary>Acrylic blurs the desktop behind our translucent surfaces (Win10 1809+).</summary>
    private void TryEnableAcrylic()
    {
        try { SystemBackdrop = new DesktopAcrylicBackdrop(); }
        catch { /* composition backdrops unavailable: brushes stay semi-opaque */ }
    }
}
