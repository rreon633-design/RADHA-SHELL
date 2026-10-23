using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Radha.App.Services.Win;
using Radha.App.ViewModels;
using Radha.App.Views;
using Radha.Core.Config;
using Radha.Core.Diagnostics;
using Radha.Core.Ipc;
using Radha.Core.Modules;
using Radha.Core.Net;
using Radha.Core.Notifications;
using Radha.Core.Power;
using Radha.Core.Security;
using Radha.Core.Services;
using Radha.Core.Theming;
using Radha.Core.Updates;
using Microsoft.UI.Windowing;

namespace Radha.App.Hosting;

/// <summary>
/// Composition root: builds services, wires the IPC daemon, keeps the clock ticking and
/// owns window lifetime. Everything the CLI can ask for is funnelled through <see cref="Execute"/>.
/// </summary>
public sealed class ShellHost : IAsyncDisposable
{
    private readonly ConfigStore _configStore;
    private readonly ShellConfig _config;
    private readonly ServiceProvider _services;
    private readonly ShellState _state;
    private readonly DashboardViewModel _dashboard;
    private readonly LauncherViewModel _launcher;
    private readonly ControlCenterViewModel _controlCenter;
    private readonly IThemeService _theme;
    private readonly IWallpaperService _wallpaper;
    private readonly IAudioService _audio;
    private readonly IMediaService _media;
    private readonly INotificationService _notifications;
    private readonly ISystemMonitorService _system;
    private readonly IWeatherService _weather;
    private readonly IBluetoothService _bluetooth;
    private readonly IVpnService _vpn;
    private readonly ToastMirrorService _toastMirror;
    private readonly IWorkspaceService _workspaces;
    private readonly IpcServer _ipc;
    private readonly string _auditLogPath;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DispatcherTimer? _weatherTimer;

    // gap-closing services
    private readonly TrayIcon _tray = new();
    private readonly List<BarWindow> _bars = new();
    private readonly BatteryWatch _battery = new();
    private HotkeyService? _hotkeys;
    private ConfigWatcher? _configWatcher;
    private DispatcherTimer? _idleTimer;
    private UpdateResult? _update;
    private SettingsWindow? _settings;
    private string[] _wifiNetworks = Array.Empty<string>();
    private bool _idleInhibited;

    public BarWindow Bar { get; }
    public OverlayWindow Overlay { get; }
    public OsdWindow Osd { get; }

    /// <summary>Non-primary bars, one per extra display. Always includes nothing for a single monitor.</summary>
    public IReadOnlyList<BarWindow> ExtraBars => _bars;
    public bool TrayVisible => _tray.IsVisible;
    public IReadOnlyList<string> HotkeyFailures => _hotkeys?.Failures ?? Array.Empty<string>();
    public int HotkeysRegistered => _hotkeys?.Registered.Count ?? 0;
    public UpdateResult? AvailableUpdate => _update;
    public IReadOnlyList<string> VisibleWifiNetworks => _wifiNetworks;

