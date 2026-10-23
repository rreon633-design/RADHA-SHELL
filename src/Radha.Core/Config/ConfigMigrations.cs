using System.Text.Json.Nodes;

namespace Radha.Core.Config;

/// <summary>
/// Forward-only config migrations.
///
/// Every migration takes a JSON document and returns it in the next schema version without
/// discarding anything it does not understand: unknown keys survive because the document is
/// mutated in place rather than round-tripped through <see cref="ShellConfig"/>.
/// </summary>
public static class ConfigMigrations
{
    /// <summary>Schema version this build writes.</summary>
    public const int Current = 2;

    public const string VersionKey = "schemaVersion";

    /// <summary>One step: (from, to, description, mutator). Applied in ascending order.</summary>
    private static readonly (int From, int To, string What, Action<JsonObject> Apply)[] Steps =
    {
        (0, 1, "hoist legacy flat appearance keys into \"appearance\"", HoistAppearance),
        (1, 2, "rename bar.showSeconds → bar.showClockSeconds", RenameClockKey)
    };

    public static IReadOnlyList<string> Descriptions => Steps.Select(s => $"v{s.From}→v{s.To}: {s.What}").ToList();

    /// <summary>Version a document declares; absent means 0 (pre-versioning).</summary>
    public static int VersionOf(JsonObject document) =>
        document.TryGetPropertyValue(VersionKey, out var node) && node is not null && node.GetValueKind() is
            System.Text.Json.JsonValueKind.Number ? node.GetValue<int>() : 0;

    /// <summary>
    /// Bring <paramref name="document"/> up to <see cref="Current"/>. Returns the descriptions of
    /// the migrations that actually ran, so the caller can log them.
    /// </summary>
    public static IReadOnlyList<string> Migrate(JsonObject document)
    {
        var applied = new List<string>();
        var version = VersionOf(document);

        foreach (var step in Steps)
        {
            if (version < step.From) continue;
            if (version >= step.To) continue;

            step.Apply(document);
            version = step.To;
            applied.Add($"v{step.From}→v{step.To}: {step.What}");
        }

        document[VersionKey] = Math.Max(version, Current);
        return applied;
    }

    /// <summary>True when the document was written by a newer build than this one.</summary>
    public static bool IsFromFuture(JsonObject document) => VersionOf(document) > Current;

    // ---------------- steps ----------------

    /// <summary>v0 shipped appearance keys at the root; v1 nests them.</summary>
    private static void HoistAppearance(JsonObject root)
    {
        string[] legacy = { "mode", "schemeVariant", "transparency", "blurRadius", "font", "monoFont", "scale", "useSystemAccent" };

        var appearance = root["appearance"] as JsonObject;
        foreach (var key in legacy)
        {
            if (!root.TryGetPropertyValue(key, out var value) || value is null) continue;
            appearance ??= new JsonObject();
            // Never overwrite an explicit nested value; the nested form is authoritative.
            if (!appearance.ContainsKey(key)) appearance[key] = value.DeepClone();
            root.Remove(key);
        }

        if (appearance is not null) root["appearance"] = appearance;
    }

    /// <summary>v2 renamed the clock-seconds key so it reads like the rest of the bar section.</summary>
    private static void RenameClockKey(JsonObject root)
    {
        if (root["bar"] is not JsonObject bar) return;
        if (!bar.TryGetPropertyValue("showSeconds", out var value) || value is null) return;
        if (!bar.ContainsKey("showClockSeconds")) bar["showClockSeconds"] = value.DeepClone();
        bar.Remove("showSeconds");
    }
}
