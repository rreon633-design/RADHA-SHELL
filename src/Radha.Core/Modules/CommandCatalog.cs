namespace Radha.Core.Modules;

/// <summary>Top-level modules addressable over IPC and from the CLI.</summary>
public enum ShellModule
{
    Bar,
    Dashboard,
    Launcher,
    ControlCenter,
    Notifications,
    Osd,
    Session,
    Media,
    Wallpaper,
    Theme,
    Network,
    Bluetooth,
    System,
    Config
}

/// <summary>
/// The authoritative command surface, the Radha equivalent of
/// <c>caelestia shell &lt;module&gt; &lt;command&gt;</c>.
/// Both the daemon and the CLI validate against this table so they can never drift.
/// </summary>
public static class CommandCatalog
{
    private static readonly Dictionary<ShellModule, string[]> Commands = new()
    {
        [ShellModule.Bar] = new[] { "show", "hide", "toggle", "reload" },
        [ShellModule.Dashboard] = new[] { "open", "close", "toggle", "tab" },
        [ShellModule.Launcher] = new[] { "open", "close", "toggle", "query", "calc" },
        [ShellModule.ControlCenter] = new[] { "open", "close", "toggle" },
        [ShellModule.Notifications] = new[] { "open", "close", "toggle", "clear", "dnd-on", "dnd-off", "dnd-toggle", "dismiss", "action" },
        [ShellModule.Osd] = new[] { "volume", "brightness", "mic", "show" },
        [ShellModule.Session] = new[] { "menu", "logout", "suspend", "reboot", "shutdown" },
        [ShellModule.Media] = new[] { "play-pause", "next", "previous", "seek", "mute", "status" },
        [ShellModule.Wallpaper] = new[] { "set", "next", "random", "list", "current" },
        [ShellModule.Theme] = new[] { "set", "variant", "dark", "light", "toggle", "status", "extract" },
        [ShellModule.Network] = new[] { "status", "wifi-toggle", "vpn-toggle", "wifi-list" },
        [ShellModule.Bluetooth] = new[] { "status", "toggle" },
        [ShellModule.System] = new[] { "status", "volume", "brightness", "uptime", "update-check" },
        [ShellModule.Config] = new[] { "get", "set", "path", "reload", "monitor-override", "doctor", "fix", "diagnostics" }
    };

    public static IReadOnlyList<string> For(ShellModule module) => Commands[module];

    public static IReadOnlyCollection<ShellModule> Modules => Commands.Keys;

    public static bool TryParseModule(string text, out ShellModule module)
    {
        module = default;
        var normalized = text.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        foreach (var candidate in Commands.Keys)
        {
            if (candidate.ToString().ToLowerInvariant().Replace("-", "") == normalized)
            {
                module = candidate;
                return true;
            }
        }
        return false;
    }

    public static bool IsValid(ShellModule module, string command)
        => Commands.TryGetValue(module, out var list) &&
           list.Contains(command, StringComparer.OrdinalIgnoreCase);

    public static string Help()
    {
        var lines = new List<string> { "RADHA SHELL — command surface", "" };
        foreach (var (module, commands) in Commands.OrderBy(k => k.Key.ToString(), StringComparer.Ordinal))
        {
            var name = module.ToString().ToLowerInvariant();
            lines.Add($"  radha {name,-14} {string.Join(" | ", commands)}");
        }
        lines.Add("");
        lines.Add("  radha scheme extract <image>     wallpaper → Material 3 palette (offline)");
        lines.Add("  radha bench [--json] [--gate]    micro-benchmarks with p95 budgets (offline)");
        lines.Add("  radha help                       this list");
        return string.Join(Environment.NewLine, lines);
    }
}
