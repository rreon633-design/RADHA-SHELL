using Radha.Core.Search;

namespace Radha.Core.Launcher;

public enum LauncherKind { App, Action, Command, Calculator }

/// <summary>One searchable thing. <see cref="Payload"/> is what the host executes, never a shell string.</summary>
public sealed record LauncherEntry(
    string Id,
    LauncherKind Kind,
    string Title,
    string Subtitle,
    string Icon,
    string? Payload,
    bool Dangerous = false);

public sealed record LauncherHit(LauncherEntry Entry, int Score, string? Detail);

public sealed record LauncherPrefs(
    IReadOnlyList<string> Favourites,
    IReadOnlyList<string> Hidden,
    bool EnableDangerous = false,
    bool VimKeys = false,
    string ActionPrefix = ">",
    string CommandPrefix = "?");

/// <summary>
/// The launcher's decision layer: what a keystroke should show, in what order, and what it means.
///
/// All of it is pure so the ranking rules can be tested without a window: prefix search beats
/// substring beats subsequence, favourites float on an empty query, hidden entries stay hidden,
/// dangerous actions are gated, and anything that evaluates as arithmetic appears as a calculator
/// row above the app results.
/// </summary>
public sealed class LauncherEngine
{
    private readonly List<LauncherEntry> _apps;
    private readonly List<LauncherEntry> _actions;
    private readonly List<LauncherEntry> _commands;
    private readonly LauncherPrefs _prefs;

    public LauncherEngine(
        IEnumerable<LauncherEntry> apps,
        IEnumerable<LauncherEntry> actions,
        IEnumerable<LauncherEntry>? commands = null,
        LauncherPrefs? prefs = null)
    {
        _apps = apps.ToList();
        _actions = actions.ToList();
        _commands = commands?.ToList() ?? new List<LauncherEntry>();
        _prefs = prefs ?? new LauncherPrefs(Array.Empty<string>(), Array.Empty<string>());
    }

    public int AppCount => _apps.Count;
    public int ActionCount => _actions.Count;

    public IReadOnlyList<LauncherHit> Query(string? query, int limit = 8)
    {
        limit = Math.Clamp(limit, 1, 100);
        var text = (query ?? string.Empty).Trim();

        // Calculator first: "2+2" should answer immediately, above anything it might fuzzy-match.
        var hits = new List<LauncherHit>();
        if (TryCalculate(text, out var calc) && calc is not null) hits.Add(calc);

        if (text.Length == 0)
        {
            hits.AddRange(BlankHits(limit - hits.Count));
            return hits.Count <= limit ? hits : hits.Take(limit).ToList();
        }

        if (text.StartsWith(_prefs.ActionPrefix, StringComparison.Ordinal))
        {
            hits.AddRange(Rank(_actions, text[_prefs.ActionPrefix.Length..].Trim(), limit - hits.Count, allowDangerous: _prefs.EnableDangerous));
            return hits.Take(limit).ToList();
        }

        if (text.StartsWith(_prefs.CommandPrefix, StringComparison.Ordinal))
        {
            hits.AddRange(Rank(_commands, text[_prefs.CommandPrefix.Length..].Trim(), limit - hits.Count, allowDangerous: _prefs.EnableDangerous));
            return hits.Take(limit).ToList();
        }

        hits.AddRange(Rank(_apps, text, limit - hits.Count, allowDangerous: false));

        // Actions are still reachable by name without the prefix, ranked below apps.
        if (hits.Count < limit)
            hits.AddRange(Rank(_actions, text, limit - hits.Count, allowDangerous: _prefs.EnableDangerous, penalty: 150));

        return hits.Take(limit).ToList();
    }

    private IEnumerable<LauncherHit> BlankHits(int limit)
    {
        if (limit <= 0) yield break;

        var favouriteIds = new HashSet<string>(_prefs.Favourites, StringComparer.OrdinalIgnoreCase);
        var shown = 0;

        foreach (var id in _prefs.Favourites)
        {
            var entry = _apps.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is null || IsHidden(entry)) continue;
            yield return new LauncherHit(entry, 2000, "favourite");
            if (++shown >= limit) yield break;
        }

        foreach (var entry in _apps.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            if (shown >= limit) yield break;
            if (IsHidden(entry) || favouriteIds.Contains(entry.Id)) continue;
            yield return new LauncherHit(entry, 1000, null);
            shown++;
        }
    }

    private IEnumerable<LauncherHit> Rank(
        List<LauncherEntry> source,
        string query,
        int limit,
        bool allowDangerous,
        int penalty = 0)
    {
        if (limit <= 0) yield break;
        if (string.IsNullOrWhiteSpace(query))
        {
            var blank = source
                .Where(e => allowDangerous || !e.Dangerous)
                .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .Select(e => new LauncherHit(e, 1000 - penalty, null))
                .ToList();
            foreach (var hit in blank) yield return hit;
            yield break;
        }

        var scored = new List<LauncherHit>();
        foreach (var entry in source)
        {
            if (!allowDangerous && entry.Dangerous) continue;
            if (_prefs.KindHidden(entry)) continue;

            var score = Math.Max(
                FuzzyMatcher.Score(entry.Title, query),
                FuzzyMatcher.Score(entry.Subtitle, query) * 3 / 4);

            if (score <= FuzzyMatcher.NoMatch) continue;
            scored.Add(new LauncherHit(entry, score - penalty, entry.Dangerous ? "destructive" : null));
        }

        foreach (var hit in scored
                     .OrderByDescending(h => h.Score)
                     .ThenBy(h => h.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
                     .Take(limit))
            yield return hit;
    }

    private bool IsHidden(LauncherEntry entry) =>
        _prefs.Hidden.Contains(entry.Id, StringComparer.OrdinalIgnoreCase);

    private bool TryCalculate(string text, out LauncherHit? hit)
    {
        hit = null;
        if (text.Length < 3) return false;
        if (!Calculator.LooksLikeExpression(text)) return false;
        if (!Calculator.TryEvaluate(text, out var value)) return false;

        hit = new LauncherHit(
            new LauncherEntry(
                "calc:" + text,
                LauncherKind.Calculator,
                Calculator.Format(value),
                text + " =",
                "calculator",
                Calculator.Format(value)),
            5000,
            null);
        return true;
    }

    /// <summary>
    /// Vim-style list navigation. Returns the index to move to, so the view model never encodes
    /// key handling itself and the behaviour can be tested.
    /// </summary>
    public static bool TryNavigate(char key, bool vim, int index, int count, out int next)
    {
        next = index;
        if (count <= 0) return false;

        switch (key)
        {
            case '\u001b': // Esc
                next = index;
                return false;
            case 'j' when vim:
            case 'J' when vim:
                next = Math.Min(index + 1, count - 1);
                return index != next;
            case 'k' when vim:
            case 'K' when vim:
                next = Math.Max(index - 1, 0);
                return index != next;
            case 'g' when vim:
                next = 0;
                return index != next;
            default:
                return false;
        }
    }
}

internal static class LauncherPrefsExtensions
{
    public static bool KindHidden(this LauncherPrefs prefs, LauncherEntry entry) =>
        prefs.Hidden.Contains(entry.Id, StringComparer.OrdinalIgnoreCase);
}
