namespace Radha.Core.Search;

/// <summary>
/// Launcher matcher. Deterministic, allocation-light and testable — the launcher view model
/// just calls <see cref="Score"/> and sorts.
///
/// Scoring intent:
///   prefix match            → strongest
///   substring match         → strong, earlier is better
///   subsequence match       → weaker, rewards consecutive runs and word starts
///   acronym ("vsc" → "Visual Studio Code") → explicit bonus
/// </summary>
public static class FuzzyMatcher
{
    public const int NoMatch = 0;

    public static int Score(string? candidate, string? query)
    {
        if (string.IsNullOrEmpty(candidate)) return NoMatch;
        if (string.IsNullOrEmpty(query)) return 1;

        var q = query.Trim();
        if (q.Length == 0) return 1;

        if (candidate.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 1000 - Math.Min(candidate.Length, 200);

        var index = candidate.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
            return 700 - Math.Min(index, 200) * 2 - Math.Min(candidate.Length, 100) / 4;

        var acronym = AcronymScore(candidate, q);
        var subsequence = SubsequenceScore(candidate, q);

        if (subsequence == NoMatch) return NoMatch;
        return Math.Max(subsequence, acronym);
    }

    public static bool Matches(string? candidate, string? query) => Score(candidate, query) > NoMatch;

    /// <summary>Best-first ranking; ties break on name for stable output.</summary>
    public static IReadOnlyList<T> Rank<T>(IEnumerable<T> items, string query, Func<T, string> selector, int limit = 12)
    {
        var scored = new List<(T Item, int Score, string Name)>();
        foreach (var item in items)
        {
            var name = selector(item) ?? string.Empty;
            var score = Score(name, query);
            if (score > NoMatch) scored.Add((item, score, name));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Max(1, limit))
            .Select(x => x.Item)
            .ToList();
    }

    private static int SubsequenceScore(string candidate, string query)
    {
        var score = 0;
        var queryIndex = 0;
        var run = 0;
        var previousMatch = -2;

        for (var i = 0; i < candidate.Length && queryIndex < query.Length; i++)
        {
            if (char.ToUpperInvariant(candidate[i]) != char.ToUpperInvariant(query[queryIndex]))
                continue;

            var isWordStart = i == 0 || candidate[i - 1] is ' ' or '-' or '_' or '.' or '(';
            var consecutive = i == previousMatch + 1;

            score += 4;
            if (isWordStart) score += 8;
            if (consecutive) { run++; score += 6 + run * 2; }
            else run = 0;

            previousMatch = i;
            queryIndex++;
        }

        if (queryIndex < query.Length) return NoMatch;

        // Shorter candidates that contain the same subsequence rank higher.
        return score - Math.Min(candidate.Length, 80) / 4;
    }

    private static int AcronymScore(string candidate, string query)
    {
        Span<char> initials = stackalloc char[32];
        var count = 0;
        var atWordStart = true;

        foreach (var c in candidate)
        {
            if (atWordStart && char.IsLetterOrDigit(c) && count < initials.Length)
            {
                initials[count++] = char.ToUpperInvariant(c);
                atWordStart = false;
                continue;
            }

            if (c is ' ' or '-' or '_' or '.') atWordStart = true;
        }

        if (count == 0) return NoMatch;

        var acronym = new string(initials[..count]);
        if (acronym.Equals(query, StringComparison.OrdinalIgnoreCase)) return 600 - count;
        if (acronym.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 500 - count;
        return NoMatch;
    }
}
