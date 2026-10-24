using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Radha.Core.Config;
using Radha.Core.Services;
using Radha.Core.Theming;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace Radha.App.Views;

/// <summary>
/// First-run setup.
///
/// Opens the first time the executable runs and any time it is launched with <c>--setup</c>. Three
/// short pages cover the choices a new user actually has an opinion about — how it looks, where the
/// bar goes, and which global behaviours are allowed — with a live preview of the bar at the top so
/// nothing has to be imagined.
///
/// It is also the answer to "how do I stop this": the shell has not started yet at this point, so
/// quitting here simply exits, and once the shell is running the same control exists in the control
/// centre and the tray menu.
/// </summary>
public sealed partial class FirstRunWindow : Window
{
    private readonly ConfigStore _store;
    private readonly IThemeService _theme;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ShellConfig _config;
    private int _page;
    private Rgb _seed = new(0x9A, 0x6B, 0xF5);

    private static readonly (string Name, Rgb Colour)[] Palette =
    {
        ("Violet", new Rgb(0x9A, 0x6B, 0xF5)),
        ("Rose", new Rgb(0xE8, 0x6A, 0xA3)),
        ("Amber", new Rgb(0xF2, 0xA6, 0x2B)),
        ("Green", new Rgb(0x3F, 0xC1, 0x7A)),
        ("Teal", new Rgb(0x2F, 0xC6, 0xC9)),
        ("Blue", new Rgb(0x4A, 0x8C, 0xFF))
    };

    public FirstRunWindow(ConfigStore store, IThemeService theme)
    {
        _store = store;
        _theme = theme;
        _config = store.Load();

        InitializeComponent();
        Title = "RADHA SHELL — Set up";

        try
        {
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
            var appWindow = AppWindow.GetFromWindowId(id);
            appWindow.Resize(new SizeInt32(860, 680));
        }
        catch
        {
            // Sizing is cosmetic; a default-sized window still works.
        }

        VariantBox.ItemsSource = Enum.GetValues<SchemeVariant>();
        WorkspacesBox.ItemsSource = new[] { "dots", "numbers", "shapes", "icons" };
        ClockFormatBox.ItemsSource = new[] { "24h", "12h" };
        BuildSwatches();
        LoadFromConfig();
        ShowPage(0);
    }

    /// <summary>Completes when the user either starts the shell (true) or quits (false).</summary>
    public Task<bool> Completion => _completion.Task;

    // ---------------- loading / saving ----------------

    private void BuildSwatches()
    {
        foreach (var (name, colour) in Palette)
        {
            var swatch = new Button
            {
                Width = 38,
                Height = 38,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(255, colour.R, colour.G, colour.B)),
                Tag = colour,
                Content = new FontIcon { Glyph = "\uE73E", FontSize = 11, Visibility = Visibility.Collapsed }
            };

            ToolTipService.SetToolTip(swatch, name);
            swatch.Click += (_, _) =>
            {
                _seed = (Rgb)swatch.Tag;
                SelectSwatch(swatch);
                ApplyPreview();
            };

            SwatchPanel.Children.Add(swatch);
        }

