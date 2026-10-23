# Architecture

## Layers

```
┌──────────────────────────────────────────────────────────────┐
│ PRESENTATION      WinUI 3 · XAML · Composition · Win2D       │
│                   BarWindow · OverlayWindow · OsdWindow      │
├──────────────────────────────────────────────────────────────┤
│ VIEW MODELS       ShellState · Dashboard · Launcher · CC     │
│                   CommunityToolkit.Mvvm (ObservableObject)   │
├──────────────────────────────────────────────────────────────┤
│ SERVICES (Win)    GSMTC · NAudio/WASAPI · LibreHardwareMonitor│
│                   NetworkInformation · Radios · Open-Meteo   │
├──────────────────────────────────────────────────────────────┤
│ CORE (portable)   palettes · config · IPC · command catalog  │
│                   service contracts + DTOs (net9.0, tested)  │
├──────────────────────────────────────────────────────────────┤
│ NATIVE            C++/WinRT bridge: AppBar, DWM, hotkeys, VD │
└──────────────────────────────────────────────────────────────┘
```

Dependency direction is strictly downward. The UI never calls Win32/WinRT directly — it talks to
interfaces from `Radha.Core.Services`, which is what makes the core testable off-Windows.

## Windows

| Window | Lifetime | Behaviour |
|---|---|---|
| `BarWindow` | always | borderless, always-on-top, registered as an app bar |
| `OverlayWindow` | shown only while a panel is open | full-screen transparent host for dashboard, launcher, control centre, notifications, session |
| `OsdWindow` | transient | click-through (`WS_EX_TRANSPARENT`), bottom-centre, auto-hides |

One host process owns all three. Panels are `UserControl`s inside `OverlayWindow`, so opening a
panel never creates a new HWND storm — it toggles visibility and plays the Composition transition.

## State and threading

- All view models live on the UI thread. Service callbacks (sensor timer, SMTC events, network
  changes) are marshalled through `Services/Win/Ui.cs` → `DispatcherQueue.TryEnqueue`.
- `ShellState` is the single source of truth for *which surface is open*; every other view model
  holds a reference to it, so a CLI command and a button press take the exact same code path.
- Theme changes replace the immutable `ThemeViewModel`; one `PropertyChanged(nameof(Theme))`
  re-colours the tree. List templates carry their own brushes because XAML name scope prevents
  template bindings from reaching the view model's theme.

## IPC

Length-prefixed JSON over a stream — named pipe `\\.\pipe\radha-shell` on Windows, Unix domain
socket elsewhere (used by tests):

```
[4-byte little-endian length][UTF-8 JSON payload]
```

```jsonc
// request
{ "id": "a1b2c3d4", "module": "dashboard", "command": "toggle", "args": [] }
// response
{ "id": "a1b2c3d4", "ok": true, "payload": "ok", "error": null, "ms": 0.42 }
```

Guarantees:
* the server caps frames at 4 MiB and rejects malformed lengths;
* modules/commands are validated against `CommandCatalog` before execution;
* a handler exception becomes an error response — it cannot kill the daemon;
* disposal is idempotent and cleans up the socket file.

The native hotkey bridge writes the same command text into the pipe, so hotkeys are subject to the
same validation as the CLI.

## Theming

See README "How the theming pipeline works". Key properties:

* **Deterministic** — same image, same scheme. Asserted by tests.
* **Perceptual** — clustering and distance in OKLab, not RGB.
* **Gamut-safe** — chroma reduced by bisection until the sRGB round-trip is stable; a saturated
  yellow seed cannot produce NaN colours (tested).
* **Contrast-checked** — `HasReadableContrast()` asserts ≥ 4.5:1 for primary/onPrimary, surface/
  onSurface, container pairs and error pairs across all 7 variants × dark/light (tested).

## Service mapping (Caelestia → Windows)

| Caelestia (Linux) | RADHA SHELL (Windows) |
|---|---|
| Quickshell/QML | WinUI 3 XAML + Composition |
| cava / aubio FFT | NAudio WASAPI loopback + FFT |
| MPRIS | GlobalSystemMediaTransportControls (SMTC) |
| lm_sensors / psutil | LibreHardwareMonitor + Win32 memory stats |
| NetworkManager | Windows.Networking.Connectivity + Radio API |
| BlueZ | Windows.Devices.Radios |
| Hyprland workspaces | Virtual desktops COM (stubbed) |
| layershell | WS_EX_TOPMOST/LAYERED + DWM backdrop |
| PAM/fprintd/Howdy | *(lock screen intentionally out of scope)* |

## Deliberate omissions

- No plugin system, no scripting engine: extensibility is config + IPC only (phase 1).
- No Explorer replacement; the bar is an app bar, not a taskbar hijack.
- No telemetry, no background service beyond the shell process itself.
