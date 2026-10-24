using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Radha.Core.Launcher;
using Radha.Core.Search;
using Radha.Core.Services;

namespace Radha.App.ViewModels;

/// <summary>A quick-settings / control-centre tile.</summary>
public sealed partial class TileViewModel : ObservableObject
{
    private readonly Action<TileViewModel>? _onToggle;

    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _glyph;
    [ObservableProperty] private string _detail = "";

    /// <summary>False when this machine cannot do what the tile offers; the tile greys out.</summary>
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private SolidColorBrush _tileBrush = new(Microsoft.UI.Colors.Transparent);
    [ObservableProperty] private SolidColorBrush _iconBackground = new(Microsoft.UI.Colors.Transparent);
    [ObservableProperty] private SolidColorBrush _labelBrush = new(Microsoft.UI.Colors.White);
    [ObservableProperty] private SolidColorBrush _iconBrush = new(Microsoft.UI.Colors.White);

    public TileViewModel(string label, string glyph, bool isOn = false, Action<TileViewModel>? onToggle = null)
    {
        _label = label;
        _glyph = glyph;
        _isOn = isOn;
        _onToggle = onToggle;
    }

    /// <summary>Called by the owning view-model whenever the dynamic scheme changes.</summary>
    public void ApplyTheme(ThemeViewModel theme)
    {
        TileBrush = IsOn ? theme.PrimaryContainer : theme.TileOff;
        IconBackground = IsOn ? theme.Primary : theme.SurfaceContainerHighest;
        IconBrush = IsOn ? theme.OnPrimary : theme.OnSurface;
        LabelBrush = IsOn ? theme.OnPrimaryContainer : theme.OnSurface;
    }

    public void Toggle()
    {
        IsOn = !IsOn;
        _onToggle?.Invoke(this);
    }
}

public sealed partial class WeatherHourVm : ObservableObject
{
    [ObservableProperty] private SolidColorBrush _labelBrush = new(Microsoft.UI.Colors.Gray);
    [ObservableProperty] private SolidColorBrush _valueBrush = new(Microsoft.UI.Colors.White);

    public WeatherHourVm(string label, string glyph, string tempText, double barHeight)
    {
        Label = label;
        Glyph = glyph;
        TempText = tempText;
        BarHeight = barHeight;
    }

    public string Label { get; }
    public string Glyph { get; }
    public string TempText { get; }
    public double BarHeight { get; }

    public void ApplyTheme(ThemeViewModel theme)
    {
        LabelBrush = theme.OnSurfaceVariant;
        ValueBrush = theme.OnSurface;
    }
}

