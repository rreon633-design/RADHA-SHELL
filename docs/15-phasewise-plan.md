# 15. Phasewise Plan

Each phase ends with something that runs. Status is honest: 🟡 means written but awaiting the
Windows build job.

## Phase 0 — Foundations (complete)

| Deliverable | Status |
|---|---|
| Solution layout, core/CLI/tests/app separation | ✅ |
| OKLab colour math, tonal palettes, gamut mapping | ✅ |
| PNG/BMP decoder + k-means palette extraction | ✅ |
| Config model + store with overrides | ✅ |
| IPC protocol + server + client | ✅ |
| Command catalog | ✅ |
| Test harness + fixtures | ✅ |
| CI (core, invariants, security, performance, docs) | ✅ |

**Exit criteria met:** `dotnet run --project src/Radha.Tests` → 235/235; `radha scheme extract` works.

## Phase 1 — Shell surface (current)

| Deliverable | Status |
|---|---|
| Bar (workspaces, media, tray, clock, session) | 🟡 |
| Dashboard (overview / media / performance) | 🟡 |
| Launcher (apps, fuzzy, `>` actions, calculator) | 🟡 |
| Control centre (12 tiles, sliders, theme card) | 🟡 |
| Notification centre + toasts + DND | 🟡 |
| OSD (click-through) | 🟡 |
| Session menu with confirmation | 🟡 |
| Windows services (SMTC, NAudio, LHM, radios, weather) | 🟡 |
| Win2D sparklines + spectrum | 🟡 |
| Native bridge (app bar, hotkeys, DWM) | 🟡 |
| HLSL effects | 🟡 (not wired) |
| Security hardening of IPC | ✅ |
| Benchmarks + CI perf gate | ✅ |
| Full docs set | ✅ |

**Exit criteria:** `winui` + `native` + `shaders` CI jobs green on a real runner; app-bar docking
verified against maximised windows; no crash on RDP/no-audio.

## Phase 2 — Depth

- Composition spring animations for every panel transition (replace visibility toggles with
  compositor animations; keep state changes instant).
- Wire HLSL effects through `CompositionEffectBrush` (compile `.cso` in CI, load from `assets/`).
- Virtual desktop switching via `IVirtualDesktopManagerInternal` with per-build IID dispatch,
  behind the existing `IWorkspaceService` contract.
- Global hotkeys auto-loaded from `RadhaNative.dll`; rebinding from config.
- System toast mirroring (MSIX identity + `UserNotificationListener`).
- Bluetooth device list, VPN profiles.
- Lyrics (LRCLIB) with cache.
- Wallpaper picker with per-image palette previews and rotation.
- Per-monitor bars.

**Exit criteria:** every panel animates through the compositor; VD switching works on 22H2–24H2;
no managed thread does work inside a composition frame.

## Phase 3 — Productisation

- Accessibility pass (UI Automation names, focus order, reduced motion end-to-end).
- Localisation framework (resource-based, no hard-coded strings in XAML).
- Crash-safe supervisor + structured logs with rotation.
- MSIX packaging, WinGet manifest, auto-update channel.
- Installer-level autostart task.
- ARM64 build.

**Exit criteria:** signed MSIX, `winget install`, zero-config first run.

## Deliberately excluded

Lock screen / authentication (excluded by the product owner), Explorer replacement, tiling,
plugin runtime.
