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

    public string SaveMonitorOverride(string monitorKey, string json)
    {
        var dir = Path.Combine(MonitorConfigDirectory, Sanitize(monitorKey));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "shell.json");
        JsonNode.Parse(json); // validate before writing
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
