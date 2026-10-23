using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Radha.Core.Config;
using Radha.Core.Diagnostics;
using Radha.Core.Services;
using Radha.Core.Theming;
using Radha.Core.Wallpaper;

namespace Radha.App.Views;

/// <summary>
/// Settings — the only place a user needs to touch to configure the shell.
///
/// Deliberately control-driven rather than binding-driven: the window edits a strongly typed
/// <see cref="ShellConfig"/>, and a save goes through <see cref="ConfigStore.SavePreservingUnknown"/>
/// so keys this build does not model survive. The running shell picks the change up through its
/// config watcher, so nothing has to restart — except the two things that cannot be changed live
/// (transparency, and the desktop layer's existence), which say so on the control itself.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly ConfigStore _store;
    private readonly IThemeService _theme;
    private readonly Func<Task<string>> _updateCheck;
    private readonly Func<string, bool> _applyPower;
    private readonly int _hotkeys;
    private readonly IReadOnlyList<string> _hotkeyFailures;
    private ShellConfig _config;
    private bool _loading;

    public SettingsWindow(ConfigStore store, IThemeService theme, Func<Task<string>> updateCheck,
                          Func<string, bool> applyPower, int hotkeys, IReadOnlyList<string> hotkeyFailures)
    {
        _store = store;
        _theme = theme;
        _updateCheck = updateCheck;
        _applyPower = applyPower;
        _hotkeys = hotkeys;
        _hotkeyFailures = hotkeyFailures;
        _config = store.Load();

        InitializeComponent();
        Title = "RADHA SHELL — Settings";

        // A settings window has a natural size; without this it opens at the WinUI default.
        try
        {
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                    WinRT.Interop.WindowNative.GetWindowHandle(this)));
            appWindow.Resize(new Windows.Graphics.SizeInt32(780, 900));
        }
        catch (Exception) { /* sizing is cosmetic */ }

        VariantBox.ItemsSource = Enum.GetValues<SchemeVariant>();
        ClockFormatBox.ItemsSource = new[] { "24h", "12h" };
        WorkspacesBox.ItemsSource = new[] { "dots", "numbers", "shapes", "icons" };
        FitBox.ItemsSource = new[] { "fill", "fit", "stretch", "center", "span" };
        PowerBox.ItemsSource = new[] { "balanced", "saver", "performance" };

        LoadIntoControls();
    }

    private void LoadIntoControls()
    {
        _loading = true;
        try
        {
            var a = _config.Appearance;
            var b = _config.Bar;
            var n = _config.Notifications;
            var l = _config.Launcher;
            var s = _config.Services;

            DarkSwitch.IsOn = a.Mode.Equals("dark", StringComparison.OrdinalIgnoreCase) || _theme.Current.IsDark;
            SeedBox.Text = _theme.Current.Seed.ToHex();
            VariantBox.SelectedItem = _theme.Current.Variant;
            ScaleSlider.Value = Math.Clamp(a.Scale, 0.8, 1.6);
            BlurSlider.Value = Math.Clamp(a.BlurRadius, 0, 64);
            TransparencySwitch.IsOn = a.Transparency;
            SystemAccentSwitch.IsOn = a.UseSystemAccent;

            AnimationSlider.Value = Math.Clamp(a.AnimationScale, 0, 2);
            RoundingSlider.Value = Math.Clamp(a.RoundingScale, 0, 2);
            FontScaleSlider.Value = Math.Clamp(a.FontScale, 0.7, 1.8);

            HeightSlider.Value = Math.Clamp(b.Height, 24, 72);
            TraySwitch.IsOn = b.ShowTray;
            AllDisplaysSwitch.IsOn = b.ShowOnAllDisplays;
            SecondsSwitch.IsOn = b.ShowClockSeconds;
            ClockFormatBox.SelectedItem = b.ClockFormat;
            WorkspacesBox.SelectedItem = b.WorkspacesDisplay;
            WorkspaceCountSlider.Value = Math.Clamp(b.WorkspaceCount, 1, 12);

            DndSwitch.IsOn = n.DoNotDisturb;
            MirrorSwitch.IsOn = n.ListenSystemToasts;
            GroupSwitch.IsOn = n.GroupByApp;
            TimeoutSlider.Value = Math.Clamp(n.TimeoutSeconds, 2, 60);
            MaxVisibleSlider.Value = Math.Clamp(n.MaxVisible, 1, 10);

            FuzzySwitch.IsOn = l.Fuzzy;
            VimSwitch.IsOn = l.VimKeys;
            CalcSwitch.IsOn = l.LiveCalculator;
            MaxResultsSlider.Value = Math.Clamp(l.MaxResults, 3, 50);

            OsdSwitch.IsOn = _config.Osd.Enabled;
            OsdTimeoutSlider.Value = Math.Clamp(_config.Osd.TimeoutMs, 400, 6000);
            WeatherSlider.Value = Math.Clamp(s.Weather.RefreshMinutes, 5, 180);
            VisualizerSlider.Value = Math.Clamp(s.Audio.VisualizerBars, 8, 128);
            PollSlider.Value = Math.Clamp(s.PollIntervalMs, 250, 5000);

            BackgroundSwitch.IsOn = _config.Background.Enabled;
            DeskClockSwitch.IsOn = _config.Background.Clock;
            VisSwitch.IsOn = _config.Background.Visualiser;
            DimSlider.Value = Math.Clamp(_config.Background.Dim, 0, 0.8);
            FitBox.SelectedItem = WallpaperLibrary.ToConfigString(WallpaperLibrary.ParseFit(_config.Wallpaper.Fit));

            PowerBox.SelectedItem = _config.Power.Profile;

            PathText.Text = _store.ConfigPath;

            var hotkeySummary = _hotkeys > 0
                ? $"{_hotkeys} global hotkey(s) bound: Ctrl+Alt+Space launcher, +D dashboard, +C control centre, +N notifications, +W wallpaper, +Q session."
                : "No global hotkeys bound.";
            HotkeyText.Text = _hotkeyFailures.Count == 0
                ? hotkeySummary
                : hotkeySummary + "\n" + string.Join("\n", _hotkeyFailures.Select(f => "· " + f));

            StatusText.Text = string.Empty;
            DoctorText.Text = "Not checked yet.";
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnApplySeed(object sender, RoutedEventArgs e)
    {
        if (!TryParseHex(SeedBox.Text, out var color))
        {
            StatusText.Text = "accent must be #RRGGBB";
            return;
        }

        var variant = VariantBox.SelectedItem is SchemeVariant v ? v : _theme.Current.Variant;
        _theme.Apply(new RgbSeed(color.R, color.G, color.B), DarkSwitch.IsOn, variant);
        StatusText.Text = "accent applied";
    }

    private static bool TryParseHex(string? text, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6) return false;

        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return false;

        color = (r, g, b);
        return true;
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        _config = _store.Load();
        LoadIntoControls();
        StatusText.Text = "reloaded from disk";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var a = _config.Appearance;
        a.Mode = DarkSwitch.IsOn ? "dark" : "light";
        a.Scale = Math.Round(ScaleSlider.Value, 2);
        a.BlurRadius = (int)Math.Round(BlurSlider.Value);
        a.Transparency = TransparencySwitch.IsOn;
        a.UseSystemAccent = SystemAccentSwitch.IsOn;
        a.AnimationScale = Math.Round(AnimationSlider.Value, 2);
        a.RoundingScale = Math.Round(RoundingSlider.Value, 2);
        a.FontScale = Math.Round(FontScaleSlider.Value, 2);

        var b = _config.Bar;
        b.Height = (int)Math.Round(HeightSlider.Value);
        b.ShowTray = TraySwitch.IsOn;
        b.ShowOnAllDisplays = AllDisplaysSwitch.IsOn;
        b.ShowClockSeconds = SecondsSwitch.IsOn;
        if (ClockFormatBox.SelectedItem is string clock) b.ClockFormat = clock;
        if (WorkspacesBox.SelectedItem is string display) b.WorkspacesDisplay = display;
        b.WorkspaceCount = (int)Math.Round(WorkspaceCountSlider.Value);

        var n = _config.Notifications;
        n.DoNotDisturb = DndSwitch.IsOn;
        n.ListenSystemToasts = MirrorSwitch.IsOn;
        n.GroupByApp = GroupSwitch.IsOn;
        n.TimeoutSeconds = (int)Math.Round(TimeoutSlider.Value);
        n.MaxVisible = (int)Math.Round(MaxVisibleSlider.Value);

        var l = _config.Launcher;
        l.Fuzzy = FuzzySwitch.IsOn;
        l.VimKeys = VimSwitch.IsOn;
        l.LiveCalculator = CalcSwitch.IsOn;
        l.MaxResults = (int)Math.Round(MaxResultsSlider.Value);

        _config.Osd.Enabled = OsdSwitch.IsOn;
        _config.Osd.TimeoutMs = (int)Math.Round(OsdTimeoutSlider.Value);

        var s = _config.Services;
        s.Weather.RefreshMinutes = (int)Math.Round(WeatherSlider.Value);
        s.Audio.VisualizerBars = (int)Math.Round(VisualizerSlider.Value);
        s.PollIntervalMs = (int)Math.Round(PollSlider.Value);

        var bg = _config.Background;
        bg.Enabled = BackgroundSwitch.IsOn;
        bg.Clock = DeskClockSwitch.IsOn;
        bg.Visualiser = VisSwitch.IsOn;
        bg.Dim = Math.Round(DimSlider.Value, 2);

        if (FitBox.SelectedItem is string fit) _config.Wallpaper.Fit = fit;

        if (VariantBox.SelectedItem is SchemeVariant variant)
            _theme.SetVariant(variant);

        try
        {
            _store.SavePreservingUnknown(_config.Normalized());
            _theme.SetMode(DarkSwitch.IsOn);

            // Power is an OS setting, not just a config value: apply it now, and only record it if
            // the machine accepted it.
            if (PowerBox.SelectedItem is string power && _applyPower(power))
                _config.Power.Profile = power;

            StatusText.Text = "saved — the running shell reloads from its watcher";
        }
        catch (Exception ex)
        {
            StatusText.Text = "save failed: " + ex.Message;
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(_store.ConfigPath);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                DoctorText.Text = "config folder does not exist yet";
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DoctorText.Text = "could not open the folder: " + ex.Message;
        }
    }

    private void OnRunDoctor(object sender, RoutedEventArgs e)
    {
        var report = new ConfigDoctor().Scan(ReadConfigTextSafe(), _store.ConfigPath);
        DoctorText.Text = report.Fixes.Count > 0
            ? report.Summary + "\n" + string.Join("\n", report.Fixes.Select(f => "· " + f))
            : report.Summary;

        if (report.Findings.Count > 0)
        {
            DoctorText.Text += "\n" + string.Join("\n", report.Findings.Take(8)
                .Select(f => $"· [{f.Severity}] {f.Path}: {f.Message}"));
        }
    }

    private void OnFix(object sender, RoutedEventArgs e)
    {
        var doctor = new ConfigDoctor();
        var (json, report) = doctor.Fix(ReadConfigTextSafe());

        try
        {
            File.WriteAllText(_store.ConfigPath, json);
            _config = _store.Load();
            LoadIntoControls();
            DoctorText.Text = report.Fixes.Count == 0
                ? "nothing to fix — " + report.Summary
                : "fixed " + report.Fixes.Count + " issue(s):\n" + string.Join("\n", report.Fixes.Select(f => "· " + f));
        }
        catch (Exception ex)
        {
            DoctorText.Text = "could not write config: " + ex.Message;
        }
    }

    private void OnDiagnostics(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = ConfigStore.DefaultRoot();
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"radha-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            new DiagnosticsBundle()
                .WithEnvironment(configPath: _store.ConfigPath, logDirectory: Path.Combine(root, "logs"))
                .WithTextFile("config/shell.json", _store.ConfigPath)
                .WithTextFile("logs/ipc-audit.log", Path.Combine(root, "logs", "ipc-audit.log"))
                .WithReport($"hotkeys bound: {_hotkeys}\nfailures: {string.Join("; ", _hotkeyFailures)}")
                .Build(path);

            DoctorText.Text = "bundle written to " + path;
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DoctorText.Text = "bundle failed: " + ex.Message;
        }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        UpdateText.Text = "checking…";
        try
        {
            UpdateText.Text = await _updateCheck();
        }
        catch (Exception ex)
        {
            UpdateText.Text = "check failed: " + ex.Message;
        }
    }

    private string ReadConfigTextSafe()
    {
        try { return File.Exists(_store.ConfigPath) ? File.ReadAllText(_store.ConfigPath) : "{}"; }
        catch { return "{}"; }
    }
}
