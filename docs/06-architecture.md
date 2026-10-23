# 6. Architecture

For the deep technical version see [ARCHITECTURE.md](ARCHITECTURE.md). This page is the map.

## 6.1 Layers

```
Presentation   WinUI 3 · XAML · Composition · Win2D · HLSL
                 ├─ BarWindow        (docked app bar)
                 ├─ OverlayWindow    (dashboard / launcher / control centre / notifications / session)
                 └─ OsdWindow        (click-through pill)
View models    ShellState · DashboardViewModel · LauncherViewModel · ControlCenterViewModel
Services       SMTC · NAudio · LibreHardwareMonitor · Network/Radio · Weather · Wallpaper
Core (net9.0)  Theming · Config · Ipc · Modules · Security · Performance · Search · Motion · Weather
Native         C++/WinRT bridge: AppBar, DWM, hotkeys, virtual-desktop stubs
```

Dependencies point downward only. The UI never P/Invokes directly; it consumes interfaces from
`Radha.Core.Services`, which is why the core is testable on Linux.

## 6.2 Project map

| Project | TFM | Runs on Linux | Responsibility |
|---|---|---|---|
| `src/Radha.Core` | net9.0 | ✅ | palette engine, config, IPC, catalog, security, contracts |
| `src/Radha.Cli` | net9.0 | ✅ | `radha` command surface, palette extraction, benchmarks |
| `src/Radha.Tests` | net9.0 | ✅ | 235-assertion harness + image fixtures |
| `src/Radha.App` | net9.0-windows | ❌ | WinUI 3 shell |
| `src/Radha.Native` | C++20 | ❌ | native bridge (CMake) |
| `src/Radha.Shaders` | HLSL | ❌ | GPU effects |

## 6.3 Process & windows

One process owns three windows. The overlay is shown only while a panel is open, so idle cost is a
single 42 px-tall bar. Panels are `UserControl`s, not separate HWNDs.

## 6.4 State & threading

- All view models live on the UI thread.
- Service callbacks are marshalled through `Services/Win/Ui.cs` (`DispatcherQueue.TryEnqueue`).
- `ShellState` is the single source of truth for which surface is open; CLI and buttons share it.
- Theme changes swap an immutable `ThemeViewModel`; one `PropertyChanged` re-colours the tree.
  List items carry their own brushes because XAML name scope cannot reach the shell's theme.

## 6.5 Data flow: a command

```
radha dashboard toggle
      │  (CLI, separate process)
      ▼
IpcClient ── length-prefixed JSON ──► IpcServer
                                        │ 1. rate limit (token bucket)
                                        │ 2. validate (InputGuard)
                                        │ 3. audit (redacted, append-only)
                                        ▼
                                 ShellHost.Execute(catalog-validated)
                                        ▼
                                   ShellState.ShowModule
                                        ▼
                        OverlayWindow visibility + Composition transition
```

## 6.6 Data flow: theming

```
wallpaper file ─► decode (Core PNG/BMP, WIC otherwise) ─► stride sampling
      ─► k-means in OKLab (max 8 clusters, 14 iterations)
      ─► score clusters (chroma, lightness, population) ─► seed
      ─► tonal palettes (primary/secondary/tertiary/neutral/error)
      ─► gamut-map by chroma bisection ─► 35 roles (dark/light)
      ─► ThemeViewModel brushes ─► every surface, cross-faded
```

## 6.7 Failure isolation

| Failure | Containment |
|---|---|
| Sensor driver disappears | try/catch per hardware node; last sample kept |
| No audio endpoint (RDP/VM) | spectrum stays flat, no capture thread |
| Weather offline | last reading retained, no UI error |
| Corrupt `shell.json` | backed up as `.broken`, defaults applied |
| Malformed IPC frame | connection dropped, accept loop survives (tested) |
| Handler exception | error response with redacted message (tested) |
| Native DLL missing | managed shell runs; hotkeys simply absent |
