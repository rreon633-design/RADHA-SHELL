using Radha.Core.Services;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Networking;
using WinBt = Windows.Devices.Bluetooth.BluetoothDevice;
using Windows.Networking.Connectivity;

namespace Radha.App.Services.Win;

/// <summary>NetworkListManager equivalent using Windows.Networking.Connectivity + Radio APIs.</summary>
public sealed class NetworkService : INetworkService
{
    public NetworkState Current { get; private set; } =
        new(false, "Disconnected", null, 0, false, null, "");

    public event EventHandler<NetworkState>? Changed;

    public NetworkService()
    {
        try
        {
            NetworkInformation.NetworkStatusChanged += _ => Refresh();
            Refresh();
        }
        catch { }
    }

    private void Refresh()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                Current = new NetworkState(false, "Disconnected", null, 0, false, null, "");
            }
            else
            {
                var connectivity = profile.GetNetworkConnectivityLevel();
                var connected = connectivity is NetworkConnectivityLevel.InternetAccess or NetworkConnectivityLevel.ConstrainedInternetAccess;
                var signal = profile.IsWlanConnectionProfile ? profile.GetSignalBars() ?? 0 : 4;
                Current = new NetworkState(
                    connected,
                    profile.IsWlanConnectionProfile ? "Wi-Fi" : profile.IsWwanConnectionProfile ? "Mobile" : "Ethernet",
                    profile.IsWlanConnectionProfile ? profile.ProfileName : null,
                    signal,
                    profile.GetConnectionCost()?.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable,
                    null,
                    "");
            }
        }
        catch { }

        Ui.Post(() => Changed?.Invoke(this, Current));
    }

    public async Task<bool> ToggleWifiAsync()
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return false;

            foreach (var radio in await Radio.GetRadiosAsync())
            {
                if (radio.Kind != RadioKind.WiFi) continue;
                var target = radio.State == RadioState.On ? RadioState.Off : RadioState.On;
                var result = await radio.SetStateAsync(target);
                return result == RadioAccessStatus.Allowed;
            }
        }
        catch { }
        return false;
    }

    public Task<bool> ToggleVpnAsync()
    {
        // VPN connect/disconnect requires a stored profile (RAS/VpnManagement API).
        // Kept as an explicit phase-2 item rather than a silent no-op.
        return Task.FromResult(false);
    }
}

/// <summary>Bluetooth radio state via Windows.Devices.Radios.</summary>
public sealed class BluetoothService : IBluetoothService
{
    private Radio? _radio;
    private readonly List<BluetoothDevice> _devices = new();

    public bool Enabled => _radio?.State == RadioState.On;
    public IReadOnlyList<BluetoothDevice> Devices => _devices;
    public event EventHandler? Changed;

    /// <summary>Paired devices, refreshed whenever the radio or the paired list changes.</summary>
    private readonly Dictionary<string, WinBt> _linked = new(StringComparer.OrdinalIgnoreCase);

    public BluetoothService()
    {
        _ = InitializeAsync();
    }

    /// <summary>
    /// Re-reads the paired-device list. <c>GetDeviceSelectorFromPairingState(true)</c> is the
    /// documented selector for paired devices and needs no capability in a desktop process.
    /// </summary>
    public async Task RefreshDevicesAsync()
    {
        try
        {
            var infos = await DeviceInformation.FindAllAsync(WinBt.GetDeviceSelectorFromPairingState(true));
            var found = new List<BluetoothDevice>();

            foreach (var info in infos)
            {
                try
                {
                    var device = await WinBt.FromIdAsync(info.Id);
                    if (device is null) continue;

                    found.Add(new BluetoothDevice(
                        Id: device.DeviceId,
                        Name: string.IsNullOrWhiteSpace(device.Name) ? info.Name : device.Name,
                        Connected: device.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected,
                        BatteryPercent: null,
                        IconHint: device.BluetoothAddress != 0 ? "bluetooth" : null));

                    _linked[device.DeviceId] = device;
                }
                catch { /* device vanished between enumeration and open */ }
            }

            var changed = !found.Select(d => (d.Id, d.Connected, d.Name))
                                .SequenceEqual(_devices.Select(d => (d.Id, d.Connected, d.Name)));
            _devices.Clear();
            _devices.AddRange(found);

            if (changed) Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        }
        catch { /* no radio / no permission: the list stays empty, the toggle still works */ }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return;
            var radios = await Radio.GetRadiosAsync();
            _radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (_radio is not null)
            {
                _radio.StateChanged += (_, _) => Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
                Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
            }
        }
        catch { }
    }

    public async Task<bool> ToggleAsync()
    {
        if (_radio is null) return false;
        var target = _radio.State == RadioState.On ? RadioState.Off : RadioState.On;
        return await _radio.SetStateAsync(target) == RadioAccessStatus.Allowed;
    }

    public async Task ConnectAsync(string deviceId)
    {
        // Opening the device object is what makes Windows establish the link; for classic devices the
        // OS owns the connection and this simply refreshes the state.
        try
        {
            if (!_linked.TryGetValue(deviceId, out var device))
            {
                device = await WinBt.FromIdAsync(deviceId);
                if (device is null) return;
                _linked[deviceId] = device;
            }
            _ = device.ConnectionStatus;
        }
        catch { }
        await RefreshDevicesAsync();
    }

    public async Task DisconnectAsync(string deviceId)
    {
        // Windows has no "disconnect this paired device" API; disposing the cached object drops the
        // link we hold, and the radio toggle remains the way to sever everything.
        if (_linked.Remove(deviceId, out var device)) device.Dispose();
        await RefreshDevicesAsync();
    }

    /// <summary>
    /// Devices the machine can see but has not paired with. Discovery is a query, not a background
    /// scan: it only asks the enumeration service what it already knows, which is why it returns in
    /// milliseconds and needs no capability.
    /// </summary>
    public static async Task<IReadOnlyList<(string Id, string Name)>> ScanUnpairedAsync()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<(string, string)>();

        try
        {
            var selector = WinBt.GetDeviceSelector();
            var found = await DeviceInformation.FindAllAsync(selector);

            return found
                .Where(d => d.Pairing is { IsPaired: false })
                .Where(d => !string.IsNullOrWhiteSpace(d.Name))
                .Select(d => (d.Id, d.Name))
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<(string, string)>();
        }
    }

    /// <summary>Prompts the user (Windows shows the pairing UI) for an unpaired device id.</summary>
    public static async Task<bool> PairAsync(string deviceId)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(deviceId)) return false;

        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(deviceId);
            if (device is null) return false;

            var result = await device.Pairing.PairAsync();
            return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
