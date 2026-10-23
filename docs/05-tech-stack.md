# 5. Tech Stack

## 5.1 Presentation

| Concern | Technology | Why |
|---|---|---|
| UI framework | **WinUI 3 / XAML** (Windows App SDK 1.6) | native, declarative, data-bound, GPU-rendered, Microsoft-supported |
| Animation | **Windows Composition API** | compositor-driven springs; no UI-thread animation |
| Custom drawing | **Win2D** (Direct2D) | sparklines, spectrum bars, SDF shapes |
| Effects | **HLSL** (ps_5_0) | blur, morph, grain |
| Typography | Segoe UI Variable / Cascadia Code | native variable fonts, zero install |
| Icons | Segoe Fluent Icons | ships with Windows |

## 5.2 Application

| Concern | Technology | Why |
|---|---|---|
| Language | **C# 13 / .NET 9** | fast iteration, async-first, strong tooling |
| MVVM | CommunityToolkit.Mvvm | source-generated `ObservableProperty`/`RelayCommand` |
| DI | Microsoft.Extensions.DependencyInjection | explicit composition root |
| JSON | System.Text.Json | source-gen friendly, no reflection at startup for config |
| Config | strongly-typed model + deep merge | per-monitor overrides without a schema library |

## 5.3 Native / interop

| Concern | Technology | Why |
|---|---|---|
| Bridge | **C++/WinRT + Win32 (C ABI)** | app-bar docking, DWM attributes, hotkeys, virtual desktops |
| P/Invoke | hand-written `NativeMethods` | no source generator dependency |
| Audio | **NAudio** (WASAPI loopback + FFT) | real-time spectrum without a driver |
| Sensors | **LibreHardwareMonitor** | CPU/GPU/RAM/temps across vendors |

## 5.4 Platform services

`Windows.Media.Control` (SMTC) · `Windows.Networking.Connectivity` · `Windows.Devices.Radios` ·
`Windows.Devices.Power` · `Windows.Graphics.Imaging` (WIC) · `SystemParametersInfo`, `SHAppBarMessage`,
`DwmSetWindowAttribute`, `RegisterHotKey`.

## 5.5 IPC

Named pipes (`\\.\pipe\radha-shell`) with 4-byte length-prefixed JSON; Unix domain socket on
non-Windows for tests. One catalog (`CommandCatalog`) validates every request.

## 5.6 Build & distribution

MSBuild + `dotnet publish` · CMake for the native bridge · `dxc`/`fxc` for shaders · GitHub Actions
for CI/CD · `Compress-Archive` → GitHub Release (MSIX/WinGet in phase 3).

## 5.7 Deliberate rejections

| Alternative | Why rejected |
|---|---|
| Electron / Tauri / WebView2 | no native composition, 150–250 MB overhead, web security surface for a shell |
| WPF | legacy renderer, no Mica/Acrylic/Composition access |
| Avalonia | weaker Windows integration, no DWM composition |
| QML/Quickshell | Linux-first; no Windows layered-window or AppBar story |
| C++ only | slower iteration for 80% of the code; kept for the 20% that needs it |

## 5.8 Dependency policy

`Radha.Core` has **zero NuGet dependencies** (enforced by CI). All packages live in `Radha.App`,
are pinned, and are reviewed on upgrade. See [17-security.md](17-security.md).
