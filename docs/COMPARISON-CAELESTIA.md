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
| Per-monitor bars / monitor targeting | ❌ | ours is a single window on the primary display |
| Bar position (top/bottom/left/right) | ⚠️ | top/bottom in config; left/right unimplemented |
| Bar popouts (active window, tray, status icons) | ❌ | none |
| Scroll on bar: volume / brightness / workspaces | ❌ | no pointer-wheel handlers |
| `Style layer`: rounded/translucent surfaces | ✅ | tokens + W10 acrylic |
| Dashboard with tabs | ⚠️ | single page; no tab shell |
| Dashboard: media, performance, weather | ✅ | `DashboardView` |
| Dashboard: lap counter, GPU/disk/network graphs | ⚠️ | graphs for CPU/GPU/RAM; no disk/network graphs |
| Launcher: apps, fuzzy search | ✅ | `FuzzyMatcher` |
| Launcher: wallpaper switcher, actions (`>`), calculator | ⚠️ | wallpaper via control centre; no `>` actions, no calculator |
| Launcher: favourites / hidden apps / vim keybinds | ❌ | none |
| Notification centre with groups, actions, expiry | ⚠️ | list + dismiss; no per-notification action buttons, no grouping rules |
| Sidebar / notification dock | ❌ | notifications are a centred panel only |
| OSD (volume, brightness, mic) | ✅ | `OsdWindow` |
| Session menu (logout/reboot/shutdown/suspend/hibernate) | ⚠️ | no hibernate, no confirmation flow |
| Lock screen (PAM, fingerprint, howdy) | — | **excluded on request** — no auth code in this repo |
| Area picker (region screenshot) | ❌ | none |
| Screen recording | ❌ | none |
| Window info / preview on hover | ❌ | none |
| Desktop clock + background visualiser | ❌ | `AudioService.GetSpectrum` exists; nothing renders it |
| Wallpaper transition animations | ❌ | 3 shaders compiled, 0 wired |

### Services

| caelestia service | RADHA | Notes |
|---|---|---|
| Audio (PipeWire) volume/mute | ✅ | WASAPI endpoint + mic |
| Audio spectrum / CAVA / beat tracker | ⚠️ | FFT spectrum only; no CAVA, no beat tracking |
| Brightness (`brightnessctl`, `ddcutil`) | ⚠️ | DDC/CI via control centre, no external-monitor enumeration |
| Media players (MPRIS, multi-player) | ⚠️ | GSMTC single active session; no player switching |
| Lyrics (LRCLIB / NetEase / local) | ❌ | config keys only, no provider |
| Weather | ✅ | Open-Meteo + IP geolocation |
| Network (NM: Wi-Fi, ethernet, VPN, profiles) | ⚠️ | Wi-Fi radio toggle + VPN profiles; no ethernet detail, no Wi-Fi network list |
| Bluetooth (pair, connect, list) | ⚠️ | paired list + connect/disconnect; **no pairing flow** |
| System monitor (CPU, GPU, RAM, disk, net, temps) | ✅ | LibreHardwareMonitor |
| Disk / network usage history | ⚠️ | current values only, no history |
| Colours (`matugen`/smart scheme) | ✅ | own OKLab engine, 6 M3 variants |
| Wallpapers (picker, random, per-monitor) | ⚠️ | set/next; no picker grid, no per-monitor |
| Screens (`Screens.qml` — refresh rate, scale) | ❌ | none |
| Idle / idle-inhibit / auto-lock | ❌ | none |
| Session manager | ⚠️ | power actions only |
| Game mode / power profiles | ❌ | none |
| Battery warnings (configurable levels) | ❌ | battery % shown, no thresholds |
| Updates page (package manager integration) | ❌ | none |
| Plugins | ❌ | no plugin model |

### Infrastructure

| caelestia | RADHA | Notes |
|---|---|---|
| JSON config with nested nodes, enums, per-key defaults | ✅ | `ShellConfig` |
| Config hot-reload | ❌ | read at startup |
| Config schema validation + quarantine of broken files | ⚠️ | validate + fall back; no quarantine/backup |
| GUI settings app (Nexus: 10 pages) | ❌ | JSON editing only |
| IPC / CLI (`caelestia shell …`, queryable paths) | ✅ | `radha <module> <command>`, typed IPC |
| Global shortcuts | ⚠️ | native `RadhaHotkeyRegister` exists; **no caller in the app** |
| i18n (translations, plural rules, units) | ❌ | English-only, hard-coded strings |
| Installer / package (AUR, Nix) | ⚠️ | zip + optional MSIX; no winget/installer |
| Crash reports / issue templates | ❌ | none |
| Release automation | ✅ | CI + auto-CD release on every green `main` |

