namespace Radha.Core.Services;

/// <summary>
/// Pure projection of the Windows RAS phonebook into <see cref="VpnProfile"/>.
///
/// The Win32 call (<c>RasEnumEntriesW</c>) happens in the app layer; everything that can be
/// decided without Windows lives here so it is unit-testable on any OS.
/// </summary>
public readonly record struct RasEntryInfo(string Name, string DeviceType, string PhoneNumber, uint Flags);

public static class RasPhonebook
{
    /// <summary>RASEO_Connected — the entry currently has an active connection.</summary>
    public const uint ConnectedFlag = 0x00000002;

    /// <summary>
    /// Projects raw entries into profiles: drops nameless and duplicate entries (case-insensitive,
    /// first wins), infers the tunnel kind from the device type, and orders connected → name.
    /// </summary>
    public static IReadOnlyList<VpnProfile> Project(IEnumerable<RasEntryInfo> entries, string? connectedName = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profiles = new List<VpnProfile>();

        foreach (var entry in entries)
        {
            var name = entry.Name?.Trim() ?? "";
            if (name.Length == 0 || !seen.Add(name)) continue;

            var connected = (entry.Flags & ConnectedFlag) != 0 ||
                            (connectedName is not null && string.Equals(name, connectedName, StringComparison.OrdinalIgnoreCase));

            profiles.Add(new VpnProfile(name, KindOf(entry.DeviceType, entry.PhoneNumber), entry.PhoneNumber ?? "", connected));
        }

        return profiles
            .OrderByDescending(p => p.Connected)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// RAS gives a device description such as "WAN Miniport (IKEv2)" or "RASDT_..."; fall back to the
    /// target when the tunnel type cannot be read from it.
    /// </summary>
    public static string KindOf(string? deviceType, string? target)
    {
        var device = deviceType ?? "";
        foreach (var kind in KnownKinds)
            if (device.Contains(kind, StringComparison.OrdinalIgnoreCase)) return kind;

        var host = target ?? "";
        if (host.StartsWith("vpn://", StringComparison.OrdinalIgnoreCase)) return "IKEv2";
        if (host.Contains(":1723", StringComparison.Ordinal)) return "PPTP";
        return "VPN";
    }

    private static readonly string[] KnownKinds = { "IKEv2", "SSTP", "L2TP", "PPTP", "WireGuard", "OpenVPN" };
}
