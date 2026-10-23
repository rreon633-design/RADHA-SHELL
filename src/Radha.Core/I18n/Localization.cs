using System.Globalization;
using System.Text.Json;

namespace Radha.Core.I18n;

/// <summary>
/// Runtime string catalogue. Missing keys fall back to the English catalogue, then to the key
/// itself — a missing translation must never render as an empty label or throw.
/// </summary>
public sealed class Translator
{
    private readonly Dictionary<string, string> _catalog;
    private readonly Dictionary<string, string> _fallback;
    private readonly CultureInfo _culture;

    public Translator(string language, IReadOnlyDictionary<string, string>? catalog = null,
                      IReadOnlyDictionary<string, string>? fallback = null)
    {
        Language = Normalize(language);
        // A trimmed runtime may only carry the invariant culture; formatting must still work.
        _culture = TryCulture(Language);
        _catalog = catalog is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(catalog, StringComparer.Ordinal);
        _fallback = fallback is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(fallback, StringComparer.Ordinal);
    }

    public string Language { get; }

    public static Translator English { get; } = new("en");

    public int Count => _catalog.Count;

    /// <summary>
    /// BCP-47 normalisation without touching ICU: trimming globalization out of the runtime must
    /// not change how the shell spells a language tag.
    /// </summary>
    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "en";

        var parts = language.Trim().Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "en";

        // The primary subtag is 2–3 ASCII letters; anything else is not a language tag.
        var primary = parts[0];
        if (primary.Length is < 2 or > 3 || !primary.All(char.IsAsciiLetter)) return "en";

        var result = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            result.Add(i switch
            {
                0 => part.ToLowerInvariant(),
                1 when part.Length == 4 => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant(), // script
                1 => part.ToUpperInvariant(),                                                             // region (en-GB)
                _ => part.ToLowerInvariant()
            });
        }

        return string.Join('-', result);
    }

    private static CultureInfo TryCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(string.IsNullOrEmpty(name) ? "en" : name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public string this[string key] => Tr(key);

    public string Tr(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (_catalog.TryGetValue(key, out var value) && value.Length > 0) return value;
        if (_fallback.TryGetValue(key, out var english) && english.Length > 0) return english;
        return key;
    }

    public string Tr(string key, params object?[] args)
    {
        var template = Tr(key);
        try { return string.Format(_culture, template, args); }
        catch (FormatException) { return template; }
    }

    /// <summary>
    /// Plural form lookup: tries <c>key.one</c>/<c>key.few</c>/… for the language's CLDR category,
    /// then <c>key.other</c>, then the bare key.
    /// </summary>
    public string TrPlural(string key, double count)
    {
        var category = PluralRules.Category(Language, count);
        foreach (var candidate in new[] { $"{key}.{category}", $"{key}.other", key })
        {
            if (_catalog.TryGetValue(candidate, out var value) && value.Length > 0)
                return FormatPlural(value, count);
            if (_fallback.TryGetValue(candidate, out var english) && english.Length > 0)
                return FormatPlural(english, count);
        }
        return key;
    }

    private string FormatPlural(string template, double count)
    {
        var number = Math.Abs(count - Math.Round(count)) < 1e-9
            ? Math.Round(count).ToString("0", _culture)
            : count.ToString("0.#", _culture);
        try { return string.Format(_culture, template, number); }
        catch (FormatException) { return template.Replace("{0}", number, StringComparison.Ordinal); }
    }

    public static Translator FromJson(string language, string json,
                                      IReadOnlyDictionary<string, string>? fallback = null)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String) continue;
                    map[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // A broken translation file is not fatal: English still renders.
        }

        return new Translator(language, map, fallback);
    }
}

