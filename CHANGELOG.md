# Changelog

All notable changes to RADHA SHELL are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is
[SemVer](https://semver.org/).

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
