using System.Diagnostics;

namespace Radha.App.Interop;

/// <summary>
/// Runs this executable again with a probe switch and reads one line of JSON back.
///
/// Some of what a Windows shell has to ask can only be answered through undocumented COM or a
/// vendor DLL, and a mistake there is a native access violation: not catchable on .NET, and the
/// process simply ends. Asking in a child that is allowed to die converts that into "this feature is
/// unavailable here" instead of a shell that never appears.
/// </summary>
internal static class ChildProbe
{
    internal static string? Run(string argument, TimeSpan timeout)
    {
        if (Environment.ProcessPath is not { Length: > 0 } executable) return null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, argument)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return null;

            // Read before waiting: a child that fills the pipe buffer would otherwise block forever.
            var stdout = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            if (process.ExitCode != 0) return null;

            var text = stdout.GetAwaiter().GetResult().Trim();
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
