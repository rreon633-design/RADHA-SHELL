using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radha.App.Services.Win;
using Radha.Core.Services;
using Radha.Core.Theming;

namespace Radha.App.ViewModels;

/// <summary>
/// Control centre: the three mixers, twelve toggles and the dynamic-theme card.
///
/// Every tile maps to something the machine can actually do. Where Windows has no API the tile
/// opens the settings page that owns the behaviour, and the ones that depend on hardware the
/// machine may not have (brightness on an external panel, radio switches on a desktop) disable
/// themselves with the reason on the label instead of flipping a decoration.
/// </summary>
public sealed partial class ControlCenterViewModel : ObservableObject
{
    private readonly IThemeService _theme;
    private readonly IWallpaperService _wallpaper;
    private readonly INetworkService _network;
    private readonly IBluetoothService _bluetooth;
    private readonly IAudioService _audio;
    private readonly IWorkspaceService _workspaces;
    private readonly Action<string, string, string[]> _runCommand;
    private readonly Action _openSettings;
    private readonly Action _openWallpaperPicker;
    private readonly Action _quit;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private bool _initialized;

    [ObservableProperty] private double _volume;
    [ObservableProperty] private double _brightness = 70;
    [ObservableProperty] private double _microphone;
    [ObservableProperty] private bool _isWifiOn;
    [ObservableProperty] private bool _isBluetoothOn;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isMicMuted;
    [ObservableProperty] private bool _brightnessAvailable;
    [ObservableProperty] private string _networkName = "Ethernet";
    [ObservableProperty] private string _selectedVariant = "TonalSpot";
    [ObservableProperty] private bool _isDark = true;
    [ObservableProperty] private string _volumeText = "0";
    [ObservableProperty] private string _brightnessText = "—";
    [ObservableProperty] private string _microphoneText = "On";
    [ObservableProperty] private string _variantLabel = "Material 3 · TonalSpot";

    public ShellState State { get; }
    public ObservableCollection<TileViewModel> Tiles { get; } = new();
    public ObservableCollection<string> Variants { get; } = new()
    {
        "TonalSpot", "Vibrant", "Expressive", "Fidelity", "Content", "Neutral", "Monochrome"
    };

