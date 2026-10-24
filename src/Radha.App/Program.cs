using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Radha.Core.Diagnostics;

namespace Radha.App;

/// <summary>
/// Entry point.
///
/// The XAML toolchain normally generates <c>Main</c>; this file replaces it (see
/// <c>DISABLE_XAML_GENERATED_MAIN</c> in the project) so three things can happen before any XAML
/// exists: the Windows App SDK search path is repaired for single-file bundles, <c>--selftest</c>
/// can run on a machine with no interactive desktop, and a fatal startup error becomes a message a
/// human can read instead of a process that vanishes.
/// </summary>
public static class Program
{
    private const string RuntimeDll = "Microsoft.WindowsAppRuntime.dll";

    /// <summary>Non-null while running <c>--smoke</c>: a real window, then a verdict.</summary>
    internal static SmokeProbe? Smoke { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        // First thing that happens, before anything can fail: leave a trace. A WinExe that dies
        // before its first frame otherwise leaves nothing at all behind on the user's machine.
        StartupLog.Write($"start args='{string.Join(' ', args)}' exe='{Environment.ProcessPath}'");

        // In a single-file bundle the Windows App SDK DLLs are extracted to %TEMP%\.net\... and the
        // executable stays wherever the user put it. Pointing activation at the executable's own
        // folder would send it hunting for Microsoft.WindowsAppRuntime.dll in a directory that has
        // none, which fails silently because there is no console to print to.
        if (ResolveRuntimeDirectory() is { } runtimeDirectory)
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", runtimeDirectory);

        if (HasFlag(args, "--selftest"))
            return RunSelfTest(ArgumentAfter(args, "--out")
                               ?? Path.Combine(Path.GetTempPath(), "radha-selftest.json"));

        // Child-process probe for the native virtual-desktop bridge. Runs before any XAML, prints
        // one JSON line and exits; the parent reads it and decides whether to touch the bridge.
        if (HasFlag(args, "--probe-desktops"))
            return Interop.WorkspaceProbe.RunChild();

        if (HasFlag(args, "--diagnose"))
            return RunDiagnose(ArgumentAfter(args, "--out")
                               ?? Path.Combine(Path.GetTempPath(), "radha-diagnose.txt"));

        if (HasFlag(args, "--smoke"))
        {
            var outPath = ArgumentAfter(args, "--out") ?? Path.Combine(Path.GetTempPath(), "radha-smoke.json");
            var timeout = int.TryParse(ArgumentAfter(args, "--timeout"), out var seconds) ? seconds : 90;
            Smoke = new SmokeProbe(outPath, TimeSpan.FromSeconds(timeout));
        }

        Smoke?.Stage("com-wrappers");
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Smoke?.Stage("application-start");
            Application.Start(callbackParams =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = callbackParams;

                try
                {
                    _ = new App();
                }
                catch (Exception ex)
                {
                    // No App object means no window and no message loop worth running.
                    Fatal("XAML could not load", ex);
                    Environment.Exit(1);
                }
            });

