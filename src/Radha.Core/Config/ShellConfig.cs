namespace Radha.Core.Config;

/// <summary>
/// Strongly-typed mirror of <c>%APPDATA%\Radha\shell.json</c>.
/// Every property has a sane default so a missing file still yields a working shell.
/// Per-monitor overrides live in <c>monitors\&lt;display&gt;\shell.json</c> and are
/// deep-merged over the base document.
/// </summary>
public sealed class ShellConfig
{
    public AppearanceConfig Appearance { get; set; } = new();
    public BarConfig Bar { get; set; } = new();
    public DashboardConfig Dashboard { get; set; } = new();
    public LauncherConfig Launcher { get; set; } = new();
    public NotificationsConfig Notifications { get; set; } = new();
    public OsdConfig Osd { get; set; } = new();
    public SessionConfig Session { get; set; } = new();
    public ControlCenterConfig ControlCenter { get; set; } = new();
    public ServicesConfig Services { get; set; } = new();
    public WallpaperConfig Wallpaper { get; set; } = new();
    public BackgroundConfig Background { get; set; } = new();
    public PathsConfig Paths { get; set; } = new();

    /// <summary>Clamp user-supplied numbers into ranges the UI can actually render.</summary>
    public ShellConfig Normalized()
    {
        Appearance.Scale = Math.Clamp(Appearance.Scale, 0.6, 2.0);
        Appearance.FontScale = Math.Clamp(Appearance.FontScale, 0.7, 1.8);
        Appearance.AnimationScale = Math.Clamp(Appearance.AnimationScale, 0, 2.0);
        Appearance.RoundingScale = Math.Clamp(Appearance.RoundingScale, 0, 2.0);
        Bar.Height = Math.Clamp(Bar.Height, 28, 64);
        Bar.WorkspaceCount = Math.Clamp(Bar.WorkspaceCount, 1, 12);
        Notifications.TimeoutSeconds = Math.Clamp(Notifications.TimeoutSeconds, 2, 60);
        Notifications.MaxVisible = Math.Clamp(Notifications.MaxVisible, 1, 10);
        Osd.TimeoutMs = Math.Clamp(Osd.TimeoutMs, 400, 6000);
        Launcher.MaxResults = Math.Clamp(Launcher.MaxResults, 3, 50);
        Background.Dim = Math.Clamp(Background.Dim, 0, 0.8);
        return this;
    }
}

public sealed class AppearanceConfig
{
    public string Font { get; set; } = "Segoe UI Variable Display";
    public string MonoFont { get; set; } = "Cascadia Code";
    public double Scale { get; set; } = 1.0;
    public double FontScale { get; set; } = 1.0;
    public double RoundingScale { get; set; } = 1.0;
    public double AnimationScale { get; set; } = 1.0;
    /// <summary>"auto" | "dark" | "light"</summary>
    public string Mode { get; set; } = "auto";
    /// <summary>tonalSpot | vibrant | expressive | fidelity | content | neutral | monochrome</summary>
    public string SchemeVariant { get; set; } = "tonalSpot";
    public bool Transparency { get; set; } = true;
    public int BlurRadius { get; set; } = 30;
    /// <summary>Follow the Windows accent colour instead of the wallpaper seed.</summary>
    public bool UseSystemAccent { get; set; } = false;
}

public sealed class BarConfig
{
    /// <summary>"top" | "bottom"</summary>
    public string Position { get; set; } = "top";
    public int Height { get; set; } = 42;
    public string[] Left { get; set; } = { "logo", "workspaces", "activeWindow" };
    public string[] Center { get; set; } = { "media" };
    public string[] Right { get; set; } = { "tray", "battery", "clock", "power" };
    public bool ShowTray { get; set; } = true;
    public bool AutoHide { get; set; } = false;
    public bool HoverToReveal { get; set; } = true;
    public string WorkspacesDisplay { get; set; } = "dots"; // dots | numbers | shapes | icons
    public int WorkspaceCount { get; set; } = 5;
    public bool ShowClockSeconds { get; set; } = false;
    public string ClockFormat { get; set; } = "24h"; // 12h | 24h
    /// <summary>Windows shell tray icons to mirror (msft: namespace) or "all".</summary>
    public string[] TrayModules { get; set; } = { "network", "bluetooth", "volume", "battery" };

    /// <summary>
    /// Draw a bar on every display, not only the primary one. Per-monitor overrides live in
    /// <c>monitors/&lt;id&gt;/shell.json</c> and are merged on top of this.
    /// </summary>
    public bool ShowOnAllDisplays { get; set; } = true;

    /// <summary>Bar segments that react to the mouse wheel: "volume", "brightness", "workspaces".</summary>
    public string[] ScrollActions { get; set; } = { "volume", "workspaces" };
}

public sealed class DashboardConfig
{
    public bool OpenOnHover { get; set; } = true;
    public int HoverDelayMs { get; set; } = 180;
    public string DefaultTab { get; set; } = "overview"; // overview | media | performance
    public bool ShowWeather { get; set; } = true;
    public bool ShowUptime { get; set; } = true;
    public string[] PerformanceGraphs { get; set; } = { "cpu", "gpu", "memory", "network", "storage", "battery" };
    public int GraphHistorySeconds { get; set; } = 60;
}

