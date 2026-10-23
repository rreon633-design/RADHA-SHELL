using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Radha.Core.Diagnostics;

namespace Radha.App;

/// <summary>
/// Entry point.
///
/// The XAML toolchain normally generates <c>Main</c>; this file replaces it (see
/// <c>DISABLE_XAML_GENERATED_MAIN</c> in the project) for one reason: <c>--selftest</c> must run
/// before anything touches XAML, DWM or a dispatcher, so the shipped binary can prove it works on a
/// machine with no interactive desktop — which is exactly what a CI runner is.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (HasFlag(args, "--selftest"))
            return RunSelfTest(ArgumentAfter(args, "--out")
                               ?? Path.Combine(Path.GetTempPath(), "radha-selftest.json"));

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = callbackParams;
            _ = new App();
        });
        return 0;
    }

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
    /// Null when the Windows App SDK projection loaded; otherwise why it did not.
    ///
    /// Loading the assembly — not constructing a window — is the right depth here: it proves the
    /// single-file bundle carried <c>Microsoft.WinUI.dll</c> and its dependencies, and it is legal in
    /// a session with no compositor.
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
