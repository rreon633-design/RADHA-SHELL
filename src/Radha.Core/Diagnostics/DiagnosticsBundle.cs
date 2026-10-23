using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Radha.Core.Diagnostics;

/// <summary>
/// Builds the zip a user attaches to a bug report.
///
/// Three rules: never include secrets (the redactor is applied to every text file), never include
/// anything outside the paths it was given, and always include enough metadata that the report is
/// actionable without a follow-up question.
/// </summary>
public sealed class DiagnosticsBundle
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    public const long MaxTotalBytes = 24 * 1024 * 1024;

    private readonly List<(string Name, byte[] Contents)> _files = new();
    private readonly Dictionary<string, string> _info = new(StringComparer.Ordinal);
    private long _total;

    public IReadOnlyCollection<string> Files => _files.Select(f => f.Name).ToList();

    public DiagnosticsBundle WithInfo(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) _info[key] = value!;
        return this;
    }

    public DiagnosticsBundle WithEnvironment(string? version = null, string? configPath = null, string? logDirectory = null)
    {
        WithInfo("generated", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        WithInfo("os", Environment.OSVersion.VersionString);
        WithInfo("osArchitecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
        WithInfo("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        WithInfo("processArchitecture", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
        WithInfo("culture", CultureInfo.CurrentCulture.Name);
        WithInfo("uiCulture", CultureInfo.CurrentUICulture.Name);
        WithInfo("cpuCount", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        WithInfo("workingSetMb", (Environment.WorkingSet / 1024 / 1024).ToString(CultureInfo.InvariantCulture));
        WithInfo("uptime", TimeSpan.FromMilliseconds(Environment.TickCount64).ToString());
        WithInfo("version", version ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString());
        WithInfo("configPath", configPath);
        WithInfo("logDirectory", logDirectory);
        return this;
    }

    /// <summary>Adds a text file, redacted. Oversized or unreadable files are skipped, not fatal.</summary>
    public DiagnosticsBundle WithTextFile(string name, string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return this;
            if (info.Length > MaxFileBytes) return this;

            var text = File.ReadAllText(path);
            return WithText(name, Radha.Core.Security.Redactor.Redact(text) ?? string.Empty);
        }
        catch (IOException) { return this; }
        catch (UnauthorizedAccessException) { return this; }
    }

    public DiagnosticsBundle WithText(string name, string contents)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        if (bytes.Length > MaxFileBytes) return this;
        if (_total + bytes.Length > MaxTotalBytes) return this;

        _files.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        _files.Add((SafeName(name), bytes));
        _total += bytes.Length;
        return this;
    }

    /// <summary>Reserves the report file name so a caller cannot overwrite <c>report.txt</c>.</summary>
    public DiagnosticsBundle WithReport(string report)
    {
        _files.RemoveAll(f => f.Name == "report.txt");
        return WithText("report.txt", report);
    }

    public string Build(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var info = new StringBuilder("RADHA SHELL diagnostics\n");
        info.Append(new string('=', 40)).Append('\n');
        foreach (var (key, value) in _info.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            info.Append(key.PadRight(20)).Append(" : ").AppendLine(value);

        var entry = archive.CreateEntry("environment.txt", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            writer.Write(info.ToString());

        foreach (var (name, contents) in _files)
        {
            var file = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var entryStream = file.Open();
            entryStream.Write(contents, 0, contents.Length);
        }

        return path;
    }

    private static string SafeName(string name)
    {
        var cleaned = name.Replace('\\', '/');
        var parts = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries)
                           .Where(p => p != ".." && p != ".")
                           .ToArray();
        var joined = string.Join('/', parts);
        return joined.Length == 0 ? "file.txt" : joined;
    }
}
