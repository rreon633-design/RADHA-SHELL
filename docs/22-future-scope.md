# 22. Future Scope

Beyond the phased plan in [15-phasewise-plan.md](15-phasewise-plan.md).

## 22.1 Near term (next 2 phases)

| Idea | Why it matters | Rough shape |
|---|---|---|
| Composition-driven panel morphing | the signature "fluid" feel | `ScalarNaturalMotionAnimation` + `SpringSpec` presets already defined and tested |
| HLSL effects wired via `CompositionEffectBrush` | real backdrop blur + grain on any GPU | compile `.cso` in CI (job exists), load from `assets/` |
| Virtual desktop integration | bar workspaces become real | version-dispatched `IVirtualDesktopManagerInternal` |
| System toast mirroring | one notification surface | MSIX identity + `UserNotificationListener` |
| Per-monitor bars | multi-monitor users | one `BarWindow` per `DisplayArea` |
| Wallpaper picker with palette previews | the theming story becomes discoverable | reuse `PaletteCache` + `scheme extract` |
| Lyrics with cache | media tab completeness | LRCLIB client, disk cache, synced highlight |

## 22.2 Medium term

- **Theme export**: emit the current scheme as a Windows accent / Visual Studio / terminal palette.
- **Preset library**: named theming presets ("Aurora", "Graphite", "Paper") selectable per monitor.
- **Video wallpapers**: `MediaPlayerElement` behind the desktop layer (explicitly out of v1).
- **Multi-GPU telemetry**: per-adapter graphs.
- **HDR-aware tint**: respect HDR white point.
- **Config schema + editor**: JSON Schema generation from the typed model, plus a small in-app editor.
- **Localisation**: resource-based strings, RTL-safe layout.

## 22.3 Long term

- **Plugin surface (safe)**: a declarative, capability-scoped manifest — no arbitrary code execution.
  Any plugin runtime must be sandboxed; the current design deliberately has none.
- **ARM64 + Copilot+ devices**: build matrix and battery tuning.
- **Cross-shell theming API**: expose the palette over IPC so other apps can match the desktop.
- **Session continuity**: restore open panels/state across restarts.

## 22.4 Explicitly never

| Idea | Reason |
|---|---|
| Lock screen / credential provider / biometric auth | excluded by the product owner; CI enforces absence |
| Explorer replacement (file manager, desktop icons) | scope, stability, security |
| Bundled tiling WM | use FancyZones/komorebi |
| Web-based plugin runtime | contradicts "no web runtime" |
| Telemetry / analytics | privacy stance |

## 22.5 How ideas are evaluated

An idea is accepted only if it can answer:

1. Does it keep the shell **native** (no web runtime)?
2. Does it respect **budgets** (does `bench --gate` still pass)?
3. Can it be **verified** in CI on Linux or Windows?
4. Can it be **written down honestly** (including its limitations)?
5. Does it avoid the excluded areas?

If any answer is "no", it waits.