    public ShellHost()
    {
        _configStore = new ConfigStore();
        _config = _configStore.Load();

        var services = new ServiceCollection();
        services.AddSingleton(_config);
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IMediaService, GsmtcMediaService>();
        services.AddSingleton<IAudioService>(_ => new AudioService(_config.Services.Audio.VisualizerBars));
        services.AddSingleton<ISystemMonitorService>(_ => new SystemMonitorService());
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<IBluetoothService, BluetoothService>();
        services.AddSingleton<IWeatherService, WeatherService>();
        services.AddSingleton<IWallpaperService, WallpaperService>();
        services.AddSingleton<INotificationService, NotificationStore>();
        services.AddSingleton<IVpnService, VpnService>();
        services.AddSingleton<IWorkspaceService>(_ => new WorkspaceService(_config.Bar.WorkspaceCount));
        services.AddSingleton<IDisplayService, DisplayService>();
        _services = services.BuildServiceProvider();

        _theme = _services.GetRequiredService<IThemeService>();
        _wallpaper = _services.GetRequiredService<IWallpaperService>();
        _audio = _services.GetRequiredService<IAudioService>();
        _media = _services.GetRequiredService<IMediaService>();
        _notifications = _services.GetRequiredService<INotificationService>();
        _system = _services.GetRequiredService<ISystemMonitorService>();
        _weather = _services.GetRequiredService<IWeatherService>();
        _bluetooth = _services.GetRequiredService<IBluetoothService>();
        _vpn = _services.GetRequiredService<IVpnService>();

        // Toast mirroring needs package identity + a restricted capability; when unavailable it
        // reports why and the store keeps working for shell events. Never fatal.
        _toastMirror = new ToastMirrorService(_notifications);
        _ = _toastMirror.StartAsync();
        _workspaces = _services.GetRequiredService<IWorkspaceService>();

        if (_theme is ThemeService themeService) themeService.Initialize(_config);

        _state = new ShellState(
            new ThemeViewModel(_theme.Current),
            _media,
            _notifications,
            _workspaces,
            _audio);

        _dashboard = new DashboardViewModel(_state, _system, _weather, _media, _audio, _theme);
        _launcher = new LauncherViewModel(_state, (module, command) => _ = ExecuteCommandAsync(module, command, Array.Empty<string>()));
        _controlCenter = new ControlCenterViewModel(
            _state, _theme, _wallpaper,
            _services.GetRequiredService<INetworkService>(),
            _bluetooth, _audio,
            (module, command, args) => _ = ExecuteCommandAsync(module, command, args));

        _theme.Changed += (_, scheme) => _state.ApplyTheme(scheme);

        _state.AttachConnectivity(
            _services.GetRequiredService<INetworkService>(),
            _bluetooth,
            _vpn);

        Bar = new BarWindow(_state, null, _config.Bar.Height, _config.Bar.ScrollActions);
        Overlay = new OverlayWindow(_state, _dashboard, _launcher, _controlCenter);
        Osd = new OsdWindow(_state);

        _auditLogPath = Path.Combine(_config.Paths.Logs, "ipc-audit.log");
        try { Directory.CreateDirectory(Path.GetDirectoryName(_auditLogPath)!); } catch { }

        _ipc = new IpcServer(null, HandleIpcAsync, new IpcServerOptions
        {
            Limiter = new RateLimiter(capacity: 60, refillPerSecond: 20),
            RestrictToCurrentUser = true,
            Audit = WriteAudit
        });

        _clock.Tick += (_, _) => _state.Tick();
    }

    public async Task StartAsync()
    {
        App.MainWindowInstance = Bar;
        Bar.Activate();
        await ((GsmtcMediaService)_media).InitializeAsync();
        await _ipc.StartAsync();

        _clock.Start();
        await _dashboard.InitializeAsync();

        StartSecondaryBars();
        StartTray();
        StartHotkeys();
        StartConfigWatcher();
        StartIdleWatch();
        _ = CheckForUpdatesAsync();
        _ = ScanWifiAsync();

        // Battery thresholds share the sampler the dashboard already runs.
        _system.Sampled += (_, sample) => OnSample(sample);

        var minutes = Math.Max(5, _config.Services.Weather.RefreshMinutes);
        _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
        _weatherTimer.Tick += async (_, _) => await _weather.RefreshAsync();
        _weatherTimer.Start();

        // Paint the desktop with the wallpaper's palette on startup.
        if (!string.IsNullOrWhiteSpace(_config.Wallpaper.Path) && File.Exists(_config.Wallpaper.Path))
        {
            var seed = await _wallpaper.SetAsync(_config.Wallpaper.Path);
            if (seed != default) _theme.Apply(seed, _theme.Current.IsDark, _theme.Current.Variant);
        }

        _notifications.Push(new ShellNotification(
            Guid.NewGuid().ToString("N")[..8],
            "radha",
            "RADHA SHELL",
            "Welcome back, " + Environment.UserName,
            "Radha is running. Press the logo or run `radha dashboard toggle` to open the dashboard.",
            NotificationUrgency.Low,
            DateTimeOffset.Now,
            Array.Empty<string>()));
    }

