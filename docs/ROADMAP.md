# Roadmap

Scope discipline: each phase ends with something that **runs**, not something that is merely
described. "Written but not verified" is called out explicitly in the README status table.

## Phase 1 — Shell surface (current)

- [x] Palette engine (OKLab k-means, M3 tonal palettes, gamut + contrast checks) — tested
- [x] Wallpaper decoding (PNG/BMP in Core, WIC for other formats)
- [x] Config with defaults, atomic save, corruption fallback, per-monitor deep merge — tested
- [x] IPC protocol, server, client, catalog validation — tested
- [x] CLI: command surface + offline palette extraction — tested
- [x] WinUI bar, dashboard (overview / media / performance), launcher, control centre,
      notification centre, OSD, session menu — written
- [x] Security hardening: input/path guards, secret redaction, IPC rate limiting, audit log,
      owner-only socket — tested
- [x] Performance: LRU + palette cache, benchmarks with p95 budgets, CI gate
- [x] Complete documentation set (docs/01–24 + policies)
- [x] SMTC media, WASAPI+FFT visualiser, LHM telemetry, radios, Open-Meteo weather — written
- [ ] **Windows build pass**: compile, fix XAML issues, run on a real desktop
- [ ] Replace remaining 🟡 items in docs/08-features.md as the Windows jobs go green
- [ ] Per-monitor bars and panel anchoring
- [ ] App-bar dock verified against maximised / fullscreen windows

## Phase 2 — Depth

- [ ] Composition spring animations for panel open/close and pill morphing
      (`ScalarNaturalMotionAnimation`, expression animations)
- [ ] Wire HLSL effects through `CompositionEffectBrush` (compile `.cso` in CI)
- [x] Virtual desktops: `IVirtualDesktopManagerInternal` with version dispatch, behind the
      existing `IWorkspaceService` contract
- [ ] Global hotkeys shipped with the app (auto-load `RadhaNative.dll`)
- [ ] System toast mirroring (MSIX identity + `UserNotificationListener`)
- [ ] Bluetooth device enumeration, VPN profiles
- [ ] Lyrics provider with cached LRCLIB results
- [ ] Wallpaper picker UI with per-image palette preview and rotation timer

## Phase 3 — Polish

- [ ] Per-monitor DPI auditing and mixed-scale layouts
- [ ] Accessibility pass (UI Automation names, focus visuals, reduced-motion honouring)
- [ ] Localisation framework
- [ ] Crash-safe supervisor mode and structured logs
- [ ] Installer (MSIX + WinGet manifest), auto-update channel

## Explicitly out of scope

- **Lock screen / authentication** — excluded at the product owner's request. No credential
  provider, no PAM/Windows Hello bridge, no lock UI.
- Explorer replacement (file manager, desktop icons)
- Tiling window management
- Plugin marketplace
