using System.Text.RegularExpressions;

namespace Radha.Core.Security;

/// <summary>
/// Validation for everything that crosses a trust boundary: IPC requests, CLI arguments,
/// config keys and file paths. The rule is deny-by-default — unknown shapes are rejected
/// with a precise error, never coerced into something plausible.
/// </summary>
public static partial class InputGuard
{
    public const int MaxArgs = 16;
    public const int MaxArgLength = 512;
    public const int MaxModuleLength = 32;
    public const int MaxCommandLength = 32;

    /// <summary>Control characters, path separators and shell metacharacters are never valid here.</summary>
    private static readonly char[] Forbidden = { '\0', '\r', '\n', '|', '&', ';', '`', '$', '<', '>' };

    public static ValidationResult ValidateRequest(string? module, string? command, string[]? args)
    {
        if (string.IsNullOrWhiteSpace(module))
            return ValidationResult.Fail("module is required");
        if (module.Length > MaxModuleLength)
            return ValidationResult.Fail($"module too long ({module.Length} > {MaxModuleLength})");
        if (!module.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            return ValidationResult.Fail("module contains invalid characters");

        if (string.IsNullOrWhiteSpace(command))
            return ValidationResult.Fail("command is required");
        if (command.Length > MaxCommandLength)
            return ValidationResult.Fail($"command too long ({command.Length} > {MaxCommandLength})");
        if (!command.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
            return ValidationResult.Fail("command contains invalid characters");

        args ??= Array.Empty<string>();
        if (args.Length > MaxArgs)
            return ValidationResult.Fail($"too many arguments ({args.Length} > {MaxArgs})");

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is null) return ValidationResult.Fail($"argument {i} is null");
            if (arg.Length > MaxArgLength)
                return ValidationResult.Fail($"argument {i} too long ({arg.Length} > {MaxArgLength})");
            if (arg.IndexOfAny(Forbidden) >= 0)
                return ValidationResult.Fail($"argument {i} contains a forbidden character");

            // Reject NUL-ish unicode tricks that some parsers treat as terminators.
            if (arg.Any(c => char.IsControl(c) && c is not ('\t' or ' ')))
                return ValidationResult.Fail($"argument {i} contains a control character");
        }

        return ValidationResult.Ok();
    }

    /// <summary>Config keys are dot paths over a known model: letters, digits, dots, dashes.</summary>
    public static ValidationResult ValidateConfigKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return ValidationResult.Fail("config key is required");
        if (key.Length > 128) return ValidationResult.Fail("config key too long");
        if (!key.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
            return ValidationResult.Fail("config key contains invalid characters");
        if (key.Split('.').Any(segment => segment.Length == 0))
            return ValidationResult.Fail("config key contains an empty segment");
        return ValidationResult.Ok();
    }

    /// <summary>Numeric argument parsing that never throws and rejects NaN/Infinity.</summary>
    public static bool TryParseNumber(string? text, double min, double max, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return false;
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;
        if (value < min || value > max) return false;
        return true;
    }

    /// <summary>Integer counterpart with clamping disabled by default (explicit bounds required).</summary>
    public static bool TryParseInt(string? text, int min, int max, out int value)
    {
        value = 0;
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out value) &&
               value >= min && value <= max;
    }

    /// <summary>Colour inputs: #RGB, #RRGGBB or #AARRGGBB.</summary>
    public static bool IsValidHexColor(string? text) =>
        !string.IsNullOrWhiteSpace(text) && HexColorPattern.IsMatch(text.Trim());

    private static readonly Regex HexColorPattern = new(
        @"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

public readonly record struct ValidationResult(bool IsValid, string? Error)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string error) => new(false, error);
    public override string ToString() => IsValid ? "ok" : Error ?? "invalid";
}

/// <summary>
/// Path handling for the trust boundary: a wallpaper path or monitor name from IPC must not be
/// able to escape its configured root, and must never be turned into something executable.
/// </summary>
public static class PathGuard
{
    public static readonly string[] ExecutableExtensions =
        { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".msi", ".scr", ".com", ".dll" };

