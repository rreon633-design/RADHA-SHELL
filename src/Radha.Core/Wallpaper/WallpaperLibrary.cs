using System.Security.Cryptography;
using System.Text;
using Radha.Core.Config;
using Radha.Core.Theming;

namespace Radha.Core.Wallpaper;

/// <summary>How an image is placed on a monitor.</summary>
public enum WallpaperFit
{
    Fill,
    Fit,
    Stretch,
    Center,
    Span
}

/// <summary>One image in the picker.</summary>
public readonly record struct WallpaperEntry(string Path, string Name, long Bytes, DateTime ModifiedUtc);

/// <summary>What the shell should do when the wallpaper changes.</summary>
public enum TransitionMode
{
    /// <summary>Cut. Cheapest, and what you want on a battery.</summary>
    None,

    /// <summary>Cross-fade the old frame into the new one.</summary>
    CrossFade
}

/// <summary>A resolved transition: a duration (zero when motion is reduced) and whether to blend.</summary>
public readonly record struct WallpaperTransition(TimeSpan Duration, bool Blend, string Shader)
{
    public static WallpaperTransition Cut { get; } = new(TimeSpan.Zero, false, "none");
}

/// <summary>
/// The filesystem and policy half of wallpapers, deliberately free of Win32.
///
/// The picker, the CLI and the daemon all need to answer the same questions — what is on disk,
/// what thumbnail is already cached, which image belongs to which monitor — and none of those
/// answers should depend on a window existing. Keeping them here means they can be tested on Linux
/// and cannot drift between the three callers.
/// </summary>
public static class WallpaperLibrary
{
    /// <summary>Formats Windows can actually set as a desktop background.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };

    public const int MaxEntriesDefault = 512;

    public static WallpaperFit ParseFit(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "fit" => WallpaperFit.Fit,
        "stretch" => WallpaperFit.Stretch,
        "center" => WallpaperFit.Center,
        "span" => WallpaperFit.Span,
        "fill" => WallpaperFit.Fill,
        _ => WallpaperFit.Fill
    };

    public static string ToConfigString(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => "fit",
        WallpaperFit.Stretch => "stretch",
        WallpaperFit.Center => "center",
        WallpaperFit.Span => "span",
        _ => "fill"
    };

    /// <summary>
    /// Lists images in <paramref name="root"/>, newest-name-ordered, capped.
    ///
    /// A missing or unreadable directory is an empty list, not an exception: the picker on a fresh
    /// install has nothing to show and that is a normal state, not an error to surface.
    /// </summary>
    public static IReadOnlyList<WallpaperEntry> Scan(string? root, int maxEntries = MaxEntriesDefault)
    {
        if (string.IsNullOrWhiteSpace(root) || maxEntries <= 0) return Array.Empty<WallpaperEntry>();

        try
        {
            if (!Directory.Exists(root)) return Array.Empty<WallpaperEntry>();

            var entries = new List<WallpaperEntry>();
            foreach (var file in Directory.EnumerateFiles(root))
            {
                var extension = Path.GetExtension(file);
                if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) continue;

                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                    if (!info.Exists || info.Length == 0) continue;
                }
                catch { continue; }

                entries.Add(new WallpaperEntry(file, Path.GetFileNameWithoutExtension(file), info.Length, info.LastWriteTimeUtc));
                if (entries.Count >= maxEntries) break;
            }

            entries.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return entries;
        }
        catch
        {
            return Array.Empty<WallpaperEntry>();
        }
    }

    /// <summary>
    /// A cache key for a thumbnail of <paramref name="entry"/> at a given size.
    ///
    /// It includes size and modification time, so editing a file in place produces a new key instead
    /// of a stale picture, and it is a hash so the key is safe to use as a filename on any volume
    /// (a path cannot be, and would also break the 260-character limit for deep libraries).
    /// </summary>
    public static string ThumbnailKey(WallpaperEntry entry, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var identity = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{entry.Path.ToLowerInvariant()}|{entry.ModifiedUtc.Ticks}|{entry.Bytes}|{width}x{height}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant() + ".png";
    }

    /// <summary>
    /// Which image <paramref name="monitorId"/> should get.
    ///
    /// Order: an assignment for that exact monitor, then an explicit <c>"*"</c> catch-all, then the
    /// global wallpaper. Monitor ids are device paths that survive reboots, so an assignment sticks
    /// when a dock is reconnected.
    /// </summary>
    public static string? Resolve(string? monitorId, IReadOnlyDictionary<string, string>? perMonitor, string? fallback)
    {
        if (perMonitor is { Count: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(monitorId))
            {
                foreach (var pair in perMonitor)
                {
                    if (string.Equals(pair.Key, monitorId, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(pair.Value))
                        return pair.Value;
                }
            }

            foreach (var pair in perMonitor)
            {
                if (pair.Key is "*" && !string.IsNullOrWhiteSpace(pair.Value)) return pair.Value;
            }
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    /// <summary>
    /// Cleans a per-monitor assignment map before it is written to config.
    ///
    /// Unknown monitor ids are kept on purpose: a laptop's second screen disappears the moment it is
    /// undocked, and silently deleting the assignment would lose the user's choice. Only entries
    /// that could never be used — empty, blank monitor or non-rooted image path — are dropped.
    /// </summary>
    public static IReadOnlyDictionary<string, string> SanitizeAssignments(IReadOnlyDictionary<string, string>? assignments)
    {
        if (assignments is null || assignments.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (monitor, path) in assignments)
        {
            if (string.IsNullOrWhiteSpace(monitor)) continue;
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!IsRootedImagePath(path)) continue;

            // Last write wins, and a duplicate differing only by case replaces rather than doubles.
            clean[monitor.Trim()] = path.Trim();
        }
        return clean;
    }

    /// <summary>True when the path is absolute and has a supported image extension.</summary>
    public static bool IsRootedImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/');
        var drive = normalized.Length >= 3 && char.IsLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/';
        var rooted = drive || normalized.StartsWith("//", StringComparison.Ordinal) || Path.IsPathRooted(normalized);
        if (!rooted) return false;

        var extension = Path.GetExtension(normalized);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves what a wallpaper change should look like.
    ///
    /// Reduced motion wins over every other consideration: the transition collapses to a cut, so the
    /// accessibility setting cannot be overridden by a config value.
    /// </summary>
    public static WallpaperTransition PlanTransition(TransitionMode mode, MotionTokens motion)
    {
        motion = motion.Normalized();
        if (mode == TransitionMode.None || motion.Reduced) return WallpaperTransition.Cut;

        var duration = motion.Slow;
        return duration <= TimeSpan.Zero ? WallpaperTransition.Cut : new WallpaperTransition(duration, true, "crossfade");
    }

    /// <summary>Where rotation should move to, honouring shuffle without repeating the current image.</summary>
    public static int NextIndex(int current, int count, bool shuffle, Random? random = null)
    {
        if (count <= 1) return 0;
        if (!shuffle) return (current + 1) % count;

        random ??= Random.Shared;
        // Pick from the count-1 others so pressing "next" twice can never look like a no-op.
        var candidate = random.Next(count - 1);
        return candidate >= current ? candidate + 1 : candidate;
    }
}