/// <summary>CLDR plural categories, reduced to the languages worth shipping day one.</summary>
public static class PluralRules
{
    public static string Category(string? language, double count)
    {
        var lang = (language ?? "en").Split('-')[0].ToLowerInvariant();
        var n = Math.Abs(count);
        var isInteger = Math.Abs(n - Math.Round(n)) < 1e-9;
        var mod10 = (int)Math.Round(n) % 10;
        var mod100 = (int)Math.Round(n) % 100;

        return lang switch
        {
            "ar" => n == 0 ? "zero" : n == 1 ? "one" : n == 2 ? "two"
                    : mod100 is >= 3 and <= 10 ? "few" : mod100 is >= 11 and <= 99 ? "many" : "other",

            "ru" or "uk" or "be" or "sr" or "hr" =>
                isInteger && mod10 == 1 && mod100 != 11 ? "one"
                : isInteger && mod10 is >= 2 and <= 4 && mod100 is not (12 or 13 or 14) ? "few"
                : isInteger && (mod10 == 0 || mod10 is >= 5 and <= 9 || mod100 is >= 11 and <= 14) ? "many"
                : "other",

            "pl" => isInteger && n == 1 ? "one"
                    : isInteger && mod10 is >= 2 and <= 4 && mod100 is not (12 or 13 or 14) ? "few"
                    : isInteger ? "many" : "other",

            "cs" or "sk" => isInteger && n == 1 ? "one" : isInteger && n is >= 2 and <= 4 ? "few" : "other",

            "fr" or "pt" => n is >= 0 and < 2 ? "one" : "other",

            "ja" or "zh" or "ko" or "th" or "vi" => "other",

            _ => n == 1 ? "one" : "other"
        };
    }
}

public enum ClockFormat { Auto, TwelveHour, TwentyFourHour }
public enum TemperatureUnit { Auto, Celsius, Fahrenheit, Kelvin }
public enum DataUnit { Binary, Decimal }

/// <summary>
/// Locale-correct value formatting. Windows users mix all of these, so the shell never hard-codes
/// "°C" or "GB" again.
/// </summary>
public static class Units
{
    public static string FormatClock(DateTimeOffset time, ClockFormat format, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var twelve = format switch
        {
            ClockFormat.TwelveHour => true,
            ClockFormat.TwentyFourHour => false,
            _ => culture.DateTimeFormat.ShortTimePattern.Contains('h')
        };

        return twelve
            ? time.ToString("h:mm", culture)
            : time.ToString("HH:mm", culture);
    }

    public static bool IsTwelveHour(ClockFormat format, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return format switch
        {
            ClockFormat.TwelveHour => true,
            ClockFormat.TwentyFourHour => false,
            _ => culture.DateTimeFormat.ShortTimePattern.Contains('h')
        };
    }

    public static double Convert(double celsius, TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Fahrenheit => celsius * 9 / 5 + 32,
        TemperatureUnit.Kelvin => celsius + 273.15,
        _ => celsius
    };

    public static string FormatTemperature(double celsius, TemperatureUnit unit, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var value = Convert(celsius, unit);
        var suffix = unit switch
        {
            TemperatureUnit.Fahrenheit => "°F",
            TemperatureUnit.Kelvin => "K",
            _ => "°C"
        };
        return Math.Round(value).ToString("0", culture) + suffix;
    }

    private static readonly string[] BinaryUnits = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
    private static readonly string[] DecimalUnits = { "B", "kB", "MB", "GB", "TB", "PB" };

    public static string FormatSize(double bytes, DataUnit unit, CultureInfo? culture = null, int decimals = 1)
    {
        culture ??= CultureInfo.CurrentCulture;
        var divisor = unit == DataUnit.Decimal ? 1000d : 1024d;
        var suffixes = unit == DataUnit.Decimal ? DecimalUnits : BinaryUnits;

        var value = Math.Abs(bytes);
        var index = 0;
        while (value >= divisor && index < suffixes.Length - 1)
        {
            value /= divisor;
            index++;
        }

        var pattern = index == 0 ? "0" : "0." + new string('#', Math.Max(1, decimals));
        return value.ToString(pattern, culture) + " " + suffixes[index];
    }

    /// <summary>Speed with an explicit unit, used by the network tile.</summary>
    public static string FormatRate(double bytesPerSecond, DataUnit unit, CultureInfo? culture = null) =>
        FormatSize(bytesPerSecond, unit, culture) + "/s";
}
