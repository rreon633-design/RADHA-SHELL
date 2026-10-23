using System.Text.Json;
using System.Text.Json.Nodes;

namespace Radha.Core.Config;

/// <summary>
/// A named bundle of settings a user can switch between in one action: "work", "gaming", "dim".
///
/// A profile stores only the sections it means to own, as raw JSON, so a profile written by an
/// older build cannot invalidate keys it knows nothing about — it is merged onto the live config
/// exactly like a per-monitor override.
/// </summary>
public sealed record AppearanceProfile(string Name, string Json, DateTimeOffset Modified)
{
    public static AppearanceProfile Create(string name, ShellConfig config)
    {
        var node = JsonSerializer.SerializeToNode(config, ConfigStore.JsonOptions) as JsonObject ?? new JsonObject();

        // Profiles deliberately carry appearance, theme and wallpaper; not paths or services.
        var trimmed = new JsonObject();
        foreach (var key in new[] { "appearance", "bar", "wallpaper" })
            if (node[key] is JsonNode value) trimmed[key] = value.DeepClone();

        return new AppearanceProfile(name, trimmed.ToJsonString(ConfigStore.JsonOptions), DateTimeOffset.Now);
    }

    /// <summary>Merges the profile onto a base config and returns the result.</summary>
    public ShellConfig ApplyTo(ShellConfig baseConfig)
    {
        JsonObject root;
        try
        {
            root = JsonSerializer.SerializeToNode(baseConfig, ConfigStore.JsonOptions) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return baseConfig;
        }

        try
        {
            if (JsonNode.Parse(Json) is JsonObject overlay) Merge(root, overlay);
        }
        catch (JsonException)
        {
            return baseConfig;   // a corrupt profile must never take the config with it
        }

        try
        {
            return root.Deserialize<ShellConfig>(ConfigStore.JsonOptions)?.Normalized() ?? baseConfig;
        }
        catch (JsonException)
        {
            return baseConfig;
        }
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject child && target[key] is JsonObject existing) Merge(existing, child);
            else target[key] = value?.DeepClone();
        }
    }
}

/// <summary>
/// Profile storage under <c>%APPDATA%\Radha\profiles</c>. Names are sanitised the same way monitor
/// keys are, so a profile called <c>../../x</c> cannot write outside the directory.
/// </summary>
public sealed class ProfileStore
{
    public const int MaxProfileBytes = 64 * 1024;

    public ProfileStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(ConfigStore.DefaultRoot(), "profiles");
    }

    public string Directory { get; }

    public IReadOnlyList<AppearanceProfile> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return Array.Empty<AppearanceProfile>();

        var profiles = new List<AppearanceProfile>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                if (new FileInfo(file).Length > MaxProfileBytes) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                profiles.Add(new AppearanceProfile(name, File.ReadAllText(file), File.GetLastWriteTimeUtc(file)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public AppearanceProfile? Get(string name) =>
        List().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public string Save(AppearanceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("profile needs a name", nameof(profile));
        if (System.Text.Encoding.UTF8.GetByteCount(profile.Json) > MaxProfileBytes)
            throw new ArgumentException($"profile exceeds {MaxProfileBytes} bytes", nameof(profile));

        _ = JsonNode.Parse(profile.Json);   // validate before writing

        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(profile.Name);
        File.WriteAllText(path, profile.Json);
        return path;
    }

    public AppearanceProfile SaveFrom(string name, ShellConfig config)
    {
        var profile = AppearanceProfile.Create(name, config);
        Save(profile);
        return profile;
    }

    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Rejects separators and traversal; the resolved file must stay inside the directory.</summary>
    public string PathFor(string name)
    {
        var safe = Sanitize(name);
        var path = Path.Combine(Directory, safe + ".json");

        var root = Path.GetFullPath(Directory);
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException("profile path escaped the profiles directory");

        return path;
    }

    public static string Sanitize(string name)
    {
        var cleaned = new string(name
            .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')
            .ToArray())
            .Trim()
            .Replace(' ', '-');

        return cleaned.Length == 0 ? "profile" : cleaned.ToLowerInvariant();
    }
}
