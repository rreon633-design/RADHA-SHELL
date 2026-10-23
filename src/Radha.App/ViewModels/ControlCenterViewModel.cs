using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radha.Core.Services;
using Radha.Core.Theming;

namespace Radha.App.ViewModels;

/// <summary>Quick settings: tiles, sliders and the dynamic-theme switcher.</summary>
public sealed partial class ControlCenterViewModel : ObservableObject
{
    private readonly IThemeService _theme;
    private readonly IWallpaperService _wallpaper;
    private readonly INetworkService _network;
    private readonly IBluetoothService _bluetooth;
    private readonly IAudioService _audio;
    private readonly Action<string, string, string[]> _runCommand;
    private bool _initialized;

    [ObservableProperty] private double _volume;
    [ObservableProperty] private double _brightness = 70;
    [ObservableProperty] private double _microphone;
    [ObservableProperty] private bool _isWifiOn;
    [ObservableProperty] private bool _isBluetoothOn;
    [ObservableProperty] private string _networkName = "Ethernet";
    [ObservableProperty] private string _selectedVariant = "TonalSpot";
    [ObservableProperty] private bool _isDark = true;

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
        Action<string, string, string[]> runCommand)
    {
        State = state;
        _theme = theme;
        _wallpaper = wallpaper;
        _network = network;
        _bluetooth = bluetooth;
        _audio = audio;
        _runCommand = runCommand;

        var tiles = new (string Label, string Glyph, bool On)[]
        {
            ("Wi-Fi", "\uE701", network.Current.Connected),
            ("Bluetooth", "\uE702", bluetooth.Enabled),
            ("Do not disturb", "\uE7ED", state.IsDnd),
            ("Night light", "\uE708", false),
            ("Dark mode", "\uE793", state.Theme.IsDark),
            ("Screen record", "\uE7C8", false),
            ("Airplane", "\uE709", false),
            ("Project", "\uE7F4", false),
            ("Nearby share", "\uE8FA", false),
            ("Energy saver", "\uE945", false),
            ("Accessibility", "\uE776", false),
            ("Rotation lock", "\uE7E6", false)
        };

        foreach (var (label, glyph, on) in tiles)
            Tiles.Add(new TileViewModel(label, glyph, on, OnTileToggled));

        Volume = _audio.Volume;
        Microphone = _audio.MicMuted ? 0 : 100;
        IsWifiOn = network.Current.Connected;
        NetworkName = network.Current.Ssid ?? network.Current.Interface;
        IsBluetoothOn = bluetooth.Enabled;
        SelectedVariant = theme.Current.Variant.ToString();
        IsDark = theme.Current.IsDark;

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
        };
        bluetooth.Changed += (_, _) => IsBluetoothOn = bluetooth.Enabled;

        _initialized = true;
    }

    partial void OnSelectedVariantChanged(string value)
    {
        if (!_initialized) return;
        if (Enum.TryParse<SchemeVariant>(value, ignoreCase: true, out var parsed))
            _theme.SetVariant(parsed);
    }

    partial void OnVolumeChanged(double value)
    {
        _audio.SetVolume((int)Math.Round(value));
        if (_initialized) State.ShowOsd("Volume", value);
    }

    partial void OnMicrophoneChanged(double value)
    {
        if (_initialized) State.ShowOsd("Microphone", value);
    }

    partial void OnBrightnessChanged(double value)
    {
        if (_initialized) State.ShowOsd("Brightness", value);
    }

    private void OnTileToggled(TileViewModel tile)
    {
        switch (tile.Label)
        {
            case "Do not disturb":
                State.IsDnd = tile.IsOn;
                break;
            case "Dark mode":
                _theme.SetMode(tile.IsOn);
                IsDark = tile.IsOn;
                break;
            case "Wi-Fi":
                _ = _network.ToggleWifiAsync();
                break;
            case "Bluetooth":
                _ = _bluetooth.ToggleAsync();
                break;
        }
    }

    [RelayCommand]
    private void SelectVariant(string? variant)
    {
        if (string.IsNullOrWhiteSpace(variant)) return;
        if (!Enum.TryParse<SchemeVariant>(variant, ignoreCase: true, out var parsed)) return;
        SelectedVariant = parsed.ToString();
        _theme.SetVariant(parsed);
    }

    [RelayCommand]
    private void ToggleDark() => _theme.SetMode(!_theme.Current.IsDark);

    [RelayCommand]
    private void NextWallpaper() => _runCommand("wallpaper", "next", Array.Empty<string>());

    [RelayCommand]
    private async Task RefreshWallpaperAsync()
    {
        var seed = await _wallpaper.NextAsync();
        if (seed.R == 0 && seed.G == 0 && seed.B == 0) return;
        _theme.Apply(seed, _theme.Current.IsDark, _theme.Current.Variant);
    }
}
