using System.Globalization;

namespace Radha.Core.Net;

public sealed record WifiNetwork(
    string Ssid,
    string Authentication,
    string Encryption,
    int SignalPercent,
    string Bssid,
    string Radio)
{
    public bool Secure => !Authentication.Equals("Open", StringComparison.OrdinalIgnoreCase)
                          && !Authentication.Equals("None", StringComparison.OrdinalIgnoreCase);

    /// <summary>Signal bars 0–4, the only number a UI should ever draw.</summary>
    public int Bars => SignalPercent >= 80 ? 4 : SignalPercent >= 60 ? 3 : SignalPercent >= 40 ? 2 : SignalPercent >= 20 ? 1 : 0;
}

/// <summary>
/// Parses <c>netsh wlan show networks mode=bssid</c>.
///
/// Windows exposes no unrestricted Wi-Fi *scan* API to an unpackaged process — `WiFiAdapter`
/// needs the restricted `wiFiControl` capability — but `netsh` is present on every Windows install
/// and its output is stable. Parsing it in Core keeps the fragile part testable on any OS.
/// </summary>
public static class WifiScanParser
{
    public static IReadOnlyList<WifiNetwork> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return Array.Empty<WifiNetwork>();

        // netsh repeats the SSID block once per BSSID; keep the strongest sighting of each SSID.
        var bySsid = new Dictionary<string, WifiNetwork>(StringComparer.OrdinalIgnoreCase);

        string? ssid = null;
        var auth = string.Empty;
        var encryption = string.Empty;
        var signal = 0;
        var bssid = string.Empty;
        var radio = string.Empty;
        var inBss = false;

        void Flush()
        {
            if (ssid is null) return;

            var network = new WifiNetwork(ssid, auth, encryption, signal, bssid, radio);
            if (!bySsid.TryGetValue(ssid, out var existing) || network.SignalPercent > existing.SignalPercent)
                bySsid[ssid] = network;
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var (key, value) = Split(line);
            if (key is null) continue;

            switch (key)
            {
                case "SSID":
                    // A new "SSID n :" row always starts a new network block.
                    Flush();
                    ssid = value;
                    auth = string.Empty;
                    encryption = string.Empty;
                    signal = 0;
                    bssid = string.Empty;
                    radio = string.Empty;
                    inBss = false;
                    break;
                case "Authentication":
                    if (!inBss) auth = value;
                    break;
                case "Encryption":
                    if (!inBss) encryption = value;
                    break;
                case "BSSID":
                    inBss = true;
                    if (bssid.Length == 0) bssid = value;
                    break;
                case "Signal":
                    if (value.EndsWith('%') &&
                        int.TryParse(value.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
                        signal = Math.Max(signal, Math.Clamp(pct, 0, 100));
                    break;
                case "Radio type":
                    if (radio.Length == 0) radio = value;
                    break;
            }
        }

        Flush();

        return bySsid.Values
            .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
            .OrderByDescending(n => n.SignalPercent)
            .ThenBy(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>netsh prints "    SSID name    : MyNet" — split on the first colon, trim both sides.</summary>
    private static (string? Key, string Value) Split(string line)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0) return (null, string.Empty);

        var key = line[..colon].Trim();
        var value = line[(colon + 1)..].Trim();

        // netsh indexes repeated rows: "SSID 1", "BSSID 2". The index is not part of the field.
        var end = key.Length;
        while (end > 0 && (char.IsDigit(key[end - 1]) || key[end - 1] == ' ')) end--;
        var baseKey = end > 0 ? key[..end] : key;

        return (baseKey.Length == 0 ? null : baseKey, value);
    }

    /// <summary>Removes the command echo and the "There are N networks" preamble.</summary>
    public static string Normalize(string output) => output.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>True when netsh reported a location-services block instead of a scan result.</summary>
    public static bool NeedsLocationPermission(string? output) =>
        output is not null &&
        output.Contains("location permission", StringComparison.OrdinalIgnoreCase);
}
