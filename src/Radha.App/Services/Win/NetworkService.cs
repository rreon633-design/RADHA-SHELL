using Radha.Core.Services;
using Windows.Devices.Radios;
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

    public BluetoothService() { _ = InitializeAsync(); }

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

    public Task ConnectAsync(string deviceId) => Task.CompletedTask;
    public Task DisconnectAsync(string deviceId) => Task.CompletedTask;
}