    // ---------------- IPC ----------------

    /// <summary>
    /// Append-only audit trail. Every field is redacted before it touches disk, and logging
    /// failures never affect command handling.
    /// </summary>
    private void WriteAudit(IpcAudit audit)
    {
        try
        {
            var line = string.Join('\t',
                audit.Timestamp.ToString("O"),
                audit.ClientId,
                Redactor.Redact(audit.Module) ?? "-",
                Redactor.Redact(audit.Command) ?? "-",
                audit.Outcome,
                Redactor.Redact(audit.Detail) ?? "");
            File.AppendAllText(_auditLogPath, line + Environment.NewLine);
        }
        catch { /* audit is best-effort, never fatal */ }
    }

    private Task<IpcResponse> HandleIpcAsync(IpcRequest request, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        Ui.Post(() =>
        {
            try
            {
                if (!CommandCatalog.TryParseModule(request.Module, out var module))
                {
                    completion.SetResult(IpcResponse.Fail(request.Id, $"unknown module '{request.Module}'"));
                    return;
                }

                if (!CommandCatalog.IsValid(module, request.Command))
                {
                    completion.SetResult(IpcResponse.Fail(request.Id,
                        $"unknown command '{request.Command}' for {request.Module}"));
                    return;
                }

                var payload = Execute(module, request.Command, request.Args);
                completion.SetResult(IpcResponse.Ok_(request.Id, payload));
            }
            catch (Exception ex)
            {
                completion.SetResult(IpcResponse.Fail(request.Id, ex.Message));
            }
        });

        return completion.Task;
    }

    private Task ExecuteCommandAsync(string module, string command, string[] args)
    {
        return HandleIpcAsync(
            new IpcRequest(Guid.NewGuid().ToString("N")[..8], module, command, args),
            CancellationToken.None);
    }