        if (SwatchPanel.Children[0] is Button first) SelectSwatch(first);
    }

    private void SelectSwatch(Button selected)
    {
        foreach (var child in SwatchPanel.Children)
        {
            if (child is not Button button) continue;
            var isSelected = ReferenceEquals(button, selected);
            button.BorderBrush = new SolidColorBrush(isSelected
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(0, 0, 0, 0));
            if (button.Content is FontIcon icon)
                icon.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void LoadFromConfig()
    {
        var a = _config.Appearance;
        var b = _config.Bar;
        var n = _config.Notifications;

        ModeBox.SelectedIndex = a.Mode switch
        {
            "dark" => 1,
            "light" => 2,
            _ => 0
        };

        DarkPreviewSwitch.IsOn = _theme.Current.IsDark;
        VariantBox.SelectedItem = Enum.TryParse<SchemeVariant>(a.SchemeVariant, true, out var variant)
            ? variant
            : SchemeVariant.TonalSpot;

        RoundingSlider.Value = a.RoundingScale;
        AnimationSlider.Value = a.AnimationScale;
        ScaleSlider.Value = a.Scale;

        PositionBox.SelectedIndex = b.Position == "bottom" ? 1 : 0;
        HeightSlider.Value = b.Height;
        AllDisplaysSwitch.IsOn = b.ShowOnAllDisplays;
        TraySwitch.IsOn = b.ShowTray;
        AutoHideSwitch.IsOn = b.AutoHide;
        WorkspaceCountSlider.Value = b.WorkspaceCount;

        WorkspacesBox.SelectedItem = b.WorkspacesDisplay;
        if (WorkspacesBox.SelectedIndex < 0) WorkspacesBox.SelectedIndex = 0;

        ClockFormatBox.SelectedItem = b.ClockFormat;
        if (ClockFormatBox.SelectedIndex < 0) ClockFormatBox.SelectedIndex = 0;
        SecondsSwitch.IsOn = b.ShowClockSeconds;

        HotkeysSwitch.IsOn = true;
        OsdSwitch.IsOn = _config.Osd.Enabled;
        UpdateSwitch.IsOn = true;
        MirrorSwitch.IsOn = n.ListenSystemToasts;
        DndSwitch.IsOn = n.DoNotDisturb;
        TimeoutSlider.Value = n.TimeoutSeconds;

        PathsText.Text = $"Config   {_store.ConfigPath}\nLogs     {_config.Paths.Logs}";
        ApplyPreview();
    }

    /// <summary>Writes to the config file. Later pages read the same object, so nothing is lost.</summary>
    private void Capture()
    {
        var a = _config.Appearance;
        var b = _config.Bar;
        var n = _config.Notifications;

        a.Mode = ModeBox.SelectedIndex switch { 1 => "dark", 2 => "light", _ => "auto" };
        a.SchemeVariant = VariantBox.SelectedItem?.ToString() ?? a.SchemeVariant;
        a.RoundingScale = RoundingSlider.Value;
        a.AnimationScale = AnimationSlider.Value;
        a.Scale = ScaleSlider.Value;

        b.Position = PositionBox.SelectedIndex == 1 ? "bottom" : "top";
        b.Height = (int)Math.Round(HeightSlider.Value);
        b.ShowOnAllDisplays = AllDisplaysSwitch.IsOn;
        b.ShowTray = TraySwitch.IsOn;
        b.AutoHide = AutoHideSwitch.IsOn;
        b.WorkspaceCount = (int)Math.Round(WorkspaceCountSlider.Value);
        b.WorkspacesDisplay = WorkspacesBox.SelectedItem?.ToString() ?? b.WorkspacesDisplay;
        b.ClockFormat = ClockFormatBox.SelectedItem?.ToString() ?? b.ClockFormat;
        b.ShowClockSeconds = SecondsSwitch.IsOn;

        _config.Osd.Enabled = OsdSwitch.IsOn;
        n.ListenSystemToasts = MirrorSwitch.IsOn;
        n.DoNotDisturb = DndSwitch.IsOn;
        n.TimeoutSeconds = (int)Math.Round(TimeoutSlider.Value);
    }

    // ---------------- preview ----------------

    private void ApplyPreview()
    {
        var dark = DarkPreviewSwitch.IsOn;
        var scheme = MaterialScheme.FromSeed(_seed, dark, SchemeVariant.TonalSpot);
        var roles = scheme.ToRoleMap();

        Color Role(string name)
        {
            var rgb = roles.TryGetValue(name, out var value) ? value : _seed;
            return Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
        }

        PreviewBar.Background = new SolidColorBrush(WithAlpha(Role("surfaceContainer"), 0xE6));
        PreviewBar.BorderBrush = new SolidColorBrush(Role("outlineVariant"));
        PreviewBar.CornerRadius = new CornerRadius(Math.Clamp(RoundingSlider.Value, 0, 2) * 10 + 4);
        PreviewBar.Height = Math.Clamp(HeightSlider.Value, 28, 64);
        PreviewLogo.Foreground = new SolidColorBrush(Role("primary"));
        PreviewClock.Foreground = new SolidColorBrush(Role("onSurface"));
    }

    private static Color WithAlpha(Color colour, byte alpha) =>
        Color.FromArgb(alpha, colour.R, colour.G, colour.B);

    private void OnDarkPreviewToggled(object sender, RoutedEventArgs e) => ApplyPreview();
    private void OnHeightChanged(object sender, object e) => ApplyPreview();

    // ---------------- navigation ----------------

    private static readonly string[] StepNames = { "Appearance", "Bar", "Behaviour" };

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, 2);

        PageAppearance.Visibility = _page == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageBar.Visibility = _page == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageBehaviour.Visibility = _page == 2 ? Visibility.Visible : Visibility.Collapsed;

        Step1.Style = _page == 0 ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
        Step2.Style = _page == 1 ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
        Step3.Style = _page == 2 ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;

        BackButton.IsEnabled = _page > 0;
        NextButton.Content = _page == 2 ? "Start RADHA SHELL" : "Next";
        StatusText.Text = $"Step {_page + 1} of 3 · {StepNames[_page]}";
    }

    private void OnStepAppearance(object sender, RoutedEventArgs e) => ShowPage(0);
    private void OnStepBar(object sender, RoutedEventArgs e) => ShowPage(1);
    private void OnStepBehaviour(object sender, RoutedEventArgs e) => ShowPage(2);

    private void OnBack(object sender, RoutedEventArgs e) => ShowPage(_page - 1);

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_page < 2)
        {
            ShowPage(_page + 1);
            return;
        }

        Capture();

        try
        {
            _store.SavePreservingUnknown(_config.Normalized());
            StatusText.Text = $"Saved to {_store.ConfigPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save the config: {ex.Message}";
            return;
        }

        _theme.Apply(new RgbSeed(_seed.R, _seed.G, _seed.B), DarkPreviewSwitch.IsOn, _theme.Current.Variant);
        Close();
        _completion.TrySetResult(true);
    }

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        Close();
        _completion.TrySetResult(false);
    }
}
