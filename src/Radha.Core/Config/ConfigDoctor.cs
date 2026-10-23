using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Radha.Core.Config;

public enum DoctorSeverity { Info, Warning, Error }

public readonly record struct DoctorFinding(
    DoctorSeverity Severity,
    string Path,
    string Message,
    bool Fixable);

/// <summary>
/// Validates <c>shell.json</c> the way a person would: reports every problem with the exact key
/// that caused it, and can repair the repairable ones without silently dropping user settings.
/// </summary>
public sealed class ConfigDoctor
{
    private static readonly JsonSerializerOptions Options = ConfigStore.JsonOptions;

    /// <summary>Numeric bounds enforced on load. Unlisted numbers are unconstrained.</summary>
    private static readonly (string Path, double Min, double Max)[] Ranges =
    {
        ("appearance.scale", 0.5, 3),
        ("appearance.fontScale", 0.5, 3),
        ("appearance.roundingScale", 0, 4),
        ("appearance.blurRadius", 0, 64),
        ("appearance.animationScale", 0, 4),
        ("bar.height", 16, 120),
        ("bar.workspaceCount", 1, 32)
    };

    /// <summary>Paths that must exist as strings.</summary>
    private static readonly string[] RequiredStrings = { "appearance.mode", "bar.position" };

    private readonly HashSet<string> _known;

    public ConfigDoctor()
    {
        _known = KnownPaths();
    }

    public DoctorReport Scan(string json, string? source = null)
    {
        var findings = new List<DoctorFinding>();
        JsonObject document;

        try
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (node is not JsonObject obj)
                return new DoctorReport(source, ConfigMigrations.Current, false, new[]
                {
                    new DoctorFinding(DoctorSeverity.Error, "$", "the document root must be a JSON object", false)
                }, null);
            document = obj;
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" (line {line + 1})" : string.Empty;
            return new DoctorReport(source, 0, false, new[]
            {
                new DoctorFinding(DoctorSeverity.Error, "$", $"invalid JSON{where}: {ex.Message}", false)
            }, null);
        }

        var version = ConfigMigrations.VersionOf(document);
        if (ConfigMigrations.IsFromFuture(document))
            findings.Add(new DoctorFinding(DoctorSeverity.Error, ConfigMigrations.VersionKey,
                $"written by a newer build (schema {version}, this build reads {ConfigMigrations.Current})", false));
        else if (version < ConfigMigrations.Current)
            findings.Add(new DoctorFinding(DoctorSeverity.Info, ConfigMigrations.VersionKey,
                $"schema {version} will be migrated to {ConfigMigrations.Current} on start", true));

        Walk(document, string.Empty, findings);
        foreach (var (path, min, max) in Ranges) CheckRange(document, path, min, max, findings);
        foreach (var path in RequiredStrings) CheckString(document, path, findings);

        var typeProblems = CheckAgainstModel(document, findings);

