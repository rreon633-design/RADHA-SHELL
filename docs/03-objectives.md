# 3. Objectives

Status legend: ✅ implemented and verified · 🟡 implemented, awaiting Windows build · ⬜ planned

| ID | Objective | Measurable target | Status | Where |
|---|---|---|---|---|
| O1 | Status bar with workspaces, media, tray, clock | all entries render; docked as an app bar | 🟡 | `Views/BarWindow.xaml`, `Interop/NativeMethods.cs` |
| O2 | Dashboard with overview/media/performance | 3 tabs, live data at 1 Hz | 🟡 | `Views/DashboardView.xaml` |
| O3 | Launcher: fuzzy search, `>` actions, calculator | p95 ranking < 60 ms for 5 000 apps | 🟡 (matcher ✅) | `Search/FuzzyMatcher.cs`, `ViewModels/DashboardViewModel.cs` |
| O4 | Control centre with 12 tiles + 3 sliders | toggle latency < 100 ms, no leaks | 🟡 | `Views/ControlCenterView.xaml` |
| O5 | Notification centre + toast pipeline | grouping, DND, actions, history cap 100 | 🟡 | `Services/Win/NotificationStore.cs` |
| O6 | OSD for volume/brightness/mic | click-through, auto-hide ≤ 2 s | 🟡 | `Views/OsdWindow.xaml` |
| O7 | Dynamic Material 3 theming from wallpaper | 35 colour roles, 7 variants, AA contrast | ✅ | `Theming/*` |
| O8 | Wallpaper → palette extraction | p95 < 120 ms for a 512×512 image | ✅ | `Theming/WallpaperPalette.cs` |
| O9 | Command/IPC surface | every module reachable from CLI | ✅ | `Ipc/*`, `Modules/CommandCatalog.cs` |
| O10 | Config with per-monitor overrides | deep merge, atomic save, corruption fallback | ✅ | `Config/*` |
| O11 | Hardened IPC | validation + rate limit + redacted audit | ✅ | `Security/*`, `Ipc/IpcProtocol.cs` |
| O12 | Performance gates in CI | `bench --gate` fails on p95 regression | ✅ | `.github/workflows/ci.yml` |
| O13 | Native bridge (app bar, hotkeys, DWM) | builds with MSVC, exports stable C ABI | 🟡 | `src/Radha.Native` |
| O14 | HLSL effects | 3 shaders compile with `fxc` | 🟡 | `src/Radha.Shaders` |
| O15 | Virtual desktop integration | switch desktops from the bar | ⬜ | needs `IVirtualDesktopManagerInternal` |
| O16 | System toast mirroring | show Windows toasts in our centre | ⬜ | needs MSIX identity + `UserNotificationListener` |
| O17 | Lock screen | — | ❌ excluded by request | not in scope |

## Definition of done (per objective)

An objective is only "done" when:
1. the code compiles on Windows in CI (`winui` job),
2. it has tests in `Radha.Tests` where the logic is platform-neutral,
3. it is documented in [08-features.md](08-features.md) with an accurate status,
4. it does not push a benchmark past its p95 budget.

## Non-objectives

The following were considered and rejected for v1: Explorer replacement, tiling, plugin runtime,
mobile/tablet layout, Linux/macOS UI, lock screen.