    /// <summary>Single command switch shared by the CLI, the launcher action prefix and in-app buttons.</summary>
    private string? Execute(ShellModule module, string command, string[] args)
    {
        switch (module)
        {
            case ShellModule.Bar:
                return "ok";

            case ShellModule.Launcher when command == "calc":
                if (args.Length == 0) return "usage: radha launcher calc <expression>";
                var expression = string.Join(' ', args);
                return Core.Launcher.Calculator.TryEvaluate(expression, out var value)
                    ? Core.Launcher.Calculator.Format(value)
                    : "not a calculation";

            case ShellModule.Dashboard:
            case ShellModule.Launcher:
            case ShellModule.ControlCenter:
            case ShellModule.Notifications:
            case ShellModule.Session:
            case ShellModule.Osd:
                _state.ShowModule(module.ToString(), command, args);
                return "ok";

            case ShellModule.Theme:
                switch (command)
                {
                    case "set":
                        if (args.Length > 0 && TryColor(args[0], out var manual))
                        {
                            _theme.Apply(new RgbSeed(manual.R, manual.G, manual.B), _theme.Current.IsDark, _theme.Current.Variant);
                            return manual.ToHex();
                        }
                        return "usage: radha theme set <#hex>";
                    case "variant":
                        if (args.Length > 0 && Enum.TryParse<SchemeVariant>(args[0], true, out var variant))
                        {
                            _theme.SetVariant(variant);
                            return variant.ToString();
                        }
                        return "usage: radha theme variant <tonalSpot|vibrant|expressive|fidelity|content|neutral|monochrome>";
                    case "dark":
                        _theme.SetMode(true);
                        return "dark";
                    case "light":
                        _theme.SetMode(false);
                        return "light";
                    case "toggle":
                        _theme.SetMode(!_theme.Current.IsDark);
                        return _theme.Current.IsDark ? "dark" : "light";
                    case "status":
                        return JsonSerializer.Serialize(new
                        {
                            seed = _theme.Current.Seed.ToHex(),
                            dark = _theme.Current.IsDark,
                            variant = _theme.Current.Variant.ToString(),
                            primary = _theme.Current.Primary.ToHex()
                        });
                    case "extract":
                        return "run `radha scheme extract <image>` for offline extraction";
                }
                return null;

            case ShellModule.Wallpaper:
                switch (command)
                {
                    case "list":
                        return JsonSerializer.Serialize(_wallpaper.ListWallpapers());
                    case "current":
                        return _wallpaper.CurrentPath ?? "";
                    case "next":
                    case "random":
                        var seed = _wallpaper.NextAsync().GetAwaiter().GetResult();
                        if (seed != default)
                            _theme.Apply(seed, _theme.Current.IsDark, _theme.Current.Variant);
                        return _wallpaper.CurrentPath ?? "";
                    case "set":
                        if (args.Length == 0) return "usage: radha wallpaper set <path>";
                        var applied = _wallpaper.SetAsync(args[0]).GetAwaiter().GetResult();
                        if (applied != default)
                            _theme.Apply(applied, _theme.Current.IsDark, _theme.Current.Variant);
                        return args[0];
                }
                return null;

            case ShellModule.Media:
                switch (command)
                {
                    case "play-pause": _ = _media.PlayPauseAsync(); return "ok";
                    case "next": _ = _media.NextAsync(); return "ok";
                    case "previous": _ = _media.PreviousAsync(); return "ok";
                    case "seek":
                        if (args.Length > 0 && InputGuard.TryParseInt(args[0], 0, 86_400, out var seconds))
                            _ = _media.SeekAsync(TimeSpan.FromSeconds(seconds));
                        return "ok";
                    case "mute": _audio.ToggleMute(); return _audio.Muted ? "muted" : "unmuted";
                    case "status":
                        return JsonSerializer.Serialize(new
                        {
                            title = _media.Current.Title,
                            artist = _media.Current.Artist,
                            state = _media.State.ToString(),
                            position = _media.Position.TotalSeconds
                        });
                }
                return null;

            case ShellModule.System:
                switch (command)
                {
                    case "status":
                        var latest = _system.Latest;
                        return JsonSerializer.Serialize(new
                        {
                            cpu = latest?.CpuPercent,
                            gpu = latest?.GpuPercent,
                            memoryUsedGb = latest?.MemoryUsedGb,
                            memoryTotalGb = latest?.MemoryTotalGb,
                            battery = latest?.BatteryPercent,
                            uptime = _state.UptimeText,
                            volume = _audio.Volume,
                            dark = _theme.Current.IsDark
                        });
                    case "volume":
                        if (args.Length > 0 && InputGuard.TryParseInt(args[0], 0, 100, out var volume))
                        {
                            _audio.SetVolume(volume);
                            _state.ShowOsd("Volume", volume);
                        }
                        return _audio.Volume.ToString();
                    case "brightness":
                        if (args.Length > 0 && InputGuard.TryParseNumber(args[0], 0, 100, out var brightness))
                            _state.ShowOsd("Brightness", brightness);
                        return args.FirstOrDefault() ?? "0";
                    case "uptime":
                        return _state.UptimeText;
                    case "update-check":
                        return CheckUpdatesAsync().GetAwaiter().GetResult();
                }
                return null;

            case ShellModule.Bluetooth:
                if (command == "toggle") { _ = _bluetooth.ToggleAsync(); return "ok"; }
                if (command == "scan")
                {
                    var found = BluetoothService.ScanUnpairedAsync().GetAwaiter().GetResult();
                    return JsonSerializer.Serialize(found.Select(d => new { name = d.Name, id = d.Id }));
                }
                if (command == "pair")
                {
                    if (args.Length == 0) return "usage: radha bluetooth pair <device-id>";
                    return BluetoothService.PairAsync(args[0]).GetAwaiter().GetResult()
                        ? "paired"
                        : "pairing failed or was cancelled";
                }
                return JsonSerializer.Serialize(new { enabled = _bluetooth.Enabled, devices = _bluetooth.Devices.Count });

            case ShellModule.Network:
                if (command == "wifi-connect")
                {
                    if (args.Length == 0) return "usage: radha network wifi-connect <profile>";
                    return WifiScanner.ConnectAsync(args[0]).GetAwaiter().GetResult()
                        ? "connected to " + args[0]
                        : "no saved profile named " + args[0] + " (Windows needs a profile; join it once from Settings)";
                }
                if (command == "wifi-list")
                {
                    var networks = WifiScanner.ScanAsync().GetAwaiter().GetResult();
                    return JsonSerializer.Serialize(networks.Select(n => new
                    {
                        ssid = n.Ssid,
                        signal = n.SignalPercent,
                        bars = n.Bars,
                        secure = n.Secure,
                        auth = n.Authentication
                    }));
                }
                var network = _services.GetRequiredService<INetworkService>().Current;
                return JsonSerializer.Serialize(new
                {
                    connected = network.Connected,
                    ssid = network.Ssid,
                    signal = network.SignalStrength,
                    vpn = network.VpnName
                });

            case ShellModule.Config:
                switch (command)
                {
                    case "path":
                        return _configStore.ConfigPath;
                    case "reload":
                        var reloaded = _configStore.Load();
                        if (_theme is ThemeService ts) ts.Initialize(reloaded);
                        return "reloaded";
                    case "get":
                        return JsonSerializer.Serialize(_configStore.Load(), ConfigStore.JsonOptions);
                    case "set":
                        if (args.Length < 2) return "usage: radha config set <path> <value>";
                        SetConfigValue(args[0], args[1]);
                        return "ok";
                    case "monitor-override":
                        if (args.Length < 2) return "usage: radha config monitor-override <monitor> <json>";
                        return _configStore.SaveMonitorOverride(args[0], args[1]);
                    case "doctor":
                    {
                        var text = File.Exists(_configStore.ConfigPath) ? File.ReadAllText(_configStore.ConfigPath) : "{}";
                        var report = new ConfigDoctor().Scan(text, _configStore.ConfigPath);
                        return JsonSerializer.Serialize(new
                        {
                            summary = report.Summary,
                            version = report.Version,
                            fixable = report.Fixable,
                            findings = report.Findings.Select(f => new
                            {
                                severity = f.Severity.ToString(),
                                path = f.Path,
                                message = f.Message
                            })
                        });
                    }
                    case "fix":
                    {
                        var text = File.Exists(_configStore.ConfigPath) ? File.ReadAllText(_configStore.ConfigPath) : "{}";
                        var (fixedJson, report) = new ConfigDoctor().Fix(text);
                        File.WriteAllText(_configStore.ConfigPath, fixedJson);
                        return report.Fixes.Count == 0
                            ? "nothing to fix"
                            : string.Join("\n", report.Fixes);
                    }
                    case "diagnostics":
                    {
                        var root = ConfigStore.DefaultRoot();
                        var path = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                            $"radha-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                        new DiagnosticsBundle()
                            .WithEnvironment(configPath: _configStore.ConfigPath,
                                             logDirectory: Path.Combine(root, "logs"))
                            .WithTextFile("config/shell.json", _configStore.ConfigPath)
                            .WithTextFile("logs/ipc-audit.log", Path.Combine(root, "logs", "ipc-audit.log"))
                            .WithReport($"hotkeys: {HotkeysRegistered}\ntray: {TrayVisible}\n" +
                                        string.Join("\n", HotkeyFailures.Select(f => "hotkey failure: " + f)))
                            .Build(path);
                        return path;
                    }
                }
                return null;
        }

        return null;
    }

