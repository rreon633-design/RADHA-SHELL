# Changelog

All notable changes to RADHA SHELL are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is
[SemVer](https://semver.org/).

## Unreleased — crashes you can diagnose

- Unhandled exceptions now write `logs/crash-<timestamp>-<type>.log`: version, runtime, OS, the
  exception chain and its stacks, passed through the secrets redactor. The session still survives —
  the report exists so the next bug report has something in it.

## Unreleased — power modes, safe session actions, cheaper telemetry

- `radha system power [balanced|saver|performance]` drives the Windows power overlay; the shell
  remembers the last choice so it can report it back. No elevation, no `powercfg` output parsing.
- `radha system screens` reports every monitor's bounds, scale and refresh rate.
- Session menu: hibernate added; restart, shut down and hibernate arm on the first press and run on
  the second, with a four-second expiry, so an unsaved document cannot be one stray click away.
- Sensor sampling is ref-counted: the dashboard holds a lease only while it is on screen, and the
  service drops from 1 Hz to a 10 s heartbeat when nothing is watching.

## Unreleased — wallpapers you can actually see

- Wallpaper picker: a thumbnail grid, cached on disk per (path, mtime, size), decoded off the
  UI thread once.
- Per-monitor wallpapers through `IDesktopWallpaper` — `SystemParametersInfo` cannot address a
  single display at all. Placement (fill/fit/stretch/center/span) is part of the same call.
- `radha wallpaper monitors | assign <monitor-id|*> <path> | fit [mode]`.
- Optional background layer: wallpaper, clock and audio spectrum drawn behind the desktop icons by
  reparenting into Explorer's WorkerW. Click-through, non-activating, opt-in, and it fails closed —
  if the layer cannot attach it is not shown rather than covering the icons.
- Wallpaper cross-fade in that layer, gated by `MotionTokens` so reduced motion is a cut.

## Unreleased — polish, policy and supply chain

- `DesignTokens` / `MotionTokens` — spacing, padding, radius, typography, density and reduced-motion
  in one tested place.
- `ProfileStore` — named appearance profiles with sanitised names and merge-on-apply.
- `ShellClock` — one 1 Hz tick for the whole shell, with overrun protection.
- `LaunchGuard` — allowlist for anything the shell executes or opens; guards notification deep links.
- Command catalog is now a frozen dictionary (no per-request allocation).
- CI: actions pinned by SHA, least-privilege permissions, nightly runs, concurrency cancellation,
  PR title lint, Dependabot, issue templates.
- Release: build-provenance attestation and a `SHA256SUMS.txt` asset.
- `docs/adr/` — three decision records.

## Unreleased — caelestia parity pass

Closes the gaps found in the caelestia-shell comparison (`docs/COMPARISON-CAELESTIA.md`).

**Core (all unit-tested, 255 → 427 assertions)**
- `ConfigMigrations` — forward-only schema migrations; unknown keys survive.
- `ConfigDoctor` — scans `shell.json` for invalid JSON, out-of-range values, unknown keys and
  future schema versions, and repairs what can be repaired safely.
- `ConfigWatcher` — debounced hot-reload that refuses half-written files.
- `LauncherEngine` + `Calculator` — `>` actions, dangerous-action gate, favourites, hidden apps,
  vim navigation, and arithmetic with a hand-written recursive-descent parser.
- `NotificationRules` — grouping, expiry, Do-Not-Disturb and clearing policy.
- `BatteryWatch` / `IdlePolicy` / `RefreshProfile` — thresholds with hysteresis, inhibit decisions,
  and battery-aware polling.
- `Lrc` + `LyricsQuery` — LRC parsing and LRCLIB query shaping.
- `WifiScanParser` — parses `netsh wlan show networks`.
- `Translator` / `PluralRules` / `Units` — catalogues, CLDR plurals, clock/temperature/size formats.
- `ReleaseCheck` — semver parsing and release selection.
- `Series` — allocation-free history ring buffer.
- `DiagnosticsBundle` — redacted, traversal-safe bug-report zip.

**App**
- System tray (`Shell_NotifyIcon`) with a real context menu.
- One bar per display, each honouring its per-monitor override.
- Global hotkeys wired end to end through a dedicated pipe.
- Settings window: appearance, bar, integrations and config health.
- Notification rows now render their source actions.
- Bar scroll adjusts volume or switches workspace; Wi-Fi/Bluetooth/VPN glyphs are live.
- Idle inhibitor (`SetThreadExecutionState`) while media plays — never locks the session.

**Tooling**
- `tools/TypeCheck` now resolves `xmlns:` → CLR prefixes, so custom views are typed instead of
  falling back to `FrameworkElement`; that alone caught two real errors.

## [Unreleased]

### Added
- **Security hardening (tested):** `InputGuard` request/argument/config-key validation,
  `PathGuard` root containment and filename sanitising, `Redactor` for tokens in errors and logs,
  `RateLimiter` token bucket per IPC client.
- **IPC hardening:** per-request validation and throttling before the handler, redacted exception
  messages, append-only audit trail, owner-only Unix socket permissions, containment of malformed
  and oversized frames.
- **Performance:** `LruCache`, `PaletteCache` (path + mtime + size keys), `LatencyRecorder`,
  `radha bench --gate` with p95 budgets, and a `performance` CI job.
- **Search/motion/weather modules in Core:** `FuzzyMatcher` (prefix/substring/subsequence/acronym),
  `SpringSpec` presets with settle-time and step-response maths, WMO `WeatherCodes`.
- **Config safety:** monitor-override size/type guards and path-escape protection.
- **Documentation:** complete 24-section PRD set (`docs/01`–`docs/24`), `SECURITY.md`,
  `CONTRIBUTING.md`, this changelog, MIT licence.
- **CI:** security job (secret scan, dependency policy, binary/size ceiling), performance job,
  docs job (required documents, link validation, lock-screen absence), aggregate `ci-gate`.
- **CD:** tag-driven release workflow publishing a self-contained Windows archive.

### Changed
- Launcher ranking now uses the shared `FuzzyMatcher` in `Radha.Core` (duplicate scoring removed).
- Weather icon mapping moved to `Radha.Core.Weather.WeatherCodes`.
- Numeric CLI/IPC arguments are parsed with `InputGuard` (NaN/∞ and out-of-range rejected).
- `MaterialScheme`, palettes and image decoding unchanged in behaviour; covered by more tests.

### Fixed
- `dotnet restore` with multiple project paths in CI (invalid switch) — restores the test project,
  which pulls the rest through `ProjectReference`.
- Test/CI fixtures no longer contain a real access token; the redaction tests use a fake
  `github_pat_REDACTED_FOR_TEST_SUITE_…` value.
- `SpringSpec.Normalized()` argument order.
- Palette extraction with a single-pixel image no longer throws.
- `IpcServer.DisposeAsync` is idempotent.

## [0.1.0] — initial scaffold

### Added
- `Radha.Core`: OKLab/OKLCH colour maths, Material 3 tonal palettes (35 roles, 7 variants),
  gamut mapping, PNG/BMP decoding, k-means wallpaper palette extraction, config store with
  per-monitor deep merge, length-prefixed JSON IPC (server + client), command catalog, service
  contracts and DTOs.
- `radha` CLI: full command surface, offline `scheme extract`, IPC client.
- `Radha.Tests`: dependency-free harness with image fixtures (130 assertions at this point).
- `Radha.App`: WinUI 3 bar, overlay host (dashboard / launcher / control centre / notifications /
  session), click-through OSD, Win2D sparklines and spectrum, SMTC media, WASAPI FFT,
  LibreHardwareMonitor telemetry, radios, Open-Meteo weather, wallpaper service.
- `Radha.Native`: C++/WinRT bridge (app bar, DWM effects, global hotkeys, VD stubs).
- `Radha.Shaders`: dual-Kawase blur, rounded-rect SDF morph, grain.
- CI (core, invariants, WinUI, native, shaders) and tag-driven release workflow.

### Not included (by design)
- Lock screen, credential provider, biometric authentication.
- Explorer replacement, tiling window management, plugin runtime.
