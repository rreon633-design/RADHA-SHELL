using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Radha.Core.Config;

/// <summary>
/// Loads / saves shell.json, deep-merges per-monitor overrides, and exposes
/// the well-known paths the CLI and the WinUI host agree on.
/// </summary>
public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string ConfigPath { get; }
    public string MonitorConfigDirectory { get; }

    public ConfigStore(string? configPath = null)
    {
        ConfigPath = configPath ?? DefaultConfigPath();
        MonitorConfigDirectory = Path.Combine(Path.GetDirectoryName(ConfigPath)!, "monitors");
    }

    public static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Radha");

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "radha")
            : Path.Combine(xdg, "radha");
    }

    public static string DefaultConfigPath() => Path.Combine(DefaultRoot(), "shell.json");

    public ShellConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return Defaults().Normalized();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<ShellConfig>(json, JsonOptions) ?? Defaults();
            return ApplyPathDefaults(config).Normalized();
        }
        catch (JsonException ex)
        {
            // Corrupt config must never stop the shell from starting: back it up and use defaults.
            var backup = ConfigPath + ".broken";
            try { File.Copy(ConfigPath, backup, overwrite: true); } catch { /* best effort */ }
            LastError = $"shell.json is invalid ({ex.Message}); defaults used, backup at {backup}";
            return Defaults().Normalized();
        }
    }

    public string? LastError { get; private set; }

    /// <summary>Base config with the per-monitor document deep-merged on top.</summary>
    public ShellConfig LoadForMonitor(string monitorKey)
    {
        var merged = ToNode(Load());
        var monitorFile = Path.Combine(MonitorConfigDirectory, Sanitize(monitorKey), "shell.json");
        if (File.Exists(monitorFile))
        {
            var overrideNode = JsonNode.Parse(File.ReadAllText(monitorFile), documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (overrideNode is not null) DeepMerge(merged, overrideNode);
        }

        var config = merged.Deserialize<ShellConfig>(JsonOptions) ?? Defaults();
        return ApplyPathDefaults(config).Normalized();
    }

    public void Save(ShellConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config.Normalized(), JsonOptions);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, ConfigPath, overwrite: true); // atomic swap
    }

    /// <summary>
    /// Saves while keeping keys this build does not model. Without this a newer build's settings
    /// (or a hand-written comment key) would be silently deleted on the first save.
    /// </summary>
    public void SavePreservingUnknown(ShellConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);

        JsonObject merged;
        try
        {
            merged = File.Exists(ConfigPath)
                ? JsonNode.Parse(File.ReadAllText(ConfigPath), documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                }) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch
        {
            merged = new JsonObject();
        }

        var typed = JsonSerializer.SerializeToNode(config.Normalized(), JsonOptions) as JsonObject ?? new JsonObject();
        var known = Flatten(typed);

        PruneUnknown(merged, string.Empty, known);

        DeepMerge(merged, typed);
        var result = merged;
        result[ConfigMigrations.VersionKey] = ConfigMigrations.Current;

        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, result.ToJsonString(JsonOptions));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    private static HashSet<string> Flatten(JsonObject node, string prefix = "")
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in node)
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            set.Add(path);
            if (value is JsonObject child) set.UnionWith(Flatten(child, path));
        }
        return set;
    }

    private static void PruneUnknown(JsonObject node, string prefix, HashSet<string> known)
    {
        foreach (var key in node.Select(kv => kv.Key).ToList())
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (node[key] is JsonObject child)
            {
                PruneUnknown(child, path, known);
                continue;
            }

            if (known.Contains(path)) continue;

            // Keep keys we cannot reason about (hand-written, or from a newer build); drop only
            // values that are structurally impossible to merge.
            if (node[key] is JsonArray) continue;
        }
    }

    public const int MaxOverrideBytes = 64 * 1024;

    public string SaveMonitorOverride(string monitorKey, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("override JSON is empty", nameof(json));
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxOverrideBytes)
            throw new ArgumentException($"override JSON exceeds {MaxOverrideBytes} bytes", nameof(json));

        var parsed = JsonNode.Parse(json); // validates syntax before writing
        if (parsed is not JsonObject)
            throw new ArgumentException("monitor override must be a JSON object", nameof(json));

        var safeKey = Sanitize(monitorKey);
        var dir = Path.Combine(MonitorConfigDirectory, safeKey);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "shell.json");

        // Never write outside the monitors directory, even if Sanitize changes meaning on another OS.
        var rootFull = Path.GetFullPath(MonitorConfigDirectory);
        if (!Path.GetFullPath(path).StartsWith(rootFull, StringComparison.Ordinal))
            throw new InvalidOperationException("resolved override path escaped the monitors directory");

        File.WriteAllText(path, json);
        return path;
    }

    public static ShellConfig Defaults()
    {
        var config = new ShellConfig();
        return ApplyPathDefaults(config);
    }

    private static ShellConfig ApplyPathDefaults(ShellConfig config)
    {
        var root = DefaultRoot();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(config.Paths.Config)) config.Paths.Config = root;
        if (string.IsNullOrWhiteSpace(config.Paths.Cache)) config.Paths.Cache = Path.Combine(root, "cache");
        if (string.IsNullOrWhiteSpace(config.Paths.Logs)) config.Paths.Logs = Path.Combine(root, "logs");
        if (string.IsNullOrWhiteSpace(config.Paths.Wallpapers))
            config.Paths.Wallpapers = Path.Combine(home, "Pictures", "Wallpapers");
        return config;
    }

    private static JsonNode ToNode(ShellConfig config) =>
        JsonSerializer.SerializeToNode(config, JsonOptions) ?? new JsonObject();

    private static void DeepMerge(JsonNode target, JsonNode source)
    {
        if (target is JsonObject to && source is JsonObject from)
        {
            foreach (var (key, value) in from)
            {
                if (value is null) continue;
                if (to.TryGetPropertyValue(key, out var existing) && existing is JsonObject && value is JsonObject)
                    DeepMerge(existing, value);
                else
                    to[key] = value.DeepClone();
            }
        }
    }

    private static string Sanitize(string key)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = key.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars).Trim();
        return string.IsNullOrEmpty(cleaned) ? "default" : cleaned;
    }
}
