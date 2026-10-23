using System.Globalization;
using System.Text;
using Radha.Core.Security;

namespace Radha.Core.Diagnostics;

/// <summary>
/// A crash report that is useful without being a privacy problem.
///
/// The shell sets <c>Handled = true</c> on unhandled dispatcher exceptions — a panel that throws must
/// not take the session down — which also means the exception is otherwise invisible. This writes it
/// to the log directory in a fixed, redacted format the user can attach to a bug report.
///
/// The redaction is the same one the diagnostics bundle uses, so a token in an exception message
/// (a URL with an API key in it is the common case) does not end up in a file the user will paste
/// into a public issue.
/// </summary>
public static class CrashReport
{
    public const int MaxReportBytes = 64 * 1024;

    /// <summary>Formats a report. <paramref name="context"/> is the last known UI action, if any.</summary>
    public static string Format(Exception? exception, IReadOnlyDictionary<string, string?> environment, string? context = null)
    {
        var builder = new StringBuilder(2048);
        builder.AppendLine("RADHA SHELL — crash report");
        builder.AppendLine("==========================");
        builder.AppendLine();

        builder.Append("when      : ").AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(context))
            builder.Append("context   : ").AppendLine(context);

        foreach (var (key, value) in environment.OrderBy(p => p.Key, StringComparer.Ordinal))
            builder.Append(key.PadRight(10)).Append(": ").AppendLine(value ?? "(unknown)");

        builder.AppendLine();
        builder.AppendLine("exception");
        builder.AppendLine("---------");

        var depth = 0;
        for (var current = exception; current is not null && depth < 8; current = current.InnerException, depth++)
        {
            builder.Append(depth == 0 ? "" : "inner     : ")
                   .Append(current.GetType().FullName)
                   .Append(": ")
                   .AppendLine(current.Message);

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
                builder.AppendLine(current.StackTrace);
        }

        if (exception is null) builder.AppendLine("(no exception object was supplied)");

        var text = Redactor.Redact(builder.ToString()) ?? string.Empty;
        if (text.Length > MaxReportBytes) text = text[..MaxReportBytes] + "\n… truncated\n";
        return text;
    }

    /// <summary>
    /// A stable file name for this report, so repeated failures in one session overwrite rather than
    /// fill the disk. The key is the exception type, not the timestamp.
    /// </summary>
    public static string FileName(Exception? exception, DateTimeOffset now)
    {
        var type = exception?.GetType().Name ?? "Unknown";
        var safe = new string(type.Where(char.IsLetterOrDigit).Take(32).ToArray());
        if (safe.Length == 0) safe = "Unknown";
        return string.Create(CultureInfo.InvariantCulture, $"crash-{now:yyyyMMdd-HHmmss}-{safe}.log");
    }

    /// <summary>Writes the report and returns the path, or null when the directory is not writable.</summary>
    public static string? Write(string directory, Exception? exception, IReadOnlyDictionary<string, string?> environment, string? context = null)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName(exception, DateTimeOffset.Now));
            File.WriteAllText(path, Format(exception, environment, context));
            return path;
        }
        catch
        {
            return null;
        }
    }
}
