# 8. Features List

Status: ✅ verified in this repo (tests/build) · 🟡 written, requires the Windows build job · ⬜ planned · ❌ excluded

## 8.1 Bar

| Feature | Status | Notes |
|---|---|---|
| Logo → dashboard | 🟡 | gradient tile from the active scheme |
| Workspaces (dots/numbers, active morph) | 🟡 | UI complete; switching is simulated until VD COM lands |
| Active window title | 🟡 | Win32 foreground-window hook pending |
| Now-playing pill (title/artist/state) | 🟡 | SMTC |
| Tray: network, Bluetooth, volume, battery, notifications | 🟡 | badge on unread |
| Clock 12h/24h + date | 🟡 | configurable |
| Session button | 🟡 | opens session card |
| App-bar docking (no overlap with maximised windows) | 🟡 | `SHAppBarMessage` via native bridge |
| Auto-hide / hover reveal | ⬜ | config exists, behaviour in phase 2 |

## 8.2 Dashboard

| Feature | Status |
|---|---|
| Overview: profile, uptime, CPU/GPU/RAM bars, quick tiles, sliders, weather, notification preview | 🟡 |
| Media: artwork, title/artist, SpectrumBars visualiser, transport, progress | 🟡 |
| Performance: CPU/GPU/memory sparklines, network, storage, battery | 🟡 |
| Tab switching without losing state | 🟡 |
| Hover-to-open with delay | ⬜ |

## 8.3 Launcher

| Feature | Status |
|---|---|
| Start-menu app index (user + common) | 🟡 |
| Fuzzy ranking (prefix > substring > subsequence > acronym) | ✅ tested |
| `>` command mode (10 built-in actions) | ✅ catalog-backed |
| Live calculator (`12*(3+4)`) | 🟡 |
| Keyboard navigation | 🟡 |
| Wallpaper picker with palette preview | ⬜ |

## 8.4 Control centre

Tiles: Wi-Fi, Bluetooth, DND, night light, dark mode, screen record, airplane, project, nearby
share, energy saver, accessibility, rotation (🟡 — wired ones: Wi-Fi, Bluetooth, DND, dark mode).
Sliders: volume, brightness, mic (🟡). Theme card: variant picker, dark toggle, next-wallpaper
action (✅ logic / 🟡 UI).

## 8.5 Notifications

| Feature | Status |
|---|---|
| In-app toasts with actions and timeout | 🟡 |
| Notification centre with grouping and clear-all | 🟡 |
| Do-not-disturb (silences toasts, keeps history) | ✅ logic |
| History cap (100) | ✅ |
| Windows toast mirroring | ⬜ (needs MSIX identity + `UserNotificationListener`) |

## 8.6 OSD

Volume, brightness, microphone pills; click-through; 1.6 s auto-hide (🟡).

## 8.7 Session

Sign out, sleep, restart, shut down — with confirmation when `session.confirmPowerActions` is true
(🟡). Uses documented `shutdown.exe` / `powrprof` calls, not shell hooks.

## 8.8 Theming

| Feature | Status |
|---|---|
| Wallpaper → seed extraction (OKLab k-means) | ✅ |
| 7 Material 3 variants | ✅ |
| 35 colour roles, dark + light | ✅ |
| Gamut mapping, contrast guarantees | ✅ tested |
| Palette cache (path + mtime + size) | ✅ tested |
| Manual seed (`radha theme set #hex`) | ✅ |
| Per-monitor theme overrides | ✅ config / 🟡 UI |

## 8.9 Command surface

All 14 modules with 60+ commands — see [24-appendix.md](24-appendix.md#command-reference).
Every command is validated against one catalog; unknown modules/commands are rejected (✅ tested).

## 8.10 Configuration

Defaults, atomic save, corruption fallback, per-monitor deep merge, reflection-based
`radha config set`, override size/type guards (✅ tested).

## 8.11 Diagnostics

`radha bench --gate` (5 benchmarks, p95 budgets), `radha scheme extract --json`, IPC audit log with
redaction, deterministic test fixtures (✅).

## 8.12 Explicitly excluded

- Lock screen / credential provider / Windows Hello / biometric auth (❌ by request)
- Explorer replacement, tiling, plugin runtime, Linux/macOS UI (❌ by scope)