**Score:** of ~55 comparable surfaces and services, **18 are at parity, 15 partial, 19 missing**,
and 1 (lock screen) is excluded deliberately.

---

## 3. What is genuinely missing, ranked

### P0 — users notice these in the first minute

1. **System tray.** `BarConfig.ShowTray` exists and nothing renders it. Windows equivalent:
   `Shell_NotifyIcon` + a `HwndSource` message-only window, or `Microsoft.UI.System` tray API on
   newer builds. Needs icon cache, overflow menu, and per-icon click routing.
2. **Per-monitor bars.** Windows has multi-monitor as the default, not the exception. Requires one
   `BarWindow` per `DisplayArea`, a monitor id in state, and DPI-safe placement.
3. **Global hotkeys actually wired.** The native export works and is tested; the app never calls it.
   `RadhaHotkeySetPipeEndpoint` already supports routing a hotkey into the IPC pipe.
4. **Settings GUI.** Editing JSON by hand contradicts "personalization". Priority order:
   appearance → bar → services → about.
5. **Notification actions.** WinRT toasts carry buttons; we drop them on the floor.

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
- **One clock.** A single 1 Hz tick in Core that all widgets subscribe to, with coalesced
  invalidation; today each viewmodel ticks itself.
- **Allocation discipline.** `FrozenDictionary` for the command catalog and config lookups,
  `ArrayPool<byte>` in the IPC frame reader, `StringComparison.Ordinal`, `ValueTask` on hot paths.
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

- **A real token layer.** caelestia exposes rounding, spacing, padding and font *scales*, each with
  9 steps. We have rounding and font scale; add spacing/padding and make every view read tokens
  instead of literals.
- **Appearance profiles.** Named bundles (`work`, `gaming`, `dim`) that swap scheme, wallpaper and
  bar layout atomically, import/exportable as JSON.
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

- **Pin actions by commit SHA**, not `@v4`; add `permissions: contents: read` at workflow level and
  elevate per job. A compromised tag is the most common supply-chain entry point.
- **OIDC + provenance**: drop long-lived secrets, use `actions/attest-build-provenance` on release
  artifacts, publish a SHA-256 manifest alongside the zip.
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
- **Config as attack surface**: config can name an executable to launch (terminal, explorer). Resolve
  through an allowlist, refuse `..` and UNC paths, and check the file isn't world-writable.

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
- **ADRs** for decisions that keep coming back (why RAS instead of `Windows.Networking.Vpn`, why no
  vtable hard-coding). We have the analysis — it belongs in `docs/adr/`, not only in commit bodies.
- **Triage hygiene**: issue templates (bug/feature/crash), labels, "good first issue", a stale bot.

### 4.7 Workflow automation

- **Conventional Commits → changelog → release notes**, with the bump derived from commit types
  (`feat!` → major, `feat` → minor, `fix` → patch) instead of a run number.
- **PR gates**: title lint (they have it, we don't), auto-label by path, `concurrency:
  cancel-in-progress` per branch, `fail-fast: false` on matrices.
- **Windows VM smoke test on a real desktop session.** This is the honest fix for the current
  limitation: hosted runners are session 0, so `XamlCompiler`/shell behaviours can't be exercised.
  A self-hosted runner (or a scheduled Azure VM with an interactive session) running the built app
  would have caught `UniformGrid` and `LetterSpacing` before CI did.
- **Artifact attestation + checksums** on every release, and a **winget manifest PR** opened
  automatically after a stable tag.
- **Nightly scheduled run** (not just push-triggered) to catch upstream breakage — WinAppSDK and
  Windows SDK ship frequently; a green build today can be red tomorrow with no code change.
- **Dependency automation**: Renovate/Dependabot grouped by ecosystem (Actions, NuGet, WinAppSDK),
  auto-merge patch-level after CI.
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
