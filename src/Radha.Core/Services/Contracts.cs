namespace Radha.Core.Services;

// Contracts implemented by Windows services in Radha.App (GSMTC, WASAPI, LHM,
// NetworkListManager, Windows.Devices.Radios, VirtualDesktop COM) and consumed by
// view-models plus the IPC handler. Keeping them here means the UI never talks to
// Win32/WinRT directly, and every service can be faked in tests.

public enum PlaybackState { Closed, Stopped, Paused, Playing }

public sealed record MediaTrack(
    string Title,
    string Artist,
    string Album,
    TimeSpan Duration,
    string AppId,
    string? ArtworkPath = null)
{
    public static readonly MediaTrack None = new("", "", "", TimeSpan.Zero, "");
    public bool IsEmpty => string.IsNullOrWhiteSpace(Title);
}

public sealed record SystemSample(
    double CpuPercent,
    double GpuPercent,
    double MemoryUsedGb,
    double MemoryTotalGb,
    double StorageUsedPercent,
    double NetworkDownKbps,
    double NetworkUpKbps,
    double? CpuTemperatureC,
    double? GpuTemperatureC,
    int? BatteryPercent,
    bool BatteryCharging,
    TimeSpan Uptime,
    DateTimeOffset Timestamp);

public sealed record NetworkState(
    bool Connected,
    string Interface,
    string? Ssid,
    int SignalStrength,   // 0..4
    bool Metered,
    string? VpnName,
    string Ipv4);

public sealed record BluetoothDevice(
    string Id,
    string Name,
    bool Connected,
    int? BatteryPercent,
    string? IconHint);

public sealed record WeatherNow(
    string Location,
    double TemperatureC,
    string Condition,
    string IconKey,
    double FeelsLikeC,
    int HumidityPercent,
    int WindKph,
    double HighC,
    double LowC)
{
    public IReadOnlyList<WeatherHour> Hours { get; init; } = Array.Empty<WeatherHour>();
    public bool IsEmpty => string.IsNullOrWhiteSpace(Location);
}

public sealed record WeatherHour(string Label, string IconKey, double TempC, double PrecipChance);

public enum NotificationUrgency { Low, Normal, High }

public sealed record ShellNotification(
    string Id,
    string AppId,
    string AppName,
    string Title,
    string Body,
    NotificationUrgency Urgency,
    DateTimeOffset ReceivedAt,
    string[] Actions,
    string? DeepLink = null);

public sealed record WorkspaceInfo(int Index, string Name, bool Active, bool Occupied, int WindowCount);

public sealed record MonitorInfo(
    string Id,
    string FriendlyName,
    int X,
    int Y,
    int Width,
    int Height,
    double ScaleFactor,
    bool IsPrimary,
    int RefreshHz);

public interface IMediaService
{
    MediaTrack Current { get; }
    PlaybackState State { get; }
    TimeSpan Position { get; }
    event EventHandler? Changed;
    Task PlayPauseAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan position);
}

public interface IAudioService
{
    int Volume { get; }
    bool Muted { get; }
    bool MicMuted { get; }
    event EventHandler? Changed;
    void SetVolume(int percent);
    void ToggleMute();
    void ToggleMic();
    /// <summary>Latest FFT magnitudes (0..1), produced by NAudio loopback capture.</summary>
    ReadOnlySpan<float> GetSpectrum();
}

public interface ISystemMonitorService
{
    SystemSample? Latest { get; }
    event EventHandler<SystemSample>? Sampled;

    /// <summary>Fixed-rate sampling, for the benchmark harness and tests.</summary>
    void Start(TimeSpan interval);

    void Stop();

    /// <summary>
    /// "Something is looking at the numbers." Reading every sensor once a second is the single most
    /// expensive thing this process does — LibreHardwareMonitor walks SMBus and opens handles — so
    /// sampling drops to a slow heartbeat when nothing is on screen. Dispose the handle to release.
    /// </summary>
    IDisposable Subscribe();
}

public interface INetworkService
{
    NetworkState Current { get; }
    event EventHandler<NetworkState>? Changed;
    Task<bool> ToggleWifiAsync();
    Task<bool> ToggleVpnAsync();
}

public interface IBluetoothService
{
    bool Enabled { get; }
    IReadOnlyList<BluetoothDevice> Devices { get; }
    event EventHandler? Changed;
    Task<bool> ToggleAsync();
    Task ConnectAsync(string deviceId);
    Task DisconnectAsync(string deviceId);
}

public sealed record VpnProfile(
    string Name,
    string Kind,
    string Target,
    bool Connected);

public interface IVpnService
{
    IReadOnlyList<VpnProfile> Profiles { get; }
    string? ConnectedName { get; }
    event EventHandler? Changed;
    Task<bool> ConnectAsync(string name);
    Task<bool> DisconnectAsync();
    Task RefreshAsync();
}

public interface IWeatherService
{
    WeatherNow? Current { get; }
    event EventHandler<WeatherNow>? Updated;
    Task RefreshAsync(CancellationToken ct = default);
}

public interface IWallpaperService
{
    string? CurrentPath { get; }
    IReadOnlyList<string> ListWallpapers();
    Task<RgbSeed> SetAsync(string path);
    Task<RgbSeed> NextAsync();

    /// <summary>Every monitor Windows will accept a wallpaper for, primary first.</summary>
    IReadOnlyList<DisplayTarget> Monitors { get; }

    /// <summary>Sets one monitor's background. <paramref name="monitorId"/> is a device path.</summary>
    Task<RgbSeed> SetForMonitorAsync(string monitorId, string path);

    /// <summary>Raised with the new path after any successful change.</summary>
    event EventHandler<string>? Changed;

    /// <summary>Current placement mode, as a config string.</summary>
    string Fit { get; }

    /// <summary>Changing placement is a registry write, not a decode; false means "unknown mode".</summary>
    bool SetFit(string fit);
}

/// <summary>One monitor the wallpaper layer can address.</summary>
public readonly record struct DisplayTarget(string Id, string Name, bool IsPrimary);

/// <summary>Seed colour produced by decoding a wallpaper (kept free of UI types).</summary>
public readonly record struct RgbSeed(byte R, byte G, byte B);

public interface IThemeService
{
    Theming.MaterialScheme Current { get; }
    event EventHandler<Theming.MaterialScheme>? Changed;
    void Apply(RgbSeed seed, bool dark, Theming.SchemeVariant variant);
    void SetMode(bool dark);
    void SetVariant(Theming.SchemeVariant variant);
}

public interface INotificationService
{
    bool DoNotDisturb { get; set; }
    IReadOnlyList<ShellNotification> History { get; }
    event EventHandler<ShellNotification>? Received;
    event EventHandler? Cleared;
    void Push(ShellNotification notification);
    void Dismiss(string id);
    void ClearAll();
}

public interface IWorkspaceService
{
    IReadOnlyList<WorkspaceInfo> Workspaces { get; }
    event EventHandler? Changed;
    Task SwitchToAsync(int index);
    Task MoveActiveWindowToAsync(int index);
}

public interface IDisplayService
{
    IReadOnlyList<MonitorInfo> Monitors { get; }
    event EventHandler? Changed;
}