    /// <summary><c>radha config set appearance.scale 1.1</c> — reflection over the strongly typed model.</summary>
    private void SetConfigValue(string path, string rawValue)
    {
        var config = _configStore.Load();
        object target = config;
        var segments = path.Split('.');

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var property = target.GetType().GetProperty(segments[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            target = property?.GetValue(target) ?? throw new ArgumentException($"no config section '{segments[i]}'");
        }

        var leaf = target.GetType().GetProperty(segments[^1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                   ?? throw new ArgumentException($"no config key '{segments[^1]}'");

        object? value = leaf.PropertyType switch
        {
            var t when t == typeof(double) => (object)double.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture),
            var t when t == typeof(int) => int.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture),
            var t when t == typeof(bool) => bool.Parse(rawValue),
            var t when t == typeof(string) => rawValue,
            _ => throw new NotSupportedException($"config key '{segments[^1]}' has unsupported type {leaf.PropertyType.Name}")
        };

        leaf.SetValue(target, value);
        _configStore.Save(config);
    }

    private static bool TryColor(string text, out Rgb color)
    {
        try
        {
            color = Rgb.FromHex(text);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    // ---------------- gap-closing services ----------------

    /// <summary>
    /// One bar per display. Windows users have several monitors as the rule rather than the
    /// exception, and a shell that only paints the primary one looks broken on a laptop + dock.
    /// Each bar applies its own monitor override from <c>%APPDATA%\Radha\monitors\&lt;id&gt;\shell.json</c>.
    /// </summary>
    private void StartSecondaryBars()
    {
        try
        {
            foreach (var area in DisplayArea.FindAll())
            {
                if (area.IsPrimary) continue;

                var key = "display-" + area.DisplayId.Value.ToString("x");
                var monitorConfig = _configStore.LoadForMonitor(key);
                if (!monitorConfig.Bar.ShowOnAllDisplays) continue;

                var bar = new BarWindow(_state, area, monitorConfig.Bar.Height, monitorConfig.Bar.ScrollActions);
                _bars.Add(bar);
                bar.Activate();
            }
        }
        catch (Exception)
        {
            // Older Window App SDK builds can throw from FindAll on a session-0 runner; the
            // primary bar is already up, so this is not fatal.
        }
    }

    private void StartTray()
    {
        _tray.Activated += () => _ = ExecuteCommandAsync("launcher", "toggle", Array.Empty<string>());
        _tray.DoubleClicked += () => _ = ExecuteCommandAsync("dashboard", "toggle", Array.Empty<string>());
        _tray.MenuBuilder = () => new (int, string?)[]
        {
            (1, "Dashboard"),
            (2, "Launcher"),
            (3, "Control centre"),
            (4, "Notifications"),
            (5, null),
            (6, "Settings"),
            (7, "Diagnostics bundle"),
            (8, null),
            (9, "Quit RADHA SHELL")
        };

        _tray.MenuSelected += id =>
        {
            switch (id)
            {
                case 1: _ = ExecuteCommandAsync("dashboard", "toggle", Array.Empty<string>()); break;
                case 2: _ = ExecuteCommandAsync("launcher", "toggle", Array.Empty<string>()); break;
                case 3: _ = ExecuteCommandAsync("controlcenter", "toggle", Array.Empty<string>()); break;
                case 4: _ = ExecuteCommandAsync("notifications", "toggle", Array.Empty<string>()); break;
                case 6: OpenSettings(); break;
                case 7: _ = ExecuteCommandAsync("config", "diagnostics", Array.Empty<string>()); break;
                case 9: Application.Current.Exit(); break;
            }
        };

        if (_config.Bar.ShowTray) _tray.Create("RADHA SHELL");
    }

    private void StartHotkeys()
    {
        _hotkeys = new HotkeyService((module, command) => ExecuteCommandAsync(module, command, Array.Empty<string>()));
        var bound = _hotkeys.Start();
        _tray.SetTooltip(bound > 0
            ? $"RADHA SHELL — {bound} hotkey(s) active"
            : "RADHA SHELL");
    }

    /// <summary>
    /// Watches the config file. A settled, parseable write is adopted without restarting: theme,
    /// scale and bar height apply live, and the parser refuses half-written files.
    /// </summary>
    private void StartConfigWatcher()
    {
        _configWatcher = new ConfigWatcher(_configStore.ConfigPath);
        _configWatcher.Changed += _ =>
        {
            var fresh = _configStore.Load();
            if (_theme is ThemeService theme) theme.Initialize(fresh);
            Ui.Post(() => _state.Tick());
        };
        _configWatcher.Start();
    }

    /// <summary>
    /// Keeps the display awake while media plays or a fullscreen app runs, and releases the
    /// request otherwise. It never locks the session — locking is out of scope for this project.
    /// </summary>
    private void StartIdleWatch()
    {
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _idleTimer.Tick += (_, _) =>
        {
            var inputs = new IdleInputs(
                IdleFor: IdleClock.IdleFor(),
                MediaPlaying: _media.State == PlaybackState.Playing,
                Charging: _state.IsCharging,
                FullscreenApp: false,
                UserInhibited: false);

            var decision = IdlePolicy.Evaluate(inputs, IdleConfig.Default);

            if (decision.Action == IdleAction.TurnOffDisplay && !_idleInhibited)
                IdleClock.TurnOffDisplay();

            var want = decision.InhibitDisplay;
            if (want == _idleInhibited) return;
            _idleInhibited = want;
            IdleClock.SetInhibit(want);
        };
        _idleTimer.Start();
    }

    private void OnSample(SystemSample sample)
    {
        _state.SetBattery(sample.BatteryPercent ?? 0, sample.BatteryCharging);

        var warning = _battery.Update(sample.BatteryPercent, sample.BatteryCharging);
        if (warning is null) return;

        _notifications.Push(new ShellNotification(
            "battery:" + warning.Level,
            "system",
            "RADHA SHELL",
            warning.Title,
            warning.Message,
            warning.Critical ? NotificationUrgency.High : NotificationUrgency.Normal,
            DateTimeOffset.Now,
            Array.Empty<string>()));
    }

    public void OpenSettings()
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(
            _configStore,
            _theme,
            CheckUpdatesAsync,
            _hotkeys?.Registered.Count ?? 0,
            _hotkeys?.Failures ?? Array.Empty<string>());
        _settings.Closed += (_, _) => _settings = null;
        _settings.Activate();
    }

    /// <summary>Runs the check and returns a sentence for the UI; also stores the result.</summary>
    public async Task<string> CheckUpdatesAsync()
    {
        await CheckForUpdatesAsync().ConfigureAwait(false);
        return _update?.Summary ?? "could not reach GitHub";
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RadhaShell/update-check");

            var json = await client.GetStringAsync(
                "https://api.github.com/repos/rreon633-design/RADHA-SHELL/releases?per_page=20").ConfigureAwait(false);

            var releases = new List<ReleaseInfo>();
            foreach (var node in JsonNode.Parse(json)?.AsArray() ?? new JsonArray())
            {
                var tag = node?["tag_name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(tag)) continue;

                releases.Add(new ReleaseInfo(
                    tag,
                    node?["prerelease"]?.GetValue<bool>() ?? false,
                    node?["html_url"]?.GetValue<string>(),
                    node?["published_at"] is JsonNode published && published.GetValueKind() == System.Text.Json.JsonValueKind.String
                        ? DateTimeOffset.Parse(published.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)
                        : null));
            }

            var current = ReleaseCheck.CurrentOrDefault(
                typeof(ShellHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

            var result = ReleaseCheck.Pick(releases, current, currentIsPrerelease: current.Revision == 0 && current.Build == 0);
            Ui.Post(() => _update = result);
        }
        catch (Exception)
        {
            // Offline is the normal case for a desktop shell; the update tile simply stays quiet.
        }
    }

    private async Task ScanWifiAsync()
    {
        try
        {
            var result = await WifiScanner.ScanAsync().ConfigureAwait(false);
            Ui.Post(() => _wifiNetworks = result.Select(n => n.Ssid).ToArray());
        }
        catch (Exception)
        {
            _wifiNetworks = Array.Empty<string>();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _clock.Stop();
        _weatherTimer?.Stop();
        _idleTimer?.Stop();
        _configWatcher?.Dispose();
        _hotkeys?.Dispose();
        _tray.Dispose();
        foreach (var bar in _bars) { try { bar.Close(); } catch { } }

        await _ipc.DisposeAsync();
        if (_system is IDisposable disposableSystem) disposableSystem.Dispose();
        if (_audio is IDisposable disposableAudio) disposableAudio.Dispose();
        await _services.DisposeAsync();
    }
}
