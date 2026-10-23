using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Radha.Core.Config;
using Radha.Core.Diagnostics;
using Radha.Core.Services;
using Radha.Core.Theming;

namespace Radha.App.Views;

/// <summary>
/// Settings.
///
/// Deliberately control-driven rather than binding-driven: the window edits a strongly typed
/// <see cref="ShellConfig"/>, and a save goes through <see cref="ConfigStore.SavePreservingUnknown"/>
/// so keys this build does not model survive. The running shell picks the change up through its
/// config watcher, so nothing has to restart.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly ConfigStore _store;
    private readonly IThemeService _theme;
    private readonly Func<Task<string>> _updateCheck;
    private readonly int _hotkeys;
    private readonly IReadOnlyList<string> _hotkeyFailures;
    private ShellConfig _config;
    private bool _loading;

    public SettingsWindow(ConfigStore store, IThemeService theme, Func<Task<string>> updateCheck,
                          int hotkeys, IReadOnlyList<string> hotkeyFailures)
    {
        _store = store;
        _theme = theme;
        _updateCheck = updateCheck;
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
            appWindow.Resize(new Windows.Graphics.SizeInt32(760, 860));
        }
        catch (Exception) { /* sizing is cosmetic */ }

        VariantBox.ItemsSource = Enum.GetValues<SchemeVariant>();
        LoadIntoControls();
    }

    private void LoadIntoControls()
    {
        _loading = true;
        try
        {
            var a = _config.Appearance;
            var b = _config.Bar;

            DarkSwitch.IsOn = a.Mode.Equals("dark", StringComparison.OrdinalIgnoreCase) || _theme.Current.IsDark;
            SeedBox.Text = _theme.Current.Seed.ToHex();
            VariantBox.SelectedItem = _theme.Current.Variant;
            ScaleSlider.Value = Math.Clamp(a.Scale, 0.8, 1.6);
            BlurSlider.Value = Math.Clamp(a.BlurRadius, 0, 64);

            HeightSlider.Value = Math.Clamp(b.Height, 24, 72);
            TraySwitch.IsOn = b.ShowTray;
            AllDisplaysSwitch.IsOn = b.ShowOnAllDisplays;

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

        _config.Appearance.Mode = DarkSwitch.IsOn ? "dark" : "light";
        _config.Appearance.Scale = Math.Round(ScaleSlider.Value, 2);
        _config.Appearance.BlurRadius = (int)Math.Round(BlurSlider.Value);
        _config.Bar.Height = (int)Math.Round(HeightSlider.Value);
        _config.Bar.ShowTray = TraySwitch.IsOn;
        _config.Bar.ShowOnAllDisplays = AllDisplaysSwitch.IsOn;

        if (VariantBox.SelectedItem is SchemeVariant variant)
            _theme.SetVariant(variant);

        try
        {
            _store.SavePreservingUnknown(_config);
            _theme.SetMode(DarkSwitch.IsOn);
            StatusText.Text = "saved — running shell reloads from the watcher";
        }
        catch (Exception ex)
        {
            StatusText.Text = "save failed: " + ex.Message;
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
