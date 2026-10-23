using System.Globalization;

namespace Radha.Core.Updates;

public sealed record ReleaseInfo(string Tag, bool Prerelease, string? Url = null, DateTimeOffset? Published = null);

public enum UpdateState { None, Available, PrereleaseOnly, Unknown }

public sealed record UpdateResult(UpdateState State, Version? Available, ReleaseInfo? Release, string Summary)
{
    public bool IsActionable => State == UpdateState.Available;
}

/// <summary>
/// Version comparison and release selection for the update check.
///
/// The shell ships as a zip next to a tag, so "is there a newer build" is a string problem before
/// it is a network problem. Tags are parsed strictly: `v1.2.3`, `1.2.3`, optional `-pre.N`.
/// </summary>
public static class ReleaseCheck
{
    public static bool TryParse(string? tag, out Version version, out bool prerelease)
    {
        version = new Version(0, 0, 0);
        prerelease = false;
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = true;
            text = text[..dash];
        }

        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4) return false;

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0)
                return false;
            numbers[i] = n;
        }

        if (parts.Length == 1) numbers[1] = 0;
        if (parts.Length <= 2) numbers[2] = 0;

        version = parts.Length <= 3 ? new Version(numbers[0], numbers[1], numbers[2]) : new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>
    /// Picks the best release newer than <paramref name="current"/>. Stable builds ignore
    /// prereleases; a prerelease build will accept a newer prerelease.
    /// </summary>
    public static UpdateResult Pick(IEnumerable<ReleaseInfo> releases, Version current, bool currentIsPrerelease, bool includePrerelease = false)
    {
        var candidates = releases
            .Select(r => (Release: r, Ok: TryParse(r.Tag, out var v, out var pre), Version: v, Pre: pre))
            .Where(x => x.Ok)
            .Where(x => includePrerelease || currentIsPrerelease || !x.Pre)
            .Where(x => x.Version > current)
            .OrderByDescending(x => x.Version)
            .ThenByDescending(x => x.Release.Published ?? DateTimeOffset.MinValue)
            .ToList();

        if (candidates.Count == 0)
        {
            var anyPrerelease = releases
                .Select(r => TryParse(r.Tag, out var v, out var pre) ? (true, v, pre) : (false, default, false))
                .Any(x => x.Item1 && x.v > current);
            return anyPrerelease
                ? new UpdateResult(UpdateState.PrereleaseOnly, null, null, "a newer prerelease exists; stable channel is current")
                : new UpdateResult(UpdateState.None, current, null, "up to date");
        }

        var best = candidates[0];
        return new UpdateResult(
            UpdateState.Available,
            best.Version,
            best.Release,
            $"update available: v{best.Version}");
    }

    /// <summary>Parses the version the assembly was built with, tolerating the CI's build metadata.</summary>
    public static Version CurrentOrDefault(string? informational)
    {
        if (TryParse(informational, out var version, out _)) return version;
        return new Version(0, 0, 0);
    }
}
