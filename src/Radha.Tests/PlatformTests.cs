using Radha.Core.Services;

namespace Radha.Tests;

/// <summary>
/// Tests for the platform projections behind VPN, Bluetooth and toast mirroring. The Windows calls
/// themselves need a desktop session; everything decidable without one lives here.
/// </summary>
public static class PlatformTests
{
    public static void Run(Action<string, bool> check)
    {
        // ---------------- RAS phonebook → VPN profiles ----------------
        var raw = new[]
        {
            new RasEntryInfo("Work", "WAN Miniport (IKEv2)", "vpn.corp.example", 0),
            new RasEntryInfo("  Home  ", "WAN Miniport (SSTP)", "home.example:443", 0),
            new RasEntryInfo("work", "WAN Miniport (PPTP)", "dup.example", 0),   // duplicate, different case
            new RasEntryInfo("", "WAN Miniport (PPTP)", "nameless", 0),          // dropped
            new RasEntryInfo("Office", "WAN Miniport (L2TP)", "off.example", RasPhonebook.ConnectedFlag),
        };
        var profiles = RasPhonebook.Project(raw);

        check("vpn: nameless entries dropped", profiles.Count == 3);
        check("vpn: duplicates are case-insensitive", profiles.Count(p => p.Name.Equals("Work", StringComparison.OrdinalIgnoreCase)) == 1);
        check("vpn: names are trimmed", profiles.Any(p => p.Name == "Home"));
        check("vpn: connected profile sorts first", profiles[0].Name == "Office" && profiles[0].Connected);
        check("vpn: remaining sorted by name", profiles[1].Name == "Home" && profiles[2].Name == "Work");
        check("vpn: kind read from device description", profiles.Single(p => p.Name == "Office").Kind == "L2TP");
        check("vpn: kind inference falls back to target", RasPhonebook.KindOf("unknown device", ":1723") == "PPTP");
        check("vpn: kind inference default", RasPhonebook.KindOf(null, "plain.host") == "VPN");
        check("vpn: explicit connected name wins", RasPhonebook.Project(raw, "Work").Single(p => p.Name == "Work").Connected);
        check("vpn: empty input is safe", RasPhonebook.Project(Array.Empty<RasEntryInfo>()).Count == 0);

        // ---------------- mirrored toasts ----------------
        var toast = new MirroredToast("Microsoft.WindowsStore", "Store", "Update ready", "Click to install", DateTimeOffset.UnixEpoch);
        var shell = ToastMirror.ToShell(toast);

        check("toast: app name preserved", shell.AppName == "Store");
        check("toast: title preserved", shell.Title == "Update ready");
        check("toast: id is stable", ToastMirror.IdOf("a", "b", "c") == ToastMirror.IdOf("a", "b", "c"));
        check("toast: different content → different id", ToastMirror.IdOf("a", "b", "c") != ToastMirror.IdOf("a", "b", "d"));
        check("toast: re-projection keeps the same id", ToastMirror.ToShell(toast).Id == shell.Id);
        check("toast: normal urgency by default", shell.Urgency == NotificationUrgency.Normal);
        check("toast: alarm words raise urgency",
            ToastMirror.UrgencyOf("Backup", "backup FAILED on drive D") == NotificationUrgency.High);
        check("toast: empty title falls back to app name",
            ToastMirror.ToShell(toast with { Title = "   " }).Title == "Store");
        check("toast: missing app falls back to Windows",
            ToastMirror.ToShell(default).AppName == "Windows");
        check("toast: body is bounded",
            ToastMirror.ToShell(toast with { Body = new string('x', 900) }).Body.Length <= 241);
    }
}