/// <summary>Backs the dashboard: performance graphs, weather, quick tiles and now-playing.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private const int HistoryLength = 60;

    private readonly ISystemMonitorService _system;
    private readonly ShellState _state;
    private IDisposable? _monitorLease;
    private readonly IWeatherService _weather;
    private readonly IMediaService _media;
    private readonly IAudioService _audio;
    private readonly IThemeService _theme;
    private bool _initialized;

    [ObservableProperty] private string _cpuText = "0%";
    [ObservableProperty] private string _gpuText = "0%";
    [ObservableProperty] private string _memoryText = "0 / 0 GB";
    [ObservableProperty] private string _storageText = "0%";
    [ObservableProperty] private string _networkText = "0 kb/s";
    [ObservableProperty] private string _temperatureText = "--°";
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private double _storagePercent;
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _gpuPercent;

    [ObservableProperty] private string _weatherCity = "Locating…";
    [ObservableProperty] private string _weatherTemp = "--°";
    [ObservableProperty] private string _weatherCondition = "";
    [ObservableProperty] private string _weatherHighLow = "";
    [ObservableProperty] private string _weatherGlyph = "\uE753";

    [ObservableProperty] private string _trackTitle = "Nothing playing";
    [ObservableProperty] private string _trackArtist = "";
    [ObservableProperty] private string _trackAlbum = "";
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isMediaTab;
    [ObservableProperty] private int _activeTab;      // 0 overview · 1 media · 2 performance
    [ObservableProperty] private double _volume = 50;
    [ObservableProperty] private double _brightness = 70;
    [ObservableProperty] private double _microphone = 100;

    public ShellState State { get; }

    public bool IsOverviewTab => ActiveTab == 0;
    public bool IsPerformanceTab => ActiveTab == 2;

    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> MemoryHistory { get; } = new();
    public ObservableCollection<double> Spectrum { get; } = new();
    public ObservableCollection<WeatherHourVm> WeatherHours { get; } = new();
    public ObservableCollection<TileViewModel> QuickTiles { get; } = new();

    public DashboardViewModel(
        ShellState state,
        ISystemMonitorService system,
        IWeatherService weather,
        IMediaService media,
        IAudioService audio,
        IThemeService theme)
    {
        State = state;
        _system = system;
        _weather = weather;
        _media = media;
        _audio = audio;
        _theme = theme;

        for (var i = 0; i < HistoryLength; i++)
        {
            CpuHistory.Add(0);
            GpuHistory.Add(0);
            MemoryHistory.Add(0);
        }
        for (var i = 0; i < 48; i++) Spectrum.Add(0);

        QuickTiles.Add(new TileViewModel("Do not disturb", "\uE7ED", state.IsDnd, _ =>
        {
            state.IsDnd = !state.IsDnd;
        }));
        QuickTiles.Add(new TileViewModel("Night light", "\uE708"));
        QuickTiles.Add(new TileViewModel("Dark mode", "\uE793", state.Theme.IsDark, tile =>
        {
            _theme.SetMode(!tile.IsOn);
        }));
        QuickTiles.Add(new TileViewModel("Bluetooth", "\uE702"));
        QuickTiles.Add(new TileViewModel("Wi-Fi", "\uE701", true));
        QuickTiles.Add(new TileViewModel("Project", "\uE7F4"));

        _system.Sampled += (_, sample) => ApplySample(sample);
        _weather.Updated += (_, now) => ApplyWeather(now);
        _media.Changed += (_, _) => SyncMedia();
        Volume = _audio.Volume;
        Microphone = _audio.MicMuted ? 0 : 100;
        _initialized = true;

        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellState.IsDashboardOpen))
            {
                UpdateSampling();
                return;
            }
            if (e.PropertyName != nameof(ShellState.Theme)) return;
            foreach (var tile in QuickTiles) tile.ApplyTheme(state.Theme);
            foreach (var hour in WeatherHours) hour.ApplyTheme(state.Theme);
        };
        foreach (var tile in QuickTiles) tile.ApplyTheme(state.Theme);
    }

    public async Task InitializeAsync()
    {
        try { await _weather.RefreshAsync(); } catch { /* offline is fine */ }
        SyncMedia();
    }

    /// <summary>
    /// Sensor sampling is the most expensive recurring work in the process — LibreHardwareMonitor
    /// walks SMBus and opens device handles — so the dashboard holds a lease only while it is on
    /// screen. The service drops to a slow heartbeat when the last lease goes away, which means an
    /// idle shell does not cook a laptop to draw a graph nobody is looking at.
    /// </summary>
    private void UpdateSampling()
    {
        if (_state.IsDashboardOpen)
        {
            _monitorLease ??= _system.Subscribe();
        }
        else
        {
            _monitorLease?.Dispose();
            _monitorLease = null;
        }
    }

    partial void OnActiveTabChanged(int value)
    {
        IsMediaTab = value == 1;
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsPerformanceTab));
    }

    partial void OnVolumeChanged(double value)
    {
        _audio.SetVolume((int)Math.Round(value));
        if (_initialized) State.ShowOsd("Volume", value);
    }

    partial void OnBrightnessChanged(double value)
    {
        if (_initialized) State.ShowOsd("Brightness", value);
    }

    partial void OnMicrophoneChanged(double value)
    {
        if (_initialized) State.ShowOsd("Microphone", value);
    }

    [RelayCommand]
    private void SelectTab(string index) => ActiveTab = int.TryParse(index, out var i) ? i : 0;

    [RelayCommand]
    private async Task RefreshWeatherAsync() => await _weather.RefreshAsync();

    private void ApplySample(SystemSample sample)
    {
        CpuPercent = sample.CpuPercent;
        GpuPercent = sample.GpuPercent;
        CpuText = $"{sample.CpuPercent:F0}%";
        GpuText = $"{sample.GpuPercent:F0}%";
        MemoryText = $"{sample.MemoryUsedGb:F1} / {sample.MemoryTotalGb:F0} GB";
        MemoryPercent = sample.MemoryTotalGb > 0 ? sample.MemoryUsedGb / sample.MemoryTotalGb * 100 : 0;
        StoragePercent = sample.StorageUsedPercent;
        StorageText = $"{sample.StorageUsedPercent:F0}%";
        NetworkText = sample.NetworkDownKbps >= 1024
            ? $"{sample.NetworkDownKbps / 1024:F1} MB/s"
            : $"{sample.NetworkDownKbps:F0} kb/s";
        TemperatureText = sample.CpuTemperatureC is { } t ? $"{t:F0}°" : "--°";

        Push(CpuHistory, sample.CpuPercent);
        Push(GpuHistory, sample.GpuPercent);
        Push(MemoryHistory, MemoryPercent);

        if (sample.BatteryPercent is { } battery)
        {
            State.SetBattery(battery, sample.BatteryCharging);
        }

        var spectrum = _audio.GetSpectrum();
        if (!spectrum.IsEmpty)
        {
            var bars = Spectrum.Count;
            var bucket = Math.Max(1, spectrum.Length / Math.Max(1, bars));
            for (var i = 0; i < bars; i++)
            {
                var sum = 0f;
                for (var k = 0; k < bucket && i * bucket + k < spectrum.Length; k++)
                    sum += spectrum[i * bucket + k];
                Spectrum[i] = Math.Clamp(sum / bucket * 6.0, 0, 1);
            }
        }
    }

    private void ApplyWeather(WeatherNow now)
    {
        if (now.IsEmpty) return;
        WeatherCity = now.Location;
        WeatherTemp = $"{now.TemperatureC:F0}°";
        WeatherCondition = now.Condition;
        WeatherHighLow = $"H {now.HighC:F0}° · L {now.LowC:F0}°";
        WeatherGlyph = GlyphFor(now.IconKey);

        WeatherHours.Clear();
        WeatherHours.Clear();
        foreach (var hour in now.Hours.Take(5))
        {
            var item = new WeatherHourVm(hour.Label, GlyphFor(hour.IconKey), $"{hour.TempC:F0}°",
                Math.Clamp((hour.TempC - 5) * 2.2, 8, 42));
            item.ApplyTheme(State.Theme);
            WeatherHours.Add(item);
        }
    }

    private void SyncMedia()
    {
        var track = _media.Current;
        TrackTitle = track.IsEmpty ? "Nothing playing" : track.Title;
        TrackArtist = track.IsEmpty ? "" : track.Artist;
        TrackAlbum = track.IsEmpty ? "" : track.Album;
        IsPlaying = _media.State == PlaybackState.Playing;
        DurationText = Format(track.Duration);
        PositionText = Format(_media.Position);
        ProgressPercent = track.Duration.TotalSeconds > 0
            ? Math.Clamp(_media.Position.TotalSeconds / track.Duration.TotalSeconds * 100, 0, 100)
            : 0;
    }

    private static string Format(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";

    /// <summary>Weather icon keys come from Radha.Core.Weather.WeatherCodes.</summary>
    private static string GlyphFor(string key) => key switch
    {
        "clear" => "\uE706",
        "partly" => "\uE707",
        "cloudy" => "\uE753",
        "rain" => "\uE9C4",
        "storm" => "\uE9C6",
        "snow" => "\uE9C8",
        "fog" => "\uE9CB",
        _ => "\uE753"
    };

    private static void Push(ObservableCollection<double> history, double value)
    {
        history.RemoveAt(0);
        history.Add(value);
    }
}