    public ControlCenterViewModel(
        ShellState state,
        IThemeService theme,
        IWallpaperService wallpaper,
        INetworkService network,
        IBluetoothService bluetooth,
        IAudioService audio,
        IWorkspaceService workspaces,
        Action<string, string, string[]> runCommand,
        Action openSettings,
        Action openWallpaperPicker,
        Action quit)
    {
        State = state;
        _theme = theme;
        _wallpaper = wallpaper;
        _network = network;
        _bluetooth = bluetooth;
        _audio = audio;
        _workspaces = workspaces;
        _runCommand = runCommand;
        _openSettings = openSettings;
        _openWallpaperPicker = openWallpaperPicker;
        _quit = quit;

        // Optimistic until the panel opens: the probe starts a child process, and the boot path is
        // not the place for one (see WorkspaceProbe for what a native call on that path costs).
        BrightnessAvailable = true;

        Tiles.Add(Tile("Wi-Fi", "\uE701", network.Current.Connected, true,
            network.Current.Connected ? "Connected" : "Off"));
        Tiles.Add(Tile("Bluetooth", "\uE702", bluetooth.Enabled, true, bluetooth.Enabled ? "On" : "Off"));
        Tiles.Add(Tile("Do not disturb", "\uE7ED", state.IsDnd, true, state.IsDnd ? "On" : "Off"));
        Tiles.Add(Tile("Night light", "\uE708", false, true, "Warm colour"));
        Tiles.Add(Tile("Dark mode", "\uE793", theme.Current.IsDark, true, theme.Current.IsDark ? "Dark" : "Light"));
        Tiles.Add(Tile("Screen record", "\uE7C8", false, true, "Snipping Tool"));
        Tiles.Add(Tile("Airplane", "\uE709", false, network.Current.Connected || bluetooth.Enabled, "Radios off"));
        Tiles.Add(Tile("Project", "\uE7F4", false, true, "Win + P"));
        Tiles.Add(Tile("Nearby share", "\uE8FA", false, true, "Settings"));
        Tiles.Add(Tile("Energy saver", "\uE945", false, true, "Power mode"));
        Tiles.Add(Tile("Accessibility", "\uE776", false, true, "Settings"));
        Tiles.Add(Tile("Rotation lock", "\uE7E6", false, true, "Auto-rotate"));

        Volume = _audio.Volume;
        VolumeText = $"{_audio.Volume}";
        IsMuted = _audio.Muted;
        IsMicMuted = _audio.MicMuted;
        Microphone = _audio.MicMuted ? 0 : 100;
        MicrophoneText = _audio.MicMuted ? "Muted" : "On";
        IsWifiOn = network.Current.Connected;
        NetworkName = network.Current.Ssid ?? network.Current.Interface;
        IsBluetoothOn = bluetooth.Enabled;
        SelectedVariant = theme.Current.Variant.ToString();
        VariantLabel = $"Material 3 · {SelectedVariant}";
        IsDark = theme.Current.IsDark;

        BrightnessText = "—";
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        foreach (var tile in Tiles) tile.ApplyTheme(state.Theme);

        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellState.Theme)) return;
            foreach (var tile in Tiles) tile.ApplyTheme(state.Theme);
        };

        network.Changed += (_, n) =>
        {
            IsWifiOn = n.Connected;
            NetworkName = n.Ssid ?? n.Interface;
            Set("Wi-Fi", n.Connected, n.Connected ? "Connected" : "Off");
        };

        bluetooth.Changed += (_, _) =>
        {
            IsBluetoothOn = bluetooth.Enabled;
            Set("Bluetooth", bluetooth.Enabled, bluetooth.Enabled ? "On" : "Off");
        };

        _initialized = true;
    }

    private TileViewModel Tile(string label, string glyph, bool on, bool available, string detail = "")
    {
        var tile = new TileViewModel(label, glyph, on, OnTileToggled)
        {
            Detail = detail,
            IsAvailable = available
        };
        tile.ApplyTheme(State.Theme);
        return tile;
    }

    private void Set(string label, bool on, string detail)
    {
        var tile = Tiles.FirstOrDefault(t => t.Label == label);
        if (tile is null) return;
        tile.IsOn = on;
        tile.Detail = detail;
        tile.ApplyTheme(State.Theme);
    }

    // ---------------- sliders ----------------

    partial void OnVolumeChanged(double value)
    {
        var percent = (int)Math.Round(value);
        VolumeText = IsMuted ? "Muted" : $"{percent}";
        if (!_initialized) return;
        _audio.SetVolume(percent);
        State.ShowOsd("Volume", value);
    }

    partial void OnMicrophoneChanged(double value)
    {
        if (!_initialized) return;
        var muted = value <= 0;
        if (muted != _audio.MicMuted) _audio.ToggleMic();
        IsMicMuted = muted;
        MicrophoneText = muted ? "Muted" : "On";
        State.ShowOsd("Microphone", value);
    }

    partial void OnBrightnessChanged(double value)
    {
        if (!BrightnessAvailable) return;
        BrightnessText = $"{(int)Math.Round(value)}";
        if (!_initialized) return;
        QuickActions.SetBrightness((int)Math.Round(value));
        State.ShowOsd("Brightness", value);
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_audio.Muted != value) _audio.ToggleMute();
        VolumeText = value ? "Muted" : $"{(int)Math.Round(Volume)}";
    }

    partial void OnIsDarkChanged(bool value)
    {
        if (!_initialized) return;
        _theme.SetMode(value);
    }

    // ---------------- tiles ----------------

    private void OnTileToggled(TileViewModel tile)
    {
        switch (tile.Label)
        {
            case "Wi-Fi":
                _ = _network.ToggleWifiAsync();
                break;

            case "Bluetooth":
                _ = _bluetooth.ToggleAsync();
                break;

            case "Do not disturb":
                State.IsDnd = tile.IsOn;
                break;

            case "Dark mode":
                IsDark = tile.IsOn;
                _theme.SetMode(tile.IsOn);
                break;

            case "Night light":
                if (!QuickActions.SetNightLight(tile.IsOn))
                {
                    tile.IsOn = !tile.IsOn;
                    tile.Detail = "Unavailable";
                }
                break;

            case "Airplane":
                // Windows has no airplane-mode API; the documented equivalent is the radios.
                _ = SetAirplaneAsync(tile.IsOn);
                break;

            case "Energy saver":
                if (!QuickActions.SetEnergySaver(tile.IsOn))
                {
                    tile.IsOn = !tile.IsOn;
                    tile.Detail = "Unavailable";
                }
                break;

            case "Rotation lock":
                if (!QuickActions.SetRotationLocked(tile.IsOn)) tile.Detail = "No sensor";
                break;

            case "Project":
                QuickActions.OpenProject();
                tile.IsOn = false;
                break;

            case "Nearby share":
                QuickActions.OpenNearbySharing();
                tile.IsOn = false;
                break;

            case "Accessibility":
                QuickActions.OpenAccessibility();
                tile.IsOn = false;
                break;

            case "Screen record":
                QuickActions.OpenScreenRecorder();
                tile.IsOn = false;
                break;
        }

        tile.ApplyTheme(State.Theme);
    }

    private async Task SetAirplaneAsync(bool on)
    {
        if (on)
        {
            if (_network.Current.Connected) await _network.ToggleWifiAsync();
            if (_bluetooth.Enabled) await _bluetooth.ToggleAsync();
        }
        else
        {
            if (!_bluetooth.Enabled) await _bluetooth.ToggleAsync();
            if (!_network.Current.Connected) await _network.ToggleWifiAsync();
        }

        Set("Wi-Fi", _network.Current.Connected, _network.Current.Connected ? "Connected" : "Off");
        Set("Bluetooth", _bluetooth.Enabled, _bluetooth.Enabled ? "On" : "Off");
    }

    // ---------------- theme card ----------------

    partial void OnSelectedVariantChanged(string value)
    {
        VariantLabel = $"Material 3 · {value}";
        if (!_initialized) return;
        if (Enum.TryParse<SchemeVariant>(value, ignoreCase: true, out var parsed))
            _theme.SetVariant(parsed);
    }

    [RelayCommand]
    private void ToggleDark() => IsDark = !_theme.Current.IsDark;

    [RelayCommand]
    private async Task NextWallpaperAsync()
    {
        var seed = await _wallpaper.NextAsync();
        if (seed.R == 0 && seed.G == 0 && seed.B == 0) return;
        _theme.Apply(seed, _theme.Current.IsDark, _theme.Current.Variant);
    }

    [RelayCommand] private void OpenWallpaperPicker() => _openWallpaperPicker();
    [RelayCommand] private void OpenSettings() => _openSettings();
    [RelayCommand] private void Quit() => _quit();

    /// <summary>
    /// Called by the host when the panel opens, so the mixers are never stale. This is also where
    /// the display is probed: the answer costs a child process on the runner and a few milliseconds
    /// on a laptop, and paying it once when the panel opens is cheaper than paying it on every boot.
    /// </summary>
    public void Refresh()
    {
        Volume = _audio.Volume;
        IsMuted = _audio.Muted;
        IsMicMuted = _audio.MicMuted;
        Microphone = _audio.MicMuted ? 0 : 100;
        IsWifiOn = _network.Current.Connected;
        NetworkName = _network.Current.Ssid ?? _network.Current.Interface;
        IsBluetoothOn = _bluetooth.Enabled;
        IsDark = _theme.Current.IsDark;
        SelectedVariant = _theme.Current.Variant.ToString(CultureInfo.InvariantCulture);

        // Off the UI thread: the probe is a process launch, and the panel should not wait for it.
        _ = Task.Run(() =>
        {
            var (supported, value) = QuickActions.ProbeDisplay();
            void Apply()
            {
                BrightnessAvailable = supported;
                if (supported && value >= 0) Brightness = value;
                else BrightnessText = "—";
            }

            if (_dispatcher is null) Apply();
            else _dispatcher.TryEnqueue(Apply);
        });
    }
}
