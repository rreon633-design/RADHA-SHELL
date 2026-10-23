# 24. Appendix

## Command reference

```text
radha bar            show | hide | toggle | reload
radha dashboard      open | close | toggle | tab
radha launcher       open | close | toggle | query
radha controlcenter  open | close | toggle
radha notifications  open | close | toggle | clear | dnd-on | dnd-off | dnd-toggle
radha osd            volume <0-100> | brightness <0-100> | mic | show
radha session        menu | logout | suspend | reboot | shutdown
radha media          play-pause | next | previous | seek <s> | mute | status
radha wallpaper      set <path> | next | random | list | current
radha theme          set <#hex> | variant <v> | dark | light | toggle | status | extract
radha network        status | wifi-toggle | vpn-toggle
radha bluetooth      status | toggle
radha system         status | volume <0-100> | brightness <0-100> | uptime
radha config         get | set <path> <value> | path | reload | monitor-override <m> <json>
radha scheme         extract <image> [--variant v] [--light|--dark] [--json]
radha bench          [--json] [--gate]
radha help
```

Exit codes: `0` success · `1` usage error · `2` daemon not running · `3` command failed.

## Configuration reference (abridged)

| Key | Type | Default | Notes |
|---|---|---|---|
| `appearance.mode` | string | `auto` | `auto` / `dark` / `light` |
| `appearance.schemeVariant` | string | `tonalSpot` | `tonalSpot,vibrant,expressive,fidelity,content,neutral,monochrome` |
| `appearance.scale` | double | `1.0` | clamped 0.6–2.0 |
| `appearance.fontScale` | double | `1.0` | clamped 0.7–1.8 |
| `appearance.transparency` | bool | `true` | acrylic on/off |
| `bar.position` | string | `top` | `top` / `bottom` |
| `bar.height` | int | `42` | clamped 28–64 |
| `bar.workspaceCount` | int | `5` | clamped 1–12 |
| `bar.workspacesDisplay` | string | `dots` | `dots,numbers,shapes,icons` |
| `dashboard.openOnHover` | bool | `true` | |
| `dashboard.performanceGraphs` | string[] | cpu,gpu,memory,network,storage,battery | |
| `launcher.fuzzy` | bool | `true` | |
| `launcher.actionPrefix` | string | `>` | |
| `launcher.liveCalculator` | bool | `true` | |
| `notifications.timeoutSeconds` | int | `6` | clamped 2–60 |
| `notifications.maxVisible` | int | `3` | clamped 1–10 |
| `notifications.doNotDisturb` | bool | `false` | |
| `osd.timeoutMs` | int | `1600` | clamped 400–6000 |
| `session.confirmPowerActions` | bool | `true` | |
| `services.weather.provider` | string | `open-meteo` | |
| `services.weather.latitude/longitude` | number? | null | null ⇒ IP lookup once |
| `services.audio.visualizerBars` | int | `48` | |
| `services.pollIntervalMs` | int | `1000` | sensor cadence |
| `wallpaper.path` | string | `""` | applied at startup |
| `paths.wallpapers` | string | `~/Pictures/Wallpapers` | listed in the picker |

Full sample: [examples/shell.json](../examples/shell.json).

## Keyboard shortcuts (native bridge)

| Chord | Action |
|---|---|
| `Win+Space` | launcher |
| `Win+Tab` | dashboard |
| `Win+A` | control centre |
| `Win+N` | notifications |
| `Win+W` | next wallpaper |

## File locations

| Path | Contents |
|---|---|
| `%APPDATA%\Radha\shell.json` | configuration |
| `%APPDATA%\Radha\monitors\<display>\shell.json` | per-monitor overrides |
| `%APPDATA%\Radha\logs\ipc-audit.log` | redacted IPC audit |
| `%APPDATA%\Radha\cache\` | derived assets |

## Project layout

```text
src/Radha.Core     engine (net9.0, zero packages, tested on Linux)
src/Radha.Tests    235-assertion harness + fixtures
src/Radha.App      WinUI 3 shell (net9.0-windows)
src/Radha.Native   C++/WinRT bridge (CMake)
src/Radha.Shaders  HLSL effects
docs/              this documentation set
examples/          sample shell.json
scripts/           build.ps1, run.ps1
```

## Test inventory

| Suite | Assertions | Covers |
|---|---|---|
| Colour math | ~7 | OKLCH round-trip, contrast, hex, lerp |
| Tonal palettes | ~5 | monotonicity, endpoints, chroma |
| Schemes | ~30 | 7 variants × 2 modes, determinism, gamut, contrast |
| Image decoding | ~10 | PNG filters, BMP, channel order, errors |
| Palette extraction | ~8 | clustering, determinism, edge cases |
| Config | ~20 | defaults, round-trip, corruption, overrides, guards |
| Command catalog | ~6 | parsing, validity, help coverage |
| IPC | ~10 | round-trip, concurrency, errors, cleanup |
| CLI | ~8 | palette JSON, exit codes, help |
| Security | ~40 | validation, paths, redaction, rate limit, socket, hostile frames |
| Performance | ~15 | caches, budgets, throughput |
| Feature hardening | ~20 | fuzzy matcher, springs, weather codes |

## Glossary

| Term | Meaning |
|---|---|
| **M3** | Material Design 3 colour/typography system |
| **Seed** | the single colour from which a scheme is derived |
| **Tonal palette** | one hue at multiple tones (0–100) |
| **Gamut mapping** | reducing chroma until a colour is displayable in sRGB |
| **SMTC** | System Media Transport Controls (Windows media session API) |
| **LHM** | LibreHardwareMonitor |
| **App bar** | Windows desktop toolbar that reserves work-area space |
| **Composition** | Windows compositor API for GPU animation |
| **SDF** | signed distance field (used for morphable rounded rects) |

## References

- Material 3 colour system — <https://m3.material.io/styles/color/overview>
- OKLab — <https://bottosson.github.io/posts/oklab/>
- Windows App SDK — <https://learn.microsoft.com/windows/apps/windows-app-sdk/>
- Composition animations — <https://learn.microsoft.com/windows/apps/design/motion/>
- SMTC — <https://learn.microsoft.com/uwp/api/windows.media.control>
- Caelestia Shell (inspiration) — <https://github.com/caelestia-dots/shell>
