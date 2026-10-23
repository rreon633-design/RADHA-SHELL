using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private readonly DisplayArea? _target;
    private readonly int _barHeight;
    private readonly string[] _scrollActions;
    private AppWindow? _appWindow;

    /// <summary>
    /// A bar on the primary display when no target is given, or on a specific display with a
    /// height taken from that monitor's config override.
    /// </summary>
    public BarWindow(ShellState state, DisplayArea? target = null, int barHeight = 0, string[]? scrollActions = null)
    {
        _state = state;
        _target = target;
        _barHeight = barHeight > 0 ? barHeight : 42;
        _scrollActions = scrollActions ?? new[] { "volume", "workspaces" };
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

    /// <summary>
    /// Scroll on the bar: volume or workspace depending on configuration. Pointer input is read in
    /// the code-behind because the segments are generated (the workspace strip is rebuilt on every
    /// change) and handlers attached in code survive that.
    /// </summary>
    private void OnBarWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        if (delta == 0) return;

        var direction = delta > 0 ? 1 : -1;
        if (_scrollActions.Contains("volume", StringComparer.OrdinalIgnoreCase))
            _state.NudgeVolume(direction * 2);
        else if (_scrollActions.Contains("workspaces", StringComparer.OrdinalIgnoreCase))
            _state.CycleWorkspace(direction);

        e.Handled = true;
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

        var display = _target ?? DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var width = display.WorkArea.Width;
        var scale = GetDpiScale();
        var height = (int)Math.Round(_barHeight * scale);
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
