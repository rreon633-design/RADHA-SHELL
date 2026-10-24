using System.Diagnostics;
using System.Text.Json;
using Radha.App.Services.Win;

namespace Radha.App.Interop;

/// <summary>
/// Asks the native bridge whether virtual desktops work — in a process that is allowed to die.
///
/// <c>RadhaNative.dll</c> reaches Windows' undocumented <c>IVirtualDesktopManagerInternal</c>. If the
/// vtable layout for the running build is wrong, the call lands on an arbitrary address: on .NET,
/// an access violation from native code is not catchable, so the shell itself would vanish mid-boot
/// with no window and no message — which is exactly the symptom this exists to prevent.
///
/// So the question is asked of a child process (<c>--probe-desktops</c>). The child reports what it
/// found and exits; if it dies instead, the parent knows the bridge is not safe on this build and
/// keeps the labelled preview. The probe also performs a no-op switch to the current desktop, so the
/// slot the shell will later use is proven on the same run.
/// </summary>
internal static class WorkspaceProbe
{
    internal readonly record struct Result(bool Live, int Build, int Count, int Current, string? Error)
    {
        internal static Result Unsupported(string reason) => new(false, 0, 0, -1, reason);
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly Lazy<Result> Cached = new(Run, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Runs once per process; a shell restart is a fresh process anyway.</summary>
    internal static Result Get() => Cached.Value;

    private static Result Run()
    {
        if (Environment.ProcessPath is not { Length: > 0 } exe)
            return Result.Unsupported("the executable path is unknown");

        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, "--probe-desktops")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return Result.Unsupported("the probe process could not be started");

            var stdout = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return Result.Unsupported("the probe did not answer in time");
            }

            var exit = process.ExitCode;
            if (exit != 0)
                return Result.Unsupported(
                    $"the probe crashed on this Windows build (exit 0x{exit:X8}); the shell is not touching it");

            var text = stdout.GetAwaiter().GetResult().Trim();
            if (text.Length == 0) return Result.Unsupported("the probe returned nothing");

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var backend = root.GetProperty("backend").GetInt32();
            var build = root.GetProperty("build").GetInt32();
            var count = root.GetProperty("count").GetInt32();
            var current = root.GetProperty("current").GetInt32();
            var switchOk = root.TryGetProperty("switch", out var s) && s.GetBoolean();

            if (backend != 1 || count <= 0 || current < 0 || !switchOk)
                return Result.Unsupported($"no verified desktop layout for build {build}");

            return new Result(true, build, count, current, null);
        }
        catch (Exception ex)
        {
            return Result.Unsupported($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The child side. Speaks one JSON line on stdout and returns; it never touches XAML, so it is
    /// legal in any session.
    /// </summary>
    internal static int RunChild()
    {
        int build = 0, backend = 0, count = 0, current = -1;
        var switchOk = false;

        try
        {
            build = NativeBridge.RadhaWorkspaceBuild();
            backend = NativeBridge.RadhaWorkspaceBackend();
            if (backend == 1)
            {
                count = NativeBridge.RadhaWorkspaceCount();
                current = NativeBridge.RadhaWorkspaceCurrent();

                // A switch to the desktop that is already current changes nothing visible, and it
                // proves the call the bar will make later is not a jump into the wrong slot.
                if (count > 0 && current >= 0)
                    switchOk = NativeBridge.RadhaWorkspaceSwitch(current);
            }
        }
        catch (Exception ex)
        {
            Console.Out.WriteLine($"{{\"error\":{JsonSerializer.Serialize(ex.Message)}}}");
            return 1;
        }

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            build, backend, count, current, @switch = switchOk
        }));
        return 0;
    }
}
