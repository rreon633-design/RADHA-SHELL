using System.Diagnostics;
using Radha.Core.Net;

namespace Radha.App.Services.Win;

/// <summary>
/// Wi-Fi scan for an unpackaged desktop process.
///
/// `WiFiAdapter` needs the restricted <c>wiFiControl</c> capability, which only a packaged app
/// can declare. <c>netsh wlan show networks</c> is present on every Windows install and its output
/// is parsed by <see cref="WifiScanParser"/>, which is unit-tested off-Windows — so only the
/// process plumbing lives here.
/// </summary>
internal static class WifiScanner
{
    public const int TimeoutMs = 8000;

    public static async Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<WifiNetwork>();

        var output = await RunAsync("wlan show networks mode=bssid", ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output)) return Array.Empty<WifiNetwork>();

        if (WifiScanParser.NeedsLocationPermission(output)) return Array.Empty<WifiNetwork>();

        return WifiScanParser.Parse(output);
    }

    /// <summary>
    /// Joins a network Windows already has a profile for. Windows refuses to connect to a network
    /// it has never seen, so the honest contract is "connect using a saved profile" — the first
    /// join has to come from Settings, exactly as it does for every other Win32 application.
    /// </summary>
    public static async Task<bool> ConnectAsync(string profile, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(profile)) return false;

        // The profile name goes into a command line: refuse anything that could break out of it.
        if (profile.Contains('"') || profile.Contains('\\') || profile.Length > 64) return false;

        var output = await RunAsync($"wlan connect name=\"{profile}\"", ct).ConfigureAwait(false);
        return output.Contains("completed", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> RunAsync(string arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo("netsh", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // netsh reads the console code page; UTF-8 keeps SSIDs with accents intact.
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using var process = Process.Start(info);
        if (process is null) return string.Empty;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeoutMs);

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return output;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return string.Empty;
        }
    }
}