/// <summary>Launcher: Start-menu app index, fuzzy search, action prefixes and a live calculator.</summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    private readonly List<AppEntry> _apps = new();
    private readonly ShellState _state;
    private readonly Action<string, string> _runCommand;

    public ShellState State => _state;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string? _calculation;
    [ObservableProperty] private int _selectedIndex;
    [ObservableProperty] private bool _isActionMode;
    [ObservableProperty] private bool _hasCalculation;

    public ObservableCollection<LauncherItemVm> Results { get; } = new();

    public LauncherViewModel(ShellState state, Action<string, string> runCommand)
    {
        _state = state;
        _runCommand = runCommand;
        ReloadApps();
        Refresh();

        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellState.Theme)) return;
            foreach (var item in Results) item.ApplyTheme(state.Theme);
        };
    }

    public void ReloadApps()
    {
        _apps.Clear();
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                 })
        {
            var programs = Path.Combine(root, "Programs");
            if (!Directory.Exists(programs)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories))
                    _apps.Add(new AppEntry(Path.GetFileNameWithoutExtension(file), file));
            }
            catch (UnauthorizedAccessException) { }
        }
        _apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    partial void OnQueryChanged(string value) => Refresh();

    partial void OnCalculationChanged(string? value) => HasCalculation = !string.IsNullOrWhiteSpace(value);

    private void Refresh()
    {
        Results.Clear();
        var query = Query.Trim();
        IsActionMode = query.StartsWith('>');
        Calculation = null;

        if (query.Length == 0)
        {
            SetResults(_apps.Take(12));
            return;
        }

        if (query.StartsWith('>'))
        {
            var actions = ActionCatalog.All
                .Where(action => Matches(action.Title, query[1..]))
                .Select(action => new AppEntry(action.Title, action.Glyph, action.Module, action.Command));
            SetResults(actions);
            return;
        }

        // Live calculator. Radha.Core owns the parser: DataTable.Compute needed "^^" for power and
        // silently disagreed with ^ on everything else.
        var expression = query.Replace("×", "*").Replace("÷", "/");
        if (Calculator.LooksLikeExpression(expression) && Calculator.TryEvaluate(expression, out var value))
            Calculation = Calculator.Format(value);

        SetResults(Score(query).Take(12));
    }

    private void SetResults(IEnumerable<AppEntry> entries)
    {
        Results.Clear();
        foreach (var entry in entries) Results.Add(new LauncherItemVm(entry, _state.Theme));
    }

    /// <summary>Delegates to the shared, unit-tested matcher in Radha.Core.</summary>
    private IEnumerable<AppEntry> Score(string query) =>
        FuzzyMatcher.Rank(_apps, query, app => app.Name, 12);

    private static bool Matches(string title, string query) =>
        title.Contains(query, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void Activate(LauncherItemVm? item)
    {
        if (item is null) return;
        var entry = item.Entry;
        if (entry.IsAction)
        {
            _runCommand(entry.Module!, entry.Command!);
            _state.CloseAll();
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch { /* app vanished between scan and launch */ }

        _state.CloseAll();
        Query = "";
    }
}

/// <summary>Launcher row with pre-resolved brushes so the DataTemplate can follow the theme.</summary>
public sealed partial class LauncherItemVm : ObservableObject
{
    [ObservableProperty] private SolidColorBrush _nameBrush = new(Microsoft.UI.Colors.White);
    [ObservableProperty] private SolidColorBrush _subtitleBrush = new(Microsoft.UI.Colors.Gray);
    [ObservableProperty] private SolidColorBrush _iconBackground = new(Microsoft.UI.Colors.Transparent);
    [ObservableProperty] private SolidColorBrush _iconForeground = new(Microsoft.UI.Colors.White);

    public LauncherItemVm(AppEntry entry, ThemeViewModel theme)
    {
        Entry = entry;
        ApplyTheme(theme);
    }

    public AppEntry Entry { get; }
    public string Name => Entry.Name;
    public string Subtitle => Entry.Subtitle;
    public string Glyph => Entry.Glyph;

    public void ApplyTheme(ThemeViewModel theme)
    {
        NameBrush = theme.OnSurface;
        SubtitleBrush = theme.OnSurfaceVariant;
        IconBackground = theme.PrimaryContainer;
        IconForeground = theme.OnPrimaryContainer;
    }
}

public sealed record AppEntry(string Name, string Path, string? Module = null, string? Command = null)
{
    public int Score { get; init; }
    public bool IsAction => Module is not null && Command is not null;
    public string Glyph => IsAction ? "\uE756" : "\uE7C3";
    public string Subtitle => IsAction ? $"> {Module} {Command}" : Path;
}

public sealed record LauncherAction(string Title, string Glyph, string Module, string Command);

public static class ActionCatalog
{
    public static readonly LauncherAction[] All =
    {
        new("Dashboard", "\uE80F", "dashboard", "toggle"),
        new("Control centre", "\uE713", "controlcenter", "toggle"),
        new("Notifications", "\uEA8F", "notifications", "toggle"),
        new("Wallpaper next", "\uE91B", "wallpaper", "next"),
        new("Theme: vibrant", "\uE790", "theme", "variant,vibrant"),
        new("Theme: tonal spot", "\uE790", "theme", "variant,tonalspot"),
        new("Theme: monochrome", "\uE790", "theme", "variant,monochrome"),
        new("Toggle dark mode", "\uE793", "theme", "toggle"),
        new("Session menu", "\uE7E8", "session", "menu"),
        new("Reload config", "\uE777", "config", "reload")
    };
}