            Smoke?.Fail("exit", new InvalidOperationException("The message loop ended before the shell reported success."));
            return 0;
        }
        catch (Exception ex)
        {
            Fatal("the shell could not start", ex);
            return 1;
        }
    }

    /// <summary>
    /// The directory that actually holds <c>Microsoft.WindowsAppRuntime.dll</c>.
    ///
    /// <see cref="AppContext.BaseDirectory"/> is the directory of the executable, which is the wrong
    /// answer for a single-file bundle: the native runtime lives in the extraction directory. The
    /// runtime already knows where that is, and it publishes the list it searches as
    /// <c>NATIVE_DLL_SEARCH_DIRECTORIES</c>, so the check is "which of these actually has the DLL"
    /// rather than a guess. Returns null when none does, which leaves the SDK's default in place.
    /// </summary>
    private static string? ResolveRuntimeDirectory()
    {
        var candidates = new List<string>();

        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string searchDirectories)
        {
            foreach (var directory in searchDirectories.Split(
                         Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                candidates.Add(directory);
        }

        candidates.Add(AppContext.BaseDirectory);

        if (Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } executableDirectory)
            candidates.Add(executableDirectory);

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(candidate, RuntimeDll))) return candidate;
            }
            catch
            {
                // A malformed path in the runtime's own list is not a reason to stop looking.
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the failure down and, when there is a desktop, says it out loud. Silence is the one
    /// outcome a user cannot act on.
    /// </summary>
    internal static void Fatal(string what, Exception exception)
    {
        var path = StartupLog.Write($"fatal {what}: {exception}");
        Smoke?.Fail("startup", exception);

        try
        {
            MessageBox(IntPtr.Zero,
                $"RADHA SHELL could not start.\n\n{what}\n\n{exception.GetType().Name}: {exception.Message}\n\n" +
                $"Details were written to:\n{path}",
                "RADHA SHELL", 0x00000010 /* MB_ICONERROR */);
        }
        catch
        {
            // No interactive desktop, or user32 is unreachable. The log still has the answer.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>
    /// Runs the headless checks and writes both a JSON and a human-readable report.
    /// Exit code 0 means everything that can be checked without a desktop is correct.
    /// </summary>
    private static int RunSelfTest(string outputPath)
    {
        var report = SelfTest.Run(ProbeWindowsAppSdk);

        try
        {
            var full = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, report.ToJson());
            File.WriteAllText(full + ".txt", report.ToText());
        }
        catch
        {
            // A read-only temp directory must not turn a passing run into a failing one.
        }

        return report.Passed ? 0 : 1;
    }

    /// <summary>
    /// Everything needed to explain a launch that produced no window: where the process thinks it
    /// is, where the runtime is, whether the resource index is present and correctly named, and
    /// which system dependencies WinUI's native half needs are missing.
    /// </summary>
    private static int RunDiagnose(string outputPath)
    {
        var report = new StringBuilder();
        void Line(string name, object? value) => report.AppendLine($"{name,-34}{value}");

        Line("RadhaShell version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "?");
        Line("process", Environment.ProcessPath);
        Line("process architecture", RuntimeInformation.ProcessArchitecture);
        Line("OS", Environment.OSVersion.VersionString);
        Line("64-bit OS", Environment.Is64BitOperatingSystem);
        Line(".NET runtime", RuntimeInformation.FrameworkDescription);
        Line("base directory", AppContext.BaseDirectory);
        Line("worker path (bundle cache)", Path.GetDirectoryName(Environment.ProcessPath));
        Line("TEMP", Path.GetTempPath());
        Line("native search directories", AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") ?? "(none)");
        Line("resolved runtime directory", ResolveRuntimeDirectory() ?? "(not found)");
        Line("Microsoft.WindowsAppRuntime.dll", RuntimeDllPresent() ? "present" : "MISSING");

        // The named file is what an unpackaged WinUI app loads App.xaml through. A .pri with any
        // other name is inert, which is the failure this diagnostic exists to catch.
        foreach (var directory in new[] { AppContext.BaseDirectory, ResolveRuntimeDirectory() })
        {
            if (string.IsNullOrEmpty(directory)) continue;
            var pri = Path.Combine(directory, "resources.pri");
            if (File.Exists(pri)) Line($"resources.pri in {directory}", $"{new FileInfo(pri).Length / 1024} KB");
        }

        var system32 = Path.Combine(Environment.SystemDirectory);
        foreach (var dll in new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcruntime140d.dll" })
        {
            var alternative = Path.Combine(system32, dll);
            Line($"VC++ runtime {dll}", File.Exists(alternative) ? "present" : "missing");
        }

        report.AppendLine();
        report.AppendLine($"config folder   {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Radha")}");
        report.AppendLine($"uptime          {TimeSpan.FromMilliseconds(Environment.TickCount64)}");

        try
        {
            var full = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, report.ToString());
            Console.Error.WriteLine(report.ToString());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(report.ToString());
            Console.Error.WriteLine($"could not write {outputPath}: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static bool RuntimeDllPresent() =>
        ResolveRuntimeDirectory() is { } directory && File.Exists(Path.Combine(directory, RuntimeDll));

    /// <summary>
    /// Null when the Windows App SDK projection loaded; otherwise why it did not.
    ///
    /// Loading the assembly — not constructing a window — is the right depth here: it proves the
    /// single-file bundle carried <c>Microsoft.WinUI.dll</c> and its dependencies, and it is legal in
    /// a session with no compositor. <c>--smoke</c> is the check that goes further.
    /// </summary>
    private static string? ProbeWindowsAppSdk()
    {
        try
        {
            var name = typeof(Application).Assembly.GetName().Name;
            return string.IsNullOrEmpty(name) ? "Windows App SDK assembly has no name" : null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? ArgumentAfter(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}

/// <summary>
/// One line per launch, appended to <c>%APPDATA%\Radha\logs\startup.log</c> and capped so it never
/// grows into a problem of its own. This is the file to ask a user for when "it does nothing".
/// </summary>
internal static class StartupLog
{
    private const long MaxBytes = 256 * 1024;

    internal static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Radha", "logs", "startup.log");

    internal static string Write(string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}";
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(directory);

            var info = new FileInfo(Path);
            if (info.Exists && info.Length > MaxBytes) info.Delete();

            File.AppendAllText(Path, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never be the reason a launch fails.
        }

        return Path;
    }
}

/// <summary>
/// Proves the thing <c>--selftest</c> cannot: that the packaged XAML, the resource index and the
/// WinUI compositor all come up and a window exists.
///
/// The verdict is a file, and only a file. An earlier version exited the process the moment it knew
/// the answer, which meant every release gate was really testing WinUI's shutdown path — and it
/// reported heap corruption and an access violation on runs where the window had opened fine. A
/// desktop shell is supposed to stay running anyway; the harness on the other side reads the file
/// and stops the process.
/// </summary>
internal sealed class SmokeProbe
{
    private readonly string _path;
    private readonly string _timeoutText;
    private readonly Timer _deadline;
    private int _completed;

    internal SmokeProbe(string path, TimeSpan timeout)
    {
        _path = path;
        _timeoutText = $"{timeout.TotalSeconds:0}s";
        Stage("starting");
        _deadline = new Timer(_ => Fail("timeout", new TimeoutException(
            $"No window after {_timeoutText}. The shell never finished starting.")),
            null, timeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Rewrites the report as startup advances, so a process that dies without a message still says
    /// where it got to. Writing is deliberately dumb: open, truncate, close.
    /// </summary>
    internal void Stage(string stage)
    {
        if (Volatile.Read(ref _completed) != 0) return;
        Write("running", stage, null);
    }

    /// <summary>Called once the shell is up: a window exists and its XAML tree is built.</summary>
    internal void Pass(string detail)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        Write("pass", detail, null);
    }

    internal void Fail(string phase, Exception exception)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        Write("fail", phase, exception);
    }

    private void Write(string result, string detail, Exception? exception)
    {
        try
        {
            var json = new StringBuilder()
                .Append("{\n")
                .Append("  \"result\": ").Append(Json(result)).Append(",\n")
                .Append("  \"stage\": ").Append(Json(detail)).Append(",\n")
                .Append("  \"runtime\": ").Append(Json(RuntimeInformation.FrameworkDescription)).Append(",\n")
                .Append("  \"baseDirectory\": ").Append(Json(AppContext.BaseDirectory)).Append(",\n")
                .Append("  \"error\": ").Append(Json(exception?.ToString() ?? "")).Append('\n')
                .Append("}\n")
                .ToString();

            var full = System.IO.Path.GetFullPath(_path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, json);
        }
        catch
        {
            // The startup log below carries the same information.
        }

        StartupLog.Write($"smoke {result} ({detail})");
    }

    private static string Json(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";
}