public sealed class LauncherConfig
{
    public bool Fuzzy { get; set; } = true;
    public bool VimKeys { get; set; } = true;
    public int MaxResults { get; set; } = 12;
    public bool ShowFavourites { get; set; } = true;
    public bool LiveCalculator { get; set; } = true;
    public string ActionPrefix { get; set; } = ">";
    public string SpecialPrefix { get; set; } = "@";
    public string[] HiddenApps { get; set; } = Array.Empty<string>();
    public string[] Favourites { get; set; } = Array.Empty<string>();
}

public sealed class NotificationsConfig
{
    /// <summary>Mirror Windows toasts (requires the packaged notification listener capability).</summary>
    public bool ListenSystemToasts { get; set; } = true;
    public bool DoNotDisturb { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 6;
    public int MaxVisible { get; set; } = 3;
    public bool GroupByApp { get; set; } = true;
    public bool ShowActions { get; set; } = true;
}

public sealed class OsdConfig
{
    public bool Enabled { get; set; } = true;
    public int TimeoutMs { get; set; } = 1600;
    public bool OnVolume { get; set; } = true;
    public bool OnBrightness { get; set; } = true;
    public bool OnMic { get; set; } = true;
    /// <summary>"bottom" | "center" | "top"</summary>
    public string Position { get; set; } = "bottom";
}

public sealed class SessionConfig
{
    public bool ConfirmPowerActions { get; set; } = true;
    public string[] Actions { get; set; } = { "logout", "suspend", "reboot", "shutdown" };
    public bool ShowUptime { get; set; } = true;
}

public sealed class ControlCenterConfig
{
    public string[] Tiles { get; set; } =
    {
        "wifi", "bluetooth", "dnd", "nightLight", "darkMode",
        "screenRecord", "airplane", "project", "nearbyShare", "energySaver", "accessibility", "rotation"
    };
    public string[] Sliders { get; set; } = { "volume", "brightness", "mic" };
    public bool ShowThemeCard { get; set; } = true;
    public bool ShowMediaCard { get; set; } = true;
}

public sealed class ServicesConfig
{
    public WeatherConfig Weather { get; set; } = new();
    public MediaServiceConfig Media { get; set; } = new();
    public SensorConfig Sensors { get; set; } = new();
    public AudioServiceConfig Audio { get; set; } = new();
    public int PollIntervalMs { get; set; } = 1000;
}

public sealed class WeatherConfig
{
    /// <summary>"open-meteo" (no key needed) | "weatherapi" | "manual"</summary>
    public string Provider { get; set; } = "open-meteo";
    public string Location { get; set; } = "";
    /// <summary>Null = infer from Windows location services / IP.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Units { get; set; } = "metric"; // metric | imperial
    public int RefreshMinutes { get; set; } = 20;
    public string ApiKey { get; set; } = "";
}

public sealed class MediaServiceConfig
{
    public bool EnableVisualizer { get; set; } = true;
    public bool EnableLyrics { get; set; } = true;
    public string LyricsProvider { get; set; } = "lrclib";
    public string[] IgnoredPlayers { get; set; } = Array.Empty<string>();
}

public sealed class SensorConfig
{
    public bool ReadCpu { get; set; } = true;
    public bool ReadGpu { get; set; } = true;
    public bool ReadTemperatures { get; set; } = true;
    public bool ReadStorage { get; set; } = true;
}

public sealed class AudioServiceConfig
{
    public string? PreferredOutputDevice { get; set; }
    public int VisualizerFftSize { get; set; } = 2048;
    public int VisualizerBars { get; set; } = 48;
}

public sealed class WallpaperConfig
{
    /// <summary>"static" | "video" (video wallpapers are a later phase).</summary>
    public string Mode { get; set; } = "static";
    public string Path { get; set; } = "";
    /// <summary>fill | fit | stretch | center | span. Parsed by <c>WallpaperLibrary.ParseFit</c>.</summary>
    public string Fit { get; set; } = "fill";
    public double Dim { get; set; } = 0.0;
    public double Blur { get; set; } = 0.0;
    public RotationConfig Rotation { get; set; } = new();

    /// <summary>
    /// Per-monitor overrides, keyed by the monitor's device path as Windows reports it.
    /// <c>"*"</c> is a documented catch-all. Unknown monitors are preserved on save so a docked
    /// screen's assignment survives being undocked.
    /// </summary>
    public Dictionary<string, string> PerMonitor { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>none | crossfade. Collapses to none when reduced motion is on.</summary>
    public string Transition { get; set; } = "crossfade";
}

/// <summary>The shell's own layer behind the desktop icons: clock, visualiser, wallpaper cross-fade.</summary>
public sealed class BackgroundConfig
{
    /// <summary>
    /// Off by default. It is a full-screen window attached to Explorer's wallpaper host; users who
    /// want a second background layer opt into it rather than discovering it one morning.
    /// </summary>
    public bool Enabled { get; set; } = false;

    public bool Clock { get; set; } = true;

    /// <summary>Audio spectrum under the clock. Drawn only while something is actually playing.</summary>
    public bool Visualiser { get; set; } = true;

    /// <summary>0..0.8 black wash over the image, so a clock stays readable on a bright photo.</summary>
    public double Dim { get; set; } = 0.25;
}

public sealed class RotationConfig
{
    public bool Enabled { get; set; } = false;
    public int IntervalMinutes { get; set; } = 30;
    public bool Shuffle { get; set; } = true;
}

public sealed class PathsConfig
{
    public string Wallpapers { get; set; } = "";
    public string Cache { get; set; } = "";
    public string Config { get; set; } = "";
    public string Logs { get; set; } = "";
}
