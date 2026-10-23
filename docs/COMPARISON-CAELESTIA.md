# Comparison: RADHA SHELL vs caelestia-shell

[caelestia-shell](https://github.com/caelestia-dots/shell) is the closest analogue to this project:
a full desktop shell (bar, dashboard, launcher, notifications, OSD, session, lock) rather than a
theme pack. This document maps its feature surface against ours, states which of its features are
**not implemented here**, and turns the remaining distance into concrete, prioritised engineering
work.

> Reviewed against caelestia-shell `main` at the time of writing: ~630 files, 236 QML modules,
> 55 reusable components, a 215-file C++ QML plugin, 16 services, 6 daemons.

---

## 1. Why this is not a one-to-one comparison

| | caelestia-shell | RADHA SHELL |
|---|---|---|
| Platform | Linux / Wayland (Hyprland) | Windows 10 1809+ / Windows 11 |
| Stack | Quickshell (Qt 6/QML) + C++ plugin | C# / .NET 9 + WinUI 3 + C++ bridge |
| Compositor | needs Hyprland for workspaces, windows, keybinds | own windows on the Windows shell |
| Packaging | AUR + Nix flake + home-manager module | zip artifact + CI-built MSIX (opt-in) |
| Test suite | no unit tests in repo; CI does lint/build/format | 255 assertions + XAML audit + type-check + 11 CI jobs |

Several caelestia features are **compositor-bound** — Hyprland workspaces, window rules, global
shortcuts, `swappy` region capture, PipeWire/CAVA, `nmcli`, `ddcutil`, PAM. On Windows each of those
needs a different API, so "not implemented" below sometimes means "needs a Windows-specific design",
not "missing a copy of their code".

---

## 2. Feature matrix

Legend: **✅ parity** · **⚠️ partial** · **❌ not implemented** · **— not applicable by design**

### Surfaces

| caelestia feature | RADHA | Notes |
|---|---|---|
| Bar with clock, workspaces, status area | ✅ | `BarWindow` |
| Per-monitor bars / monitor targeting | ✅ | one `BarWindow` per `DisplayArea`, per-monitor override honoured |
| Bar position (top/bottom/left/right) | ⚠️ | top/bottom in config; left/right unimplemented |
| Bar popouts (active window, tray, status icons) | ⚠️ | tray icon + live Wi-Fi/Bluetooth/VPN glyphs; no active-window popout |
| Scroll on bar: volume / brightness / workspaces | ✅ | `PointerWheelChanged` → volume or workspace step, configurable |
| `Style layer`: rounded/translucent surfaces | ✅ | tokens + W10 acrylic |
| Dashboard with tabs | ⚠️ | single page; no tab shell |
| Dashboard: media, performance, weather | ✅ | `DashboardView` |
| Dashboard: lap counter, GPU/disk/network graphs | ⚠️ | graphs for CPU/GPU/RAM; no disk/network graphs |
| Launcher: apps, fuzzy search | ✅ | `FuzzyMatcher` |
| Launcher: wallpaper switcher, actions (`>`), calculator | ✅ | `LauncherEngine`: `>` actions, arithmetic row, dangerous-action gate |
| Launcher: favourites / hidden apps / vim keybinds | ✅ | favourites float on an empty query; hidden filtered; j/k/g navigation |
| Notification centre with groups, actions, expiry | ⚠️ | action buttons + DnD + policy engine in Core; list is still flat |
| Sidebar / notification dock | ❌ | notifications are a centred panel only |
| OSD (volume, brightness, mic) | ✅ | `OsdWindow` |
| Session menu (logout/reboot/shutdown/suspend/hibernate) | ⚠️ | no hibernate, no confirmation flow |
| Lock screen (PAM, fingerprint, howdy) | — | **excluded on request** — no auth code in this repo |
| Area picker (region screenshot) | ❌ | none |
| Screen recording | ❌ | none |
| Window info / preview on hover | ❌ | none |
| Desktop clock + background visualiser | ✅ | `BackgroundWindow` parented into Explorer's WorkerW — icon-safe, click-through, parks the 30 Hz timer when silent |
| Wallpaper transition animations | ⚠️ | composition cross-fade driven by `MotionTokens`; the HLSL morph still unused |

### Services

| caelestia service | RADHA | Notes |
|---|---|---|
| Audio (PipeWire) volume/mute | ✅ | WASAPI endpoint + mic |
| Audio spectrum / CAVA / beat tracker | ⚠️ | FFT spectrum only; no CAVA, no beat tracking |
| Brightness (`brightnessctl`, `ddcutil`) | ⚠️ | DDC/CI via control centre, no external-monitor enumeration |
| Media players (MPRIS, multi-player) | ⚠️ | GSMTC single active session; no player switching |
| Lyrics (LRCLIB / NetEase / local) | ⚠️ | LRC parser + LRCLIB query builder tested; no fetch/UI yet |
| Weather | ✅ | Open-Meteo + IP geolocation |
| Network (NM: Wi-Fi, ethernet, VPN, profiles) | ⚠️ | Wi-Fi network list (`netsh`, parsed + tested) + radio + VPN; no ethernet detail |
| Bluetooth (pair, connect, list) | ⚠️ | paired list + connect/disconnect; **no pairing flow** |
| System monitor (CPU, GPU, RAM, disk, net, temps) | ✅ | LibreHardwareMonitor |
| Disk / network usage history | ⚠️ | current values only, no history |
| Colours (`matugen`/smart scheme) | ✅ | own OKLab engine, 6 M3 variants |
| Wallpapers (picker, random, per-monitor) | ✅ | thumbnail grid, `IDesktopWallpaper` per-monitor assignment, span/fit placement, rotation |
| Screens (`Screens.qml` — refresh rate, scale) | ❌ | none |
| Idle / idle-inhibit / auto-lock | ⚠️ | inhibit policy + `SetThreadExecutionState`; auto-lock excluded by instruction |
| Session manager | ⚠️ | power actions only |
| Game mode / power profiles | ❌ | none |
| Battery warnings (configurable levels) | ✅ | `BatteryWatch` with hysteresis and re-arm, surfaced as a notification |
| Updates page (package manager integration) | ⚠️ | GitHub release check with semver comparison; no package-manager integration |
| Plugins | ❌ | no plugin model |

### Infrastructure

| caelestia | RADHA | Notes |
|---|---|---|
| JSON config with nested nodes, enums, per-key defaults | ✅ | `ShellConfig` |
| Config hot-reload | ✅ | debounced watcher, invalid writes rejected, live adoption |
| Config schema validation + quarantine of broken files | ✅ | `ConfigDoctor` (scan/fix), migrations, `.broken` backup, unknown keys preserved |
| GUI settings app (Nexus: 10 pages) | ⚠️ | `SettingsWindow`: appearance, bar, integrations, config health |
| IPC / CLI (`caelestia shell …`, queryable paths) | ✅ | `radha <module> <command>`, typed IPC |
| Global shortcuts | ✅ | native sink → dedicated pipe → `CommandCatalog` validation |
| i18n (translations, plural rules, units) | ⚠️ | translator + CLDR plurals + clock/temperature/size units; UI strings not yet migrated |
| Installer / package (AUR, Nix) | ⚠️ | zip + optional MSIX; no winget/installer |
| Crash reports / issue templates | ⚠️ | redacted diagnostics bundle (config, logs, environment); no automatic crash handler |
| Release automation | ✅ | CI + auto-CD release on every green `main` |

**Score after the second batch:** of ~55 comparable surfaces and services, **32 are at parity, 12
partial, 10 missing**, and 1 (lock screen) is excluded deliberately. What remains missing is almost
entirely capture, previews, shader transitions and plugin surface.

---

## 3. What is genuinely missing, ranked

### P0 — done in this batch

System tray, per-monitor bars, global hotkeys, the settings window, notification actions and bar
scroll were the five gaps users notice first; all six are implemented. The list below is what is
still open.

### P0 — still open

1. ~~**System tray.**~~ ✅ `BarConfig.ShowTray` exists and nothing renders it. Windows equivalent:
   `Shell_NotifyIcon` + a `HwndSource` message-only window, or `Microsoft.UI.System` tray API on
   newer builds. Needs icon cache, overflow menu, and per-icon click routing.
2. ~~**Per-monitor bars.**~~ ✅
3. ~~**Global hotkeys.**~~ ✅ routed through a dedicated pipe and validated against the catalogue.
4. ~~**Settings GUI.**~~ ✅ appearance, bar, integrations, config health.
5. ~~**Notification actions.**~~ ✅ rendered per row, with dismiss and deep-link handling.

### P1 — the difference between "a shell" and "a usable daily driver"

6. **Pairing flows** (Bluetooth) and **Wi-Fi network list with connect** — enumeration is half the job.
7. **Wallpaper picker** grid with thumbnails, `radha wallpaper set <path>`, per-monitor assignment.
8. **Wallpaper transitions** — the DXIL shaders exist; wire them to the backdrop swap.
9. **Idle policy** — display off, lock after timeout, inhibit while media plays.
10. **Config hot-reload** and `radha config reload`.
11. **Launcher actions** (`>` prefix), favourites, hidden apps, calculator.
12. **Desktop clock + background visualiser** — sells the product in a screenshot, cheap to build on
    top of `GetSpectrum`.

### P2 — depth, once the shell is coherent

13. Region screenshot + screen recorder (`Windows.Graphics.Capture`).
14. Window previews (`DwmRegisterThumbnail` or `PrintWindow`), window list popout.
15. Lyrics, multi-player media, per-player volume.
16. i18n and locale-aware units (24h/12h, °C/°F, binary/decimal).
17. Screen settings (refresh rate, HDR), power profiles, battery warnings.
18. Plugin/extension model.
19. Accessibility pass (UIA names, high contrast, reduced motion).
20. Winget manifest + signed installer.

---

## 4. Improvement suggestions by area

### 4.1 Performance

*Current shape:* several independent timers (media, weather, VPN 4 s poll, system monitor), a
LibreHardwareMonitor instance held open, palette extraction on demand.

- **Ref-counted sampling.** `SystemMonitorService` should expose `Acquire()`/`Release()`; stop
  reading CPU/GPU when no dashboard is visible. Idle cost should be ~0 %, not a constant tick.
- **Event-driven instead of polling.** `NetworkListManager` COM events for connectivity,
  `PowerManager` events for battery/AC, `SystemEvents.SessionSwitch` — replace the 4 s VPN poll with
  a read only while a tunnel is up.
- **One clock.** ✅ `ShellClock` — one timer, overrun protection, per-subscriber exception isolation.
- **Allocation discipline.** ✅ the command catalog is a `FrozenDictionary<ShellModule, FrozenSet<string>>`;
  `Series` history is allocation-free. `ArrayPool` in the IPC reader is still open.
- **`LibraryImport`** (source-generated marshalling) for the P/Invoke surface instead of
  `DllImport` — removes the IL stub and the marshaller allocation per call.
- **Publish profile**: `PublishReadyToRun`, `TieredPGO`, `ServerGarbageCollection=false`,
  `InvariantGlobalization=false` (we need cultures) and `UseSystemResourceKeys=false`.
- **Budgets, enforced.** Extend the p95 harness to: cold start to first frame, bar reveal, launcher
  keystroke→results, palette extraction on a 4K image, IPC round-trip. Fail CI on regression >10 %.
- **Profile in CI**, don't guess: `dotnet-trace` + `dotnet-counters` in the `performance` job,
  publish the `.nettrace` as an artifact, compare allocation rate against a baseline.

### 4.2 Speed (perceived)

- **Pre-warm, never create.** Overlay/dashboard/launcher windows should be constructed once,
  hidden, and revealed by visibility — construction costs 100–300 ms and it shows.
- **Show the bar before anything else.** `StartAsync` currently awaits media, IPC and weather before
  the first paint feels complete; move everything except the window into a background continuation.
- **Incremental launcher index.** Build the `IStartMenu` index once, then `FileSystemWatcher` on the
  Start Menu folders; a full re-enumerate on every open is the classic 400 ms stall.
- **Debounce search at 60–80 ms** and cancel in-flight work with a token per keystroke.
- **Virtualise lists** (`ItemsRepeater` + `VirtualizingStackPanel`) before the notification store or
  wallpaper grid can grow past a few hundred rows.
- **Native caching.** The virtual-desktop code already resolves the interface once per build; do the
  same for `IServiceProvider`, hotkey ids and the `AppBar` registration — no per-call
  `CoCreateInstance`.

### 4.3 Personalization

- **A real token layer.** ✅ `DesignTokens` (spacing, padding, radius, typography, density) and
  `MotionTokens` (durations, curves, reduced-motion) live in Core and are unit-tested. Views still
  read literals; adopting the tokens is per-view work, not a design question any more.
- **Appearance profiles.** ✅ `ProfileStore` with sanitised names, size caps and merge-on-apply;
  a corrupt profile cannot take the live config with it.
- **Per-monitor identity.** Wallpaper, scale and bar segments per display — the single biggest
  missing personalization axis on Windows.
- **Bar composition.** Ordered, drag-reorderable segments (clock, workspaces, media, tray, status),
  persisted by id rather than index so upgrades don't scramble layouts.
- **Font stack**: family + weight + numeric variant for clock/digits, mono for telemetry.
- **Icon packs** with a fallback chain, and a documented glyph mapping.
- **Smart scheme**: derive the palette from the *current* wallpaper and recompute on change
  (caelestia's `smartScheme`), plus per-app accent override.

### 4.4 UI/UX customization

- **Motion tokens**: duration + easing + spring per surface, all multiplied by a global
  `AnimationScale`, and honour `UISettings.AnimationsEnabled` (reduced motion) automatically.
- **Density modes** (compact / comfortable), which mostly means two sets of spacing tokens.
- **Consistent focus model**: visible focus rings, `Tab`/`Shift+Tab` order, `Esc` closes the topmost
  surface, arrow-key navigation inside every list.
- **Accessibility baseline**: `AutomationProperties.Name` on every interactive element, high-contrast
  support, 100/125/150/200 % DPI visual audit, minimum 32×32 hit targets.
- **Interaction polish** caelestia has and we don't: scroll-to-adjust on bar segments, hover
  popouts, drag-to-dismiss with a distance threshold, double-click-to-maximise on the bar.
- **Empty and error states** designed, not blank: weather offline, no VPN profiles, mirroring
  unavailable (we now have the reason string — surface it in the UI, not just the property).

### 4.5 Security

Already good: current-user-only IPC, token-bucket limiter, redacted audit log, input guard, fixture
secrets scan, no credentials in the repo. Remaining work, in leverage order:

- **Pin actions by commit SHA.** ✅ all four actions are pinned; `permissions: contents: read` is the
  workflow default and only the release job escalates (`contents`/`id-token`/`attestations`).
- **OIDC + provenance.** ✅ `attest-build-provenance` on the release zip, plus a `SHA256SUMS.txt`
  asset written by the same job.
- **Sign the binaries** (Authenticode; Azure Trusted Signing or SignPath) — an unsigned shell that
  asks for autostart and IPC privileges will be flagged by every EDR product.
- **SBOM** (`dotnet CycloneDX`) + `packages.lock.json` with `RestoreLockedMode` in CI.
- **CodeQL and Dependabot** enabled; `dotnet list package --vulnerable --include-transitive` as a
  gating step (not just a report).
- **Hardening on the native side**: `/guard:cf`, `/DYNAMICBASE`, `/HIGHENTROPYVA`, validate every
  length parameter in the exports, and add a libFuzzer target for the workspace dispatcher —
  undocumented COM vtables are exactly where a bad pointer turns into memory corruption.
- **IPC**: cap frame size *before* allocating, verify the client image if a privileged command is
  requested, and keep the protocol fuzz target in CI.
- **Config as attack surface.** ✅ `LaunchGuard`: allowlist, traversal and UNC rejection, drive-letter
  vs URI disambiguation, and it now guards notification deep links too.

### 4.6 Maintenance

- **Config schema versioning** with forward migrations (`schemaVersion` + a migration chain), and
  unknown-key preservation so a downgrade doesn't destroy user settings.
- **`radha config doctor`**: validates the config, prints the offending line, offers `--fix`.
- **Structured logging** with rotation and a size cap, plus `radha diagnostics` that bundles logs,
  config, build info and recent audit entries into one zip for bug reports.
- **Crash handling**: register a `WER` dump handler, capture the managed stack, offer opt-in upload.
- **Analyzers as gates**: `AnalysisLevel=latest-all`, `TreatWarningsAsErrors` for our own projects,
  `dotnet format --verify-no-changes` in CI (cheap, catches drift before review).
- **Coverage floor + mutation testing** on `Radha.Core` (Stryker). 255 assertions is a good start;
  a coverage threshold stops it from silently decaying.
- **Deprecation policy**: config keys get a one-release warning window, documented in the changelog.
- **ADRs.** ✅ `docs/adr/` — RAS over WinRT VPN, version-dispatched COM, and what CI is allowed to claim.
- **Triage hygiene.** ✅ bug report template with a diagnostics-bundle prompt, PR template with the
  local gates, Dependabot grouped by ecosystem.

### 4.7 Workflow automation

- **Conventional Commits → changelog → release notes**, with the bump derived from commit types
  (`feat!` → major, `feat` → minor, `fix` → patch) instead of a run number.
- **PR gates.** ✅ conventional-commit title lint, `concurrency: cancel-in-progress` on every workflow.
- **Windows VM smoke test on a real desktop session.** This is the honest fix for the current
  limitation: hosted runners are session 0, so `XamlCompiler`/shell behaviours can't be exercised.
  A self-hosted runner (or a scheduled Azure VM with an interactive session) running the built app
  would have caught `UniformGrid` and `LetterSpacing` before CI did.
- **Artifact attestation + checksums** on every release, and a **winget manifest PR** opened
  automatically after a stable tag.
- **Nightly scheduled run.** ✅ `ci` at 03:23 and `release` at 04:17 UTC every day.
- **Dependency automation.** ✅ Dependabot, weekly, grouped (WinAppSDK+BuildTools together — they must
  move in lockstep or `NU1605` returns).
- **Preview builds from PRs**: downloadable artifact + a comment bot linking it. Makes review real.

---

## 5. If only six things get built next

| # | Item | Why first |
|---|---|---|
| 1 | System tray + per-monitor bars | The two most visible Windows-specific gaps |
| 2 | Global hotkeys wired | Native code is already written and tested |
| 3 | Settings GUI (appearance → bar → services) | Removes the "edit JSON to change a colour" wall |
| 4 | Notification actions + grouping | The store already models actions; only the UI is missing |
| 5 | Wallpaper picker + transitions | Existing shaders and palette engine make this nearly free |
| 6 | Config hot-reload + `radha config doctor` | Multiplies the value of every other config feature |

---

## 6. Where we are ahead

Worth stating, because it shapes the plan: caelestia-shell has no unit tests, no static analysis
gate, no cross-platform type-check, and no automatic release pipeline — its CI is lint/build/format.
RADHA SHELL has 255 assertions, an XAML audit, an off-Windows WinUI type-check, 11 CI jobs and a
release that publishes itself on every green `main`. Anything added here should keep that property:
a feature without a test and a doc row does not count as done.

---

Related: [08-features.md](08-features.md) · [09-novel-features.md](09-novel-features.md) ·
[16-performance.md](16-performance.md) · [17-security.md](17-security.md) ·
[19-problematic-areas.md](19-problematic-areas.md) · [ROADMAP.md](ROADMAP.md)
