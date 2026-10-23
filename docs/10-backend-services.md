# 10. Backend Services

All services implement interfaces from `src/Radha.Core/Services/Contracts.cs`, are created in
`ShellHost`'s composition root, and never touch the UI thread directly — callbacks are marshalled
through `Services/Win/Ui.cs`.

| Service | Interface | Windows API | Failure mode | Verified |
|---|---|---|---|---|
| Media | `IMediaService` | `GlobalSystemMediaTransportControlsSessionManager` (SMTC) | no session → `MediaTrack.None`, pill hides | 🟡 build |
| Audio | `IAudioService` | WASAPI loopback (NAudio) + endpoint volume | no endpoint → flat spectrum, volume 50 % default | 🟡 build |
| Sensors | `ISystemMonitorService` | LibreHardwareMonitor + `GlobalMemoryStatusEx` + `DriveInfo` + `NetworkInterface` + `Battery` | driver gone → last sample retained | 🟡 build |
| Network | `INetworkService` | `NetworkInformation`, `Windows.Devices.Radios` | no profile → "Disconnected" | 🟡 build |
| Bluetooth | `IBluetoothService` | `Radio` (Windows.Devices.Radios) | access denied → `Enabled=false`, toggle returns false | 🟡 build |
| Weather | `IWeatherService` | Open-Meteo (HTTPS), optional IP geolocation | offline → keeps last reading, silent | 🟡 build |
| Wallpaper | `IWallpaperService` | `SystemParametersInfo`, WIC decode, palette cache | unreadable file → default seed | 🟡 build |
| Notifications | `INotificationService` | in-memory store (system listener in phase 2) | DND → history only | ✅ logic |
| Workspaces | `IWorkspaceService` | virtual-desktop COM (stub today) | unavailable → local pills only | ✅ logic |
| Displays | `IDisplayService` | `DisplayArea.FindAll()` | failure → empty list, bar uses primary | 🟡 build |

## Polling & cadence

| Source | Cadence | Notes |
|---|---|---|
| Sensors | 1 s (`services.pollIntervalMs`) | sample → UI marshalled, history window 60 |
| Clock | 1 s | `DispatcherTimer` |
| Weather | 20 min (configurable) | plus manual refresh |
| Media | event-driven | `MediaPropertiesChanged`, `PlaybackInfoChanged` |
| Network / radios | event-driven | `NetworkStatusChanged`, `Radio.StateChanged` |
| Audio spectrum | capture callback | 48 bars, 1024-point FFT, Hann window, log-spaced bins |

## Contracts and DTOs

`SystemSample`, `MediaTrack`, `NetworkState`, `WeatherNow`, `ShellNotification`,
`WorkspaceInfo`, `MonitorInfo` are immutable records. That means:

- services can be faked in tests without a mocking library,
- the IPC layer can serialise them without special cases,
- a future non-Windows port only needs new implementations of the interfaces.

## Resource lifecycle

`ShellHost.DisposeAsync` stops timers, disposes the IPC server, closes
LibreHardwareMonitor (`Computer.Close`), stops the loopback capture and disposes the
`MMDeviceEnumerator`. The audio service is `IDisposable` precisely because a leaked WASAPI
capture holds an audio endpoint open and shows a red speaker icon in the tray.

## What is deliberately *not* implemented

| Feature | Why | What would be needed |
|---|---|---|
| System toast mirroring | requires packaged identity + capability | MSIX + `UserNotificationListener` |
| Virtual desktop switching | undocumented COM interface | `IVirtualDesktopManagerInternal` with per-build IIDs |
| VPN connect/disconnect | requires stored profiles | `VpnManagementAgent` or RAS |
| Bluetooth device list | needs pairing enumeration | `DeviceInformation` + `BluetoothDevice` |
| Lyrics | provider integration | LRCLIB client + cache |

Each is exposed in the docs as ⬜ rather than faked in the UI.
