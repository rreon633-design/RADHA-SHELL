using System.Globalization;
using System.Text;
using System.Text.Json;
using Radha.Core.Config;
using Radha.Core.Ipc;
using Radha.Core.Launcher;
using Radha.Core.Modules;
using Radha.Core.Security;
using Radha.Core.Theming;

namespace Radha.Core.Diagnostics;

/// <summary>One line of the self-test.</summary>
public sealed record SelfTestCheck(string Name, bool Passed, bool Skipped, string Detail);

/// <summary>The result of a self-test run.</summary>
public sealed record SelfTestReport(DateTimeOffset At, IReadOnlyList<SelfTestCheck> Checks)
{
    public bool Passed => Checks.All(c => c.Passed || c.Skipped);

    public int Failures => Checks.Count(c => !c.Passed && !c.Skipped);

    public string ToText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("RADHA SHELL — self-test");
        builder.Append("when: ").AppendLine(At.ToString("u", CultureInfo.InvariantCulture));
        builder.AppendLine();

        foreach (var check in Checks)
        {
            var mark = check.Skipped ? "SKIP" : check.Passed ? " ok " : "FAIL";
            builder.Append('[').Append(mark).Append("] ").AppendLine(check.Name);
            if (!string.IsNullOrWhiteSpace(check.Detail))
                builder.Append("       ").AppendLine(check.Detail);
        }

        builder.AppendLine();
        builder.AppendLine(Passed
            ? $"PASS — {Checks.Count(c => !c.Skipped)} checks"
            : $"FAIL — {Failures} of {Checks.Count} checks");
        return builder.ToString();
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        passed = Passed,
        at = At,
        failures = Failures,
        checks = Checks.Select(c => new { name = c.Name, passed = c.Passed, skipped = c.Skipped, detail = c.Detail })
    }, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>
/// A headless, throwaway self-test of everything that does not need a desktop.
///
/// This is what makes "the release .exe works" a checkable claim instead of a hope: the published
/// binary is executed on a real Windows runner with <c>--selftest</c>, and the release is only
/// published if it exits zero. It never creates a window, so it behaves the same in a session-0 CI
/// runner as on a real desktop.
///
/// The checks are deliberately about the *shipped* binary, not about unit correctness — those are
/// covered by the test suite. What is proven here is that the single-file bundle contains a working
/// runtime, a loadable Windows App SDK, and a Core assembly whose basic contracts hold.
/// </summary>
public static class SelfTest
{
    /// <param name="runtimeProbe">
    /// Supplied by the host: returns null when the Windows App SDK loaded, or a message describing
    /// why it did not. Null delegate means "not applicable" and the check is skipped.
    /// </param>
    public static SelfTestReport Run(Func<string?>? runtimeProbe = null)
    {
        var checks = new List<SelfTestCheck>
        {
            Check("assembly loads", () =>
            {
                var version = typeof(SelfTest).Assembly.GetName().Version?.ToString() ?? "(none)";
                return ($"{typeof(SelfTest).Assembly.GetName().Name} {version}", true);
            }),

            Check("runtime", () =>
            {
                var runtime = Environment.Version.ToString();
                var os = OperatingSystem.IsWindows()
                    ? $"windows {Environment.OSVersion.Version}"
                    : Environment.OSVersion.VersionString;
                return ($"dotnet {runtime} · {os}", true);
            }),

            Check("command catalog", () =>
            {
                var modules = CommandCatalog.Modules.Count;
                var valid = CommandCatalog.IsValid(ShellModule.Launcher, "toggle");
                var invalid = !CommandCatalog.IsValid(ShellModule.Launcher, "definitely-not-a-command");
                return ($"{modules} modules · accept={valid} · reject={invalid}", modules > 0 && valid && invalid);
            }),

            Check("calculator", () =>
            {
                var ok = Calculator.TryEvaluate("2 + 2 * 3", out var value) && Math.Abs(value - 8) < 1e-9;
                return ($"2 + 2 * 3 = {value.ToString(CultureInfo.InvariantCulture)}", ok);
            }),

            Check("config store", () =>
            {
                var config = new ShellConfig().Normalized();
                var sane = config.Bar.Height is >= 28 and <= 64 && config.Appearance.Scale > 0;
                return ($"bar {config.Bar.Height}px · scale {config.Appearance.Scale.ToString(CultureInfo.InvariantCulture)}", sane);
            }),

            Check("theme engine", () =>
            {
                var scheme = MaterialScheme.FromSeed(new Rgb(124, 92, 255));
                var distinct = scheme.Primary != scheme.Surface;
                return ($"primary {scheme.Primary} on surface {scheme.Surface}", distinct);
            }),

            Check("launch guard", () =>
            {
                var blocked = !LaunchGuard.IsAllowed(@"..\..\evil.exe", null, out var reason);
                return (reason, blocked);
            }),

            Check("ipc endpoint", () =>
            {
                var endpoint = IpcEndpoints.Default();
                return (endpoint, !string.IsNullOrWhiteSpace(endpoint));
            })
        };

        if (runtimeProbe is not null)
        {
            string? detail = null;
            bool ok;
            try
            {
                detail = runtimeProbe();
                ok = detail is null;
            }
            catch (Exception ex)
            {
                detail = $"{ex.GetType().Name}: {ex.Message}";
                ok = false;
            }
            checks.Add(new SelfTestCheck("windows app sdk", ok, false, detail ?? "loaded"));
        }
        else
        {
            checks.Add(new SelfTestCheck("windows app sdk", true, true, "not applicable on this host"));
        }

        return new SelfTestReport(DateTimeOffset.Now, checks);
    }

    private static SelfTestCheck Check(string name, Func<(string Detail, bool Passed)> body)
    {
        try
        {
            var (detail, passed) = body();
            return new SelfTestCheck(name, passed, false, detail);
        }
        catch (Exception ex)
        {
            return new SelfTestCheck(name, false, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
