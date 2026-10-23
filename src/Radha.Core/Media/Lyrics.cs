using System.Globalization;
using System.Text;

namespace Radha.Core.Media;

public readonly record struct LyricLine(TimeSpan At, string Text);

/// <summary>
/// LRC parser. Handles multiple timestamps per line, `[mm:ss]` and `[mm:ss.fff]`, offset tags and
/// metadata (`[ar:]`, `[ti:]`, `[al:]`), and ignores anything it does not understand instead of
/// throwing — lyrics arrive from the internet and must never break playback.
/// </summary>
public static class Lrc
{
    public sealed record Document(
        IReadOnlyList<LyricLine> Lines,
        string? Title,
        string? Artist,
        string? Album,
        TimeSpan Offset,
        bool Synced)
    {
        public static Document Empty { get; } = new(Array.Empty<LyricLine>(), null, null, null, TimeSpan.Zero, false);
    }

    public static Document Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Document.Empty;

        var lines = new List<LyricLine>();
        string? title = null, artist = null, album = null;
        var offset = TimeSpan.Zero;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var stamps = new List<TimeSpan>();
            var rest = line;

            while (rest.StartsWith('['))
            {
                var close = rest.IndexOf(']');
                if (close < 0) break;

                var tag = rest[1..close];
                rest = rest[(close + 1)..].TrimStart();

                if (TryParseTimestamp(tag, out var at)) stamps.Add(at);
                else if (tag.StartsWith("offset:", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(tag[7..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                    offset = TimeSpan.FromMilliseconds(ms);
                else if (tag.StartsWith("ti:", StringComparison.OrdinalIgnoreCase)) title = Clean(tag[3..]);
                else if (tag.StartsWith("ar:", StringComparison.OrdinalIgnoreCase)) artist = Clean(tag[3..]);
                else if (tag.StartsWith("al:", StringComparison.OrdinalIgnoreCase)) album = Clean(tag[3..]);
            }

            if (stamps.Count == 0 || rest.Length == 0) continue;
            foreach (var at in stamps) lines.Add(new LyricLine(at, rest));
        }

        lines.Sort((a, b) => a.At.CompareTo(b.At));
        return new Document(lines, title, artist, album, offset, lines.Count > 0);
    }

    /// <summary>Index of the line that should be highlighted at <paramref name="position"/>, or -1.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        if (lines.Count == 0) return -1;
        if (position < lines[0].At) return -1;

        var low = 0;
        var high = lines.Count - 1;
        var found = 0;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (lines[mid].At <= position) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }

        return found;
    }

    /// <summary>Current and next line, which is what a media card actually renders.</summary>
    public static (string Current, string Next) At(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        var index = IndexAt(lines, position);
        if (index < 0) return (lines.Count > 0 ? lines[0].Text : string.Empty, lines.Count > 1 ? lines[1].Text : string.Empty);

        var current = lines[index].Text;
        var next = index + 1 < lines.Count ? lines[index + 1].Text : string.Empty;
        return (current, next);
    }

    public static string ToLrc(IEnumerable<LyricLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var total = line.At.TotalMilliseconds;
            var minutes = (int)(total / 60000);
            var seconds = (int)((total % 60000) / 1000);
            var hundredths = (int)((total % 1000) / 10);
            builder.Append($"[{minutes:00}:{seconds:00}.{hundredths:00}]{line.Text}\n");
        }
        return builder.ToString();
    }

    private static bool TryParseTimestamp(string tag, out TimeSpan value)
    {
        value = default;
        var colon = tag.IndexOf(':');
        if (colon <= 0) return false;

        if (!int.TryParse(tag[..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) return false;

        var remainder = tag[(colon + 1)..];
        if (!double.TryParse(remainder, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return false;
        if (seconds < 0 || seconds >= 60) return false;

        value = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string Clean(string value) => value.Trim();
}

/// <summary>
/// LRCLIB search URL builder (the endpoint caelestia uses for LRCLIB). Kept pure so the query
/// shaping — which is where encoding bugs live — is unit-testable without network access.
/// </summary>
public static class LyricsQuery
{
    public const string BaseUrl = "https://lrclib.net/api";

    public static string Search(string title, string artist, string? album = null, int? durationSeconds = null)
    {
        var query = new List<string>
        {
            "track_name=" + Uri.EscapeDataString(title.Trim()),
            "artist_name=" + Uri.EscapeDataString(artist.Trim())
        };

        if (!string.IsNullOrWhiteSpace(album)) query.Add("album_name=" + Uri.EscapeDataString(album.Trim()));
        if (durationSeconds is > 0) query.Add("duration=" + durationSeconds.Value.ToString(CultureInfo.InvariantCulture));

        return $"{BaseUrl}/search?{string.Join('&', query)}";
    }

    public static string Get(int trackId) => $"{BaseUrl}/get/{trackId}";

    /// <summary>Plain-text fallback used when only unsynced lyrics exist.</summary>
    public static IReadOnlyList<LyricLine> Unsynced(string text) =>
        text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .Select(l => new LyricLine(TimeSpan.Zero, l))
            .ToList();
}