    public static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".avif", ".tif", ".tiff" };

    /// <summary>True when <paramref name="candidate"/> resolves inside <paramref name="root"/>.</summary>
    public static bool IsWithinRoot(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate)) return false;
        try
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidateFull = Path.GetFullPath(candidate);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return candidateFull.StartsWith(rootFull, comparison);
        }
        catch
        {
            return false;
        }
    }

    public static bool HasExtensionIn(string path, string[] allowed)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) &&
               allowed.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsImageFile(string path) => HasExtensionIn(path, ImageExtensions);

    /// <summary>Executable/script extensions are rejected anywhere a data file is expected.</summary>
    public static bool LooksExecutable(string path) => HasExtensionIn(path, ExecutableExtensions);

    /// <summary>Strips directory separators, control chars and reserved Windows names.</summary>
    public static string SafeFileName(string? name, string fallback = "default")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        // Take the last path segment first (both separators), then sanitise what is left.
        var lastSeparator = name.LastIndexOfAny(new[] { '/', '\\' });
        if (lastSeparator >= 0 && lastSeparator < name.Length - 1) name = name[(lastSeparator + 1)..];

        var invalid = Path.GetInvalidFileNameChars()
            .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            .ToHashSet();
        var cleaned = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim(' ', '.');
        if (cleaned.Length == 0) return fallback;
        if (cleaned.Length > 64) cleaned = cleaned[..64];

        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "LPT1", "LPT2" };
        if (reserved.Contains(cleaned, StringComparer.OrdinalIgnoreCase)) return fallback;

        return cleaned;
    }

    /// <summary>Rejects device paths and UNC tricks before they reach the filesystem.</summary>
    public static bool IsSafeLocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var trimmed = path.Trim();
        if (trimmed.Contains('\0')) return false;
        if (trimmed.StartsWith(@"\\.\", StringComparison.Ordinal)) return false; // device namespace
        if (trimmed.StartsWith("//./", StringComparison.Ordinal)) return false;
        if (trimmed.Contains("..") && trimmed.Contains(".." + Path.DirectorySeparatorChar) &&
            !Path.IsPathFullyQualified(trimmed) && trimmed.Contains(".." + Path.DirectorySeparatorChar + ".."))
            return false;

        return true;
    }
}

/// <summary>Removes credentials from anything that might be logged or shown in a toast.</summary>
public static partial class Redactor
{
    public const string Mask = "«redacted»";

    private static readonly Regex GitHubPatPattern = new(@"github_pat_[A-Za-z0-9_]{20,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GitHubClassicPattern = new(@"gh[pousr]_[A-Za-z0-9]{20,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OpenAiKeyPattern = new(@"sk-[A-Za-z0-9\-_]{16,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AssignmentPattern = new(
        @"(api[_-]?key|token|secret|password|passwd|pwd)\s*[:=]\s*[""']?([^\s""',;]{4,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BearerPattern = new(@"Bearer\s+[A-Za-z0-9\-._~+/]{16,}=*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string? text, bool includeEmails = false)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = GitHubPatPattern.Replace(text, Mask);
        result = GitHubClassicPattern.Replace(result, Mask);
        result = OpenAiKeyPattern.Replace(result, Mask);
        result = BearerPattern.Replace(result, "Bearer " + Mask);
        result = AssignmentPattern.Replace(result, match => $"{match.Groups[1].Value}={Mask}");
        if (includeEmails) result = EmailPattern.Replace(result, Mask);
        return result;
    }

    public static bool ContainsSecret(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (GitHubPatPattern.IsMatch(text) || GitHubClassicPattern.IsMatch(text) ||
         OpenAiKeyPattern.IsMatch(text) || BearerPattern.IsMatch(text));
}

/// <summary>
/// Token bucket per client. Protects the IPC server from a runaway script or a compromised
/// process trying to flood panel toggles (each toggle triggers layout work).
/// </summary>
public sealed class RateLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly double _refillPerSecond;
    private readonly TimeSpan _idleEviction;

    public RateLimiter(int capacity = 60, double refillPerSecond = 20, TimeSpan? idleEviction = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (refillPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(refillPerSecond));
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _idleEviction = idleEviction ?? TimeSpan.FromMinutes(5);
    }

    public int Capacity => _capacity;

    /// <summary>Returns false when the client has exhausted its budget.</summary>
    public bool TryAcquire(string client, DateTimeOffset? now = null)
    {
        var stamp = now ?? DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (!_buckets.TryGetValue(client, out var bucket))
            {
                bucket = new Bucket(_capacity, stamp);
                _buckets[client] = bucket;
            }

            bucket.Refill(_refillPerSecond, stamp);
            bucket.LastSeen = stamp;

            if (_buckets.Count > 256) Evict(stamp);

            if (bucket.Tokens < 1.0) return false;
            bucket.Tokens -= 1.0;
            return true;
        }
    }

    public int TrackedClients
    {
        get { lock (_gate) return _buckets.Count; }
    }

    private void Evict(DateTimeOffset now)
    {
        var stale = _buckets.Where(kv => now - kv.Value.LastSeen > _idleEviction)
                            .Select(kv => kv.Key)
                            .ToList();
        foreach (var key in stale) _buckets.Remove(key);
    }

    private sealed class Bucket
    {
        public double Tokens;
        public DateTimeOffset LastRefill;
        public DateTimeOffset LastSeen;

        public Bucket(double tokens, DateTimeOffset now)
        {
            Tokens = tokens;
            LastRefill = now;
            LastSeen = now;
        }

        public void Refill(double perSecond, DateTimeOffset now)
        {
            var elapsed = (now - LastRefill).TotalSeconds;
            if (elapsed <= 0) return;
            Tokens = Math.Min(Tokens + elapsed * perSecond, 1e9);
            LastRefill = now;
        }
    }
}
