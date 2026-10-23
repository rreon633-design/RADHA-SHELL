# 2. Aim & Vision

## 2.1 Aim

Ship **RADHA SHELL**: a native, GPU-composited desktop shell for Windows 10/11 that brings
Material 3 dynamic theming, fluid morphing panels and a unified quick-settings paradigm — built
exclusively on Microsoft's supported stack (WinUI 3, Windows Composition, Win2D, DirectWrite,
Win32/DWM), with no web runtime anywhere in the process.

## 2.2 Vision statement

> Make the Windows desktop feel alive — every colour derived from your wallpaper, every surface
> morphing with intent, every interaction one keystroke or one click away — without giving up a
> frame, a watt, or a supported upgrade path.

## 2.3 Design north star

The shell should feel like a premium Android quick-settings shade and app drawer transplanted onto
a desktop, with the depth of Material 3, but anchored to Windows' compositor, security model and
hardware ecosystem rather than fighting them.

## 2.4 Principles

1. **Native or nothing.** No Electron, Tauri, WebView2, HTML, CSS or JS in the product.
2. **Honest surfaces.** If something is simulated or unimplemented, the code says so and the docs
   say so. No fake "connected" states.
3. **One source of truth.** A command exists once, in `CommandCatalog`, and is reachable identically
   from the CLI, IPC, hotkeys and in-app buttons.
4. **Test what is testable.** Everything that does not need Windows APIs lives in `Radha.Core`,
   which builds and passes its suite on Linux.
5. **Budgeted performance.** Every hot path has a p95 budget enforced in CI.
6. **Fail soft.** A sensor, a driver or a network call failing must never take the shell down.
7. **Boring security.** Validate at the boundary, least privilege, redact logs, deny by default.

## 2.5 Non-goals (explicit)

- ❌ Explorer replacement (file manager, desktop icons, taskbar hijack)
- ❌ Tiling window manager (use FancyZones/komorebi alongside)
- ❌ macOS/Linux support for the shell UI
- ❌ Plugin marketplace or scripting engine in v1 (config + IPC only)
- ❌ Touch-first tablet mode (desktop-first, touch-friendly second)
- ❌ **Lock screen, credential provider, Windows Hello, biometric or password UI**

## 2.6 What success looks like

A user installs one binary, sets a wallpaper, and within a second every surface — bar, panels, OSD
— has re-tinted itself to that image. Opening the dashboard never drops a frame. `radha dashboard
toggle` from any terminal does exactly what the button does. `dotnet run --project src/Radha.Tests`
is green, and the Windows CI job is green too.
