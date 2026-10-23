using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Radha.App.Services.Win;
using Radha.App.ViewModels;
using Radha.App.Views;
using Radha.Core.Config;
using Radha.Core.Ipc;
using Radha.Core.Modules;
using Radha.Core.Security;
using Radha.Core.Services;
using Radha.Core.Theming;

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
    private readonly IWorkspaceService _workspaces;
    private readonly IpcServer _ipc;
    private readonly string _auditLogPath;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DispatcherTimer? _weatherTimer;

    public BarWindow Bar { get; }
    public OverlayWindow Overlay { get; }
    public OsdWindow Osd { get; }

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

        Bar = new BarWindow(_state);
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
                }
                return null;

            case ShellModule.Bluetooth:
                if (command == "toggle") { _ = _bluetooth.ToggleAsync(); return "ok"; }
                return JsonSerializer.Serialize(new { enabled = _bluetooth.Enabled, devices = _bluetooth.Devices.Count });

            case ShellModule.Network:
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

        var value = leaf.PropertyType switch
        {
            var t when t == typeof(double) => double.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture),
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

    public async ValueTask DisposeAsync()
    {
        _clock.Stop();
        _weatherTimer?.Stop();
        await _ipc.DisposeAsync();
        if (_system is IDisposable disposableSystem) disposableSystem.Dispose();
        if (_audio is IDisposable disposableAudio) disposableAudio.Dispose();
        await _services.DisposeAsync();
    }
}