        var fixable = findings.Any(f => f.Fixable);
        return new DoctorReport(source, version, fixable, findings, typeProblems);
    }

    /// <summary>
    /// Repairs what can be repaired: clamps out-of-range numbers, restores wrong types to their
    /// defaults, removes keys that are not part of the schema, and writes the current version.
    /// Returns the new document plus a report of what was done.
    /// </summary>
    public (string Json, DoctorReport Report) Fix(string json)
    {
        var report = Scan(json);
        if (report.Findings.All(f => f.Severity != DoctorSeverity.Error) is false &&
            report.Findings.Any(f => f.Message.Contains("invalid JSON", StringComparison.Ordinal)))
            return (json, report); // unparseable: nothing safe to do automatically

        var document = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        }) as JsonObject ?? new JsonObject();

        var fixedPaths = new List<string>();

        ConfigMigrations.Migrate(document);

        foreach (var (path, min, max) in Ranges)
        {
            if (NodeAt(document, path) is not JsonValue value || !value.TryGetValue<double>(out var number)) continue;
            var clamped = Math.Clamp(number, min, max);
            if (Math.Abs(clamped - number) < double.Epsilon) continue;
            SetNode(document, path, clamped);
            fixedPaths.Add($"{path}: {number.ToString(CultureInfo.InvariantCulture)} → {clamped.ToString(CultureInfo.InvariantCulture)}");
        }

        RemoveUnknown(document, string.Empty, fixedPaths);

        var rescan = Scan(document.ToJsonString(Options));
        return (document.ToJsonString(Options), rescan with { Fixes = fixedPaths });
    }

    // ---------------- checks ----------------

    private void Walk(JsonObject node, string prefix, List<DoctorFinding> findings)
    {
        foreach (var (key, value) in node)
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (value is JsonObject child)
            {
                Walk(child, path, findings);
                continue;
            }

            if (value is JsonArray or null) continue;
            if (path == ConfigMigrations.VersionKey) continue;
            if (_known.Contains(path)) continue;
            if (IsCollectionPath(path)) continue;

            findings.Add(new DoctorFinding(DoctorSeverity.Warning, path, "unknown key, ignored by this build", true));
        }
    }

    private bool IsCollectionPath(string path) =>
        _known.Any(k => k.StartsWith(path + ".", StringComparison.Ordinal));

    private static void CheckRange(JsonObject document, string path, double min, double max, List<DoctorFinding> findings)
    {
        if (NodeAt(document, path) is not JsonValue value) return;
        if (!value.TryGetValue<double>(out var number))
        {
            findings.Add(new DoctorFinding(DoctorSeverity.Error, path, "must be a number", true));
            return;
        }

        if (number < min || number > max)
            findings.Add(new DoctorFinding(DoctorSeverity.Error, path,
                $"{number.ToString(CultureInfo.InvariantCulture)} is outside {min.ToString(CultureInfo.InvariantCulture)}..{max.ToString(CultureInfo.InvariantCulture)}", true));
    }

    private static void CheckString(JsonObject document, string path, List<DoctorFinding> findings)
    {
        if (NodeAt(document, path) is not JsonValue value) return;
        if (!value.TryGetValue<string>(out var text))
            findings.Add(new DoctorFinding(DoctorSeverity.Error, path, "must be a string", true));
        else if (string.IsNullOrWhiteSpace(text))
            findings.Add(new DoctorFinding(DoctorSeverity.Error, path, "must not be empty", true));
    }

    /// <summary>Deserialising is the only honest check for enum spellings and nested shapes.</summary>
    private static string? CheckAgainstModel(JsonObject document, List<DoctorFinding> findings)
    {
        try
        {
            var config = document.Deserialize<ShellConfig>(Options);
            if (config is null) findings.Add(new DoctorFinding(DoctorSeverity.Error, "$", "deserialised to null", false));
            return null;
        }
        catch (JsonException ex)
        {
            var path = ex.Path?.TrimStart('$', '.') ?? "$";
            findings.Add(new DoctorFinding(DoctorSeverity.Error, path.Length == 0 ? "$" : path, ex.Message, false));
            return path;
        }
    }

    // ---------------- node helpers ----------------

    private static JsonNode? NodeAt(JsonObject root, string path)
    {
        JsonNode? current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(part, out var next)) return null;
            current = next;
        }
        return current;
    }

    private static void SetNode(JsonObject root, string path, double value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonObject current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JsonObject next) return;
            current = next;
        }
        current[parts[^1]] = JsonValue.Create(value);
    }

    private void RemoveUnknown(JsonObject node, string prefix, List<string> fixedPaths)
    {
        foreach (var key in node.Select(kv => kv.Key).ToList())
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (node[key] is JsonObject child)
            {
                RemoveUnknown(child, path, fixedPaths);
                if (child.Count == 0 && !_known.Contains(path)) { node.Remove(key); fixedPaths.Add($"{path}: removed empty object"); }
                continue;
            }

            if (path == ConfigMigrations.VersionKey) continue;
            if (_known.Contains(path) || IsCollectionPath(path)) continue;
            node.Remove(key);
            fixedPaths.Add($"{path}: removed unknown key");
        }
    }

    /// <summary>Every leaf path the model can produce — derived from the model, never hand-listed.</summary>
    private static HashSet<string> KnownPaths()
    {
        var json = JsonSerializer.Serialize(new ShellConfig(), Options);
        var root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        Collect(root, string.Empty, paths);
        return paths;
    }

    private static void Collect(JsonNode? node, string prefix, HashSet<string> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
                    into.Add(path);
                    Collect(value, path, into);
                }
                break;
            case JsonArray array when array.Count > 0:
                foreach (var item in array) Collect(item, prefix + "[]", into);
                break;
        }
    }
}

public sealed record DoctorReport(
    string? Source,
    int Version,
    bool Fixable,
    IReadOnlyList<DoctorFinding> Findings,
    string? FailingPath)
{
    public IReadOnlyList<string> Fixes { get; init; } = Array.Empty<string>();

    public int Errors => Findings.Count(f => f.Severity == DoctorSeverity.Error);
    public int Warnings => Findings.Count(f => f.Severity == DoctorSeverity.Warning);
    public bool Healthy => Errors == 0;

    /// <summary>One-line verdict for the CLI.</summary>
    public string Summary => Healthy
        ? Warnings == 0 ? "config is clean" : $"config is valid ({Warnings} warning(s))"
        : $"{Errors} error(s), {Warnings} warning(s)";
}
