using System.Runtime.InteropServices;
using Radha.Core.Services;

namespace Radha.App.Services.Win;

/// <summary>
/// VPN profiles and connections through the documented RAS API (rasapi32), not the WinRT
/// <c>Windows.Networking.Vpn</c> namespace.
///
/// That choice is deliberate: <c>Windows.Networking.Vpn</c> requires the restricted
/// <c>networkingVpnProvider</c> capability, which an unpackaged app cannot request. RAS is available
/// to any desktop process, so a shell user gets their real Windows VPN profiles with no packaging.
///
/// The connection name/target projection is unit-tested on Linux via <see cref="RasPhonebook"/>;
/// this class only performs the Win32 calls and feeds the results in.
/// </summary>
public sealed class VpnService : IVpnService, IDisposable
{
    private const int MaxEntryName = 256;
    private const int MaxPath = 260;

    private readonly List<VpnProfile> _profiles = new();
    private readonly System.Threading.Timer _poll;
    private readonly object _gate = new();
    private string? _connected;
    private IntPtr _handle = IntPtr.Zero;

    public IReadOnlyList<VpnProfile> Profiles
    {
        get { lock (_gate) return _profiles.ToArray(); }
    }

    public string? ConnectedName
    {
        get { lock (_gate) return _connected; }
    }

    public event EventHandler? Changed;

    public VpnService()
    {
        _ = RefreshAsync();
        _poll = new System.Threading.Timer(_ => _ = RefreshAsync(), null, 4000, 4000);
    }

    public Task RefreshAsync()
    {
        var raw = new List<RasEntryInfo>();
        var connectedName = ReadActiveConnection(out var handle);

        foreach (var entry in EnumerateEntries())
            raw.Add(new RasEntryInfo(entry.Name, "", entry.Target, entry.Flags));

        var projected = RasPhonebook.Project(raw, connectedName);

        var changed = projected.Count != _profiles.Count ||
                      !projected.Select(p => (p.Name, p.Connected))
                                .SequenceEqual(_profiles.Select(p => (p.Name, p.Connected)));

        lock (_gate)
        {
            _profiles.Clear();
            _profiles.AddRange(projected);
            _connected = connectedName;
            if (handle != IntPtr.Zero) _handle = handle;
        }

        if (changed) Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        return Task.CompletedTask;
    }

    public Task<bool> ConnectAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Task.FromResult(false);

        var parameters = new RASDIALPARAMS
        {
            dwSize = Marshal.SizeOf<RASDIALPARAMS>(),
            szEntryName = name,
            szPhoneNumber = "",
            szCallbackNumber = "",
            szUserName = "",
            szPassword = "",
            szDomain = "",
        };

        // Default credentials: Windows then uses the saved logon for the entry. Profiles that need a
        // separate password are dialled by the user through the Settings app; we do not prompt.
        var result = RasDial(IntPtr.Zero, null, ref parameters, 0, IntPtr.Zero, IntPtr.Zero);
        if (result != 0) return Task.FromResult(false);

        _ = RefreshAsync();
        return Task.FromResult(true);
    }

    public Task<bool> DisconnectAsync()
    {
        var handle = _handle;
        if (handle == IntPtr.Zero) return Task.FromResult(false);

        if (RasHangUp(handle) != 0) return Task.FromResult(false);
        _handle = IntPtr.Zero;
        _ = RefreshAsync();
        return Task.FromResult(true);
    }

    public void Dispose() => _poll.Dispose();

    // ---- Win32 ---------------------------------------------------------------------------------

    private readonly record struct EntryName(string Name, string Target, uint Flags);

    private static List<EntryName> EnumerateEntries()
    {
        const int stride = 4 + (MaxEntryName + 1) * 2 + (MaxPath + 1) * 2 + 4; // szEntryName, szPhonebookPath, dwFlags
        var size = stride;
        var entries = new List<EntryName>();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                // First DWORD of the array is dwSize for the first element; RAS expects it pre-filled.
                var count = size / stride;
                for (var i = 0; i < count; i++)
                    Marshal.WriteInt32(buffer, i * stride, stride);

                var bytes = (uint)size;
                var written = 0;
                var rc = RasEnumEntries(null, null, buffer, ref bytes, ref written);
                if (rc == 603 /* ERROR_BUFFER_TOO_SMALL */)
                {
                    size = (int)bytes;
                    continue;
                }
                if (rc != 0) return entries;

                for (var i = 0; i < written; i++)
                {
                    var offset = i * stride;
                    var name = Marshal.PtrToStringUni(buffer, offset + 4) ?? "";
                    var target = Marshal.PtrToStringUni(buffer, offset + 4 + (MaxEntryName + 1) * 2) ?? "";
                    var flags = (uint)Marshal.ReadInt32(buffer, offset + 4 + (MaxEntryName + 1) * 2 + (MaxPath + 1) * 2);
                    entries.Add(new EntryName(name, target, flags));
                }
                return entries;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return entries;
    }

    private static string? ReadActiveConnection(out IntPtr handle)
    {
        handle = IntPtr.Zero;
        var stride = 4 + IntPtr.Size + (MaxEntryName + 1) * 2 + 18 * 2 + 129 * 2 + (MaxPath + 1) * 2 + 4;

        var buffer = Marshal.AllocHGlobal(stride);
        try
        {
            Marshal.WriteInt32(buffer, 0, stride);
            var bytes = (uint)stride;
            var written = 0;
            if (RasEnumConnections(buffer, ref bytes, ref written) != 0 || written == 0) return null;

            handle = Marshal.ReadIntPtr(buffer, 4);
            var name = Marshal.PtrToStringUni(buffer, 4 + IntPtr.Size) ?? "";
            return name.Length == 0 ? null : name;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const string RasDll = "rasapi32.dll";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RASDIALPARAMS
    {
        public int dwSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxEntryName + 1)] public string szEntryName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szPhoneNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szCallbackNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxEntryName + 1)] public string szUserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxEntryName + 1)] public string szPassword;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string szDomain;
    }

    [DllImport(RasDll, CharSet = CharSet.Unicode)]
    private static extern int RasEnumEntries(string? reserved, string? phonebook, IntPtr entries, ref uint bytes, ref int count);

    [DllImport(RasDll, CharSet = CharSet.Unicode)]
    private static extern int RasEnumConnections(IntPtr connections, ref uint bytes, ref int count);

    [DllImport(RasDll, CharSet = CharSet.Unicode)]
    private static extern int RasDial(IntPtr extensions, string? phonebook, ref RASDIALPARAMS parameters,
                                      int notifierType, IntPtr notifier, IntPtr result);

    [DllImport(RasDll, CharSet = CharSet.Unicode)]
    private static extern int RasHangUp(IntPtr connection);
}
