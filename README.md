[![ci](https://github.com/rreon633-design/RADHA-SHELL/actions/workflows/ci.yml/badge.svg)](https://github.com/rreon633-design/RADHA-SHELL/actions/workflows/ci.yml)
[![release](https://github.com/rreon633-design/RADHA-SHELL/actions/workflows/release.yml/badge.svg)](https://github.com/rreon633-design/RADHA-SHELL/actions/workflows/release.yml)

# RADHA SHELL

A native, GPU-composited desktop shell for Windows 10/11 — Material 3 dynamic theming, fluid
morphing panels, a unified quick-settings surface, and a command/IPC surface modelled on
Caelestia's shell for Hyprland.

**No HTML. No CSS. No JavaScript. No Electron, WebView2 or Tauri.**

| Layer | Technology |
|---|---|
| UI | **WinUI 3 / XAML** (Windows App SDK) |
| Animation | **Windows Composition API** (springs, compositor colour/transform animations) |
| Custom rendering | **Win2D** (Direct2D) — sparklines, spectrum bars |
| Shaders | **HLSL** — blur, rounded-rect SDF morph, grain |
| Native interop | **C++/WinRT** + Win32 (AppBar, DWM, hotkeys, virtual desktops) |
| Logic / services | **C# / .NET 9** (`System.Text.Json`, DI, LibreHardwareMonitor, NAudio, SMTC) |
| IPC / CLI | named pipes + length-prefixed JSON, `radha` CLI |

---

## CI/CD — push to ship, no clicks

Every push to `main` runs the full gate on GitHub, and a green run publishes a rolling
`v0.0.0-dev.<run>` release automatically. Tagging ships a real version.

```bash
git push origin main            # → ci runs, then release publishes if green
git tag v0.2.0 && git push origin v0.2.0   # → non-prerelease build
tools/push.sh                   # same push, but streams the run's jobs to your terminal
```

| Workflow | Trigger | Result |
|---|---|---|
| `ci` | push → `main`, pull request, manual | 10 jobs + `ci-gate`; red gate blocks shipping |
| `release` | `workflow_run` of a successful `ci` on `main`, `v*.*.*` tag, manual | `RadhaShell-win-x64.zip` + GitHub Release |

Nothing is dispatched by hand. Details, failure modes and the required repo settings are in
[`docs/CI-CD.md`](docs/CI-CD.md).

## Status — what is actually built

Every project is compiled here. Only one step in the whole pipeline is Windows-locked — running
`XamlCompiler.exe` and packing MSIX — so `Radha.App` is verified in three tiers (see
[Verification tiers](#verification-tiers)); the windows job in CI is the authoritative one.

| Area | State | Evidence |
|---|---|---|
| `Radha.Core` — palette engine, wallpaper decoding, config, IPC protocol, command catalog, security, performance, service contracts | ✅ **built + tested** | **235 assertions, 0 failures**; 5 benchmark budgets green |
| `radha` CLI — palette extraction, command surface, IPC client | ✅ **built + tested** | runs offline for `scheme extract` |
| `Radha.App` — WinUI 3 bar / dashboard / launcher / control centre / notifications / OSD / session | ✅ **C# compiles against WinUI; XAML audited** | `app-typecheck` (27 files → 150 KB IL) + `xaml-audit` (0 findings). XAML→code generation + MSIX need Windows: tier 3 |
| `Radha.Native` — AppBar dock, blur, global hotkeys, VD stubs | ✅ **compiled + linked** | 5 files clean under `clang++ -Werror`; `RadhaNative.dll` (PE32+ x86-64) exports all 14 symbols |
| `Radha.Shaders` — Blur / Morph / Grain | ✅ **compiled** | 3/3 → DXIL via `dxc -T ps_6_0`; not yet wired into the app |
| Virtual desktop switching | ❌ **not implemented** | needs undocumented `IVirtualDesktopManagerInternal`; deliberately stubbed, not faked |
| System toast mirroring | ❌ **not implemented** | needs MSIX identity + `userNotificationListener`; local store works |
| VPN toggling, Bluetooth device list | ❌ **not implemented** | needs stored RAS/VPN profile + device enumeration |
| **Lock screen** | ❌ **not built — excluded on request** | no credential provider, no lock UI, no auth code in this repo |

### Verification tiers

`Radha.App` is a WinUI 3 project, and one step of its build — `XamlCompiler.exe` turning `.xaml`
into C# — is a Windows-only binary. Rather than claim "written, not compiled", the repo verifies it
in three tiers, and CI runs the first two on every push:

| Tier | Where | What it proves |
|---|---|---|
| 1 · static XAML audit (`tools/XamlAudit`) | Linux | Every `x:Class` has a code-behind, every `Click=` handler exists, every `StaticResource` key is defined, every `{Binding}` root and deep path resolves to a real member, every `x:Name` is unique, every element type + namespace exists. No `x:Bind` (which would need the real compiler). |
| 2 · C# type-check (`tools/TypeCheck`) | Linux | All 27 App source files compile against the genuine `Microsoft.WinUI.dll` / WinRT projections from Windows App SDK 1.6, with `InitializeComponent()` and `x:Name` fields generated from the markup. Wrong API names, bad overloads, invalid casts and syntax errors surface here. |
| 3 · full build (`winui` job) | `windows-latest` | XAML→C# codegen, MSIX packaging, launch. The only tier that can produce a runnable shell. |

Tiers 1 and 2 are not a substitute for tier 3 — they cannot catch a XAML parse error or a runtime
layout bug (tier 1 already found a typo'd `{Binding}` while tier 2 found 22 compile errors, so they
are worth running every push).

### Verified here (Linux, .NET 9)

```text
$ dotnet run --project src/Radha.Tests
RADHA SHELL — core test suite
130 passed, 0 failed
```

```text
$ radha scheme extract wallpaper.png
RADHA SHELL · palette from wallpaper.png (64×64)
seed #7C5CFF   variant TonalSpot   dark

  ██████  primary                #A08CFF
  ██████  onPrimary              #320880
  ██████  surface                #141019
  ...
```

---

## Repository layout

```
radha-shell/
├─ RadhaShell.sln
├─ src/
│  ├─ Radha.Core/                 # platform-neutral brain (net9.0)
│  │  ├─ Theming/                 # OKLab/OKLCH, M3 tonal palettes, wallpaper quantiser
│  │  ├─ Config/                  # shell.json model + store, per-monitor overrides
│  │  ├─ Ipc/                     # framing, server, client
│  │  ├─ Modules/                 # command catalog (single source of truth)
│  │  └─ Services/                # contracts + DTOs (media, audio, sensors, weather…)
│  ├─ Radha.App/                  # WinUI 3 shell (net9.0-windows)
│  │  ├─ Views/                   # BarWindow, OverlayWindow, OsdWindow + panels (XAML)
│  │  ├─ ViewModels/              # ShellState, Dashboard, Launcher, ControlCentre, Theme
│  │  ├─ Controls/                # Win2D LineGraph + SpectrumBars
│  │  ├─ Services/Win/            # SMTC, WASAPI+NAudio, LHM, radios, weather, wallpaper
│  │  ├─ Hosting/ShellHost.cs     # composition root + IPC command executor
│  │  └─ Interop/NativeMethods.cs # Win32 surface
│  ├─ Radha.Native/               # C++/WinRT bridge (CMake)
│  ├─ Radha.Shaders/              # HLSL effects + build notes
│  ├─ Radha.Cli/                  # `radha` command-line control
│  └─ Radha.Tests/                # dependency-free test harness + image fixtures
├─ docs/ARCHITECTURE.md · BUILD.md · ROADMAP.md
└─ scripts/build.ps1 · run.ps1
```

---

## CI/CD

Every push to `main` and every pull request runs `.github/workflows/ci.yml`:

| Job | Runner | Gate |
|---|---|---|
| `core` | ubuntu | builds Core + CLI + tests with **warnings as errors**, runs all 235 assertions, smoke-tests the palette CLI and asserts the JSON contract (35 roles, valid hex) |
| `invariants` | ubuntu | fails if any HTML/CSS/JS/Electron/WebView2 artifact appears in `src/`, and validates every XAML / csproj / manifest / JSON file |
| `security` | ubuntu | secret scan (PATs, keys, private-key blocks), dependency policy (`Core` must stay package-free), no tracked binaries/build output, 2 MiB file ceiling |
| `performance` | ubuntu | runs `radha bench --gate`; fails on any p95 budget regression |
| `docs` | ubuntu | asserts all 24 PRD documents + policies exist, validates internal markdown links, and fails if lock-screen code appears in `src/` |
| `winui` | windows | compiles the WinUI 3 shell (`-p:Platform=x64`) and uploads the app folder |
| `native` | windows | compiles the C++/WinRT bridge with MSVC and checks `RadhaNative.dll` exists |
| `shaders` | windows | compiles all three HLSL files to pixel shaders with `fxc` |
| `ci-gate` | ubuntu | single required check — fails if any job above did not succeed |

Tagging `v*.*.*` runs `.github/workflows/release.yml`: tests, publishes the self-contained shell,
the CLI, the native bridge and compiled shaders, then creates a GitHub Release with
`RadhaShell-win-x64.zip` attached.

```bash
git tag v0.1.0 && git push origin v0.1.0
```

## Build

### Linux / CI (core only — fully verified)

```bash
dotnet build src/Radha.Tests/Radha.Tests.csproj
dotnet run   --project src/Radha.Tests
dotnet run   --project src/Radha.Cli -- scheme extract path/to/image.png --json
```

### Windows (full shell)

Prerequisites: **Windows 10 1809+**, **.NET 9 SDK**, **Visual Studio 2022 17.12+** with the
*Windows application development* workload, and the Windows App SDK. Full steps, including the
exact package versions and common first-build fixes, are in [`docs/BUILD.md`](docs/BUILD.md).

```powershell
dotnet build src/Radha.App/Radha.App.csproj -c Release -p:Platform=x64
dotnet run   --project src/Radha.App -p:Platform=x64
```

The bar appears as a docked app bar; the overlay window stays hidden until a panel opens.
Start with `radha dashboard toggle`, `radha controlcenter toggle`, `radha launcher toggle`.

---

## Documentation

The full PRD set lives in [`docs/`](docs/README.md): problem statement, aim/vision, objectives,
target audience, tech stack, architecture, design philosophy, features, novel features, backend
services, API/IPC, workflow, setup, development process, phase plan, performance, security,
maintenance, problematic areas, risks, advantages, future scope, success metrics and appendix.

Policies: [SECURITY.md](SECURITY.md) · [CONTRIBUTING.md](CONTRIBUTING.md) · [CHANGELOG.md](CHANGELOG.md) · [LICENSE](LICENSE)

## Command surface

The CLI and the in-app launcher share one catalog (`Radha.Core/Modules/CommandCatalog.cs`):

```
radha bar            show | hide | toggle | reload
radha dashboard      open | close | toggle | tab
radha launcher       open | close | toggle | query
radha controlcenter  open | close | toggle
radha notifications  open | close | toggle | clear | dnd-on | dnd-off | dnd-toggle
radha osd            volume <0-100> | brightness <0-100> | mic
radha session        menu | logout | suspend | reboot | shutdown
radha media          play-pause | next | previous | seek <s> | mute | status
radha wallpaper      set <path> | next | random | list | current
radha theme          set <#hex> | variant <v> | dark | light | toggle | status
radha network        status | wifi-toggle | vpn-toggle
radha bluetooth      status | toggle
radha system         status | volume <0-100> | brightness <0-100> | uptime
radha config         get | set <path> <value> | path | reload | monitor-override <monitor> <json>
radha scheme extract <image> [--variant v] [--light|--dark] [--json]
```

The daemon validates every request against the same catalog — an unknown module or command is
rejected with an error response, never executed blindly.

---

## Configuration

`%APPDATA%\Radha\shell.json` (created on first save; missing file = defaults). Per-monitor
overrides live in `%APPDATA%\Radha\monitors\<display>\shell.json` and are deep-merged.

```json
{
  "appearance": {
    "schemeVariant": "vibrant",
    "mode": "auto",
    "scale": 1.0,
    "transparency": true,
    "blurRadius": 30
  },
  "bar": { "position": "top", "height": 42, "workspaceCount": 5, "workspacesDisplay": "dots" },
  "dashboard": { "openOnHover": true, "performanceGraphs": ["cpu", "gpu", "memory", "network"] },
  "launcher": { "fuzzy": true, "actionPrefix": ">", "liveCalculator": true },
  "notifications": { "timeoutSeconds": 6, "groupByApp": true, "doNotDisturb": false },
  "services": { "weather": { "provider": "open-meteo", "units": "metric" } },
  "paths": { "wallpapers": "C:\\Users\\me\\Pictures\\Wallpapers" }
}
```

`radha config set appearance.scale 1.1` writes through reflection to the strongly typed model,
then saves atomically (temp file + swap). A corrupt file is backed up to `shell.json.broken` and
defaults are used — the shell always starts.

---

## How the theming pipeline works

1. Wallpaper path → **PNG/BMP decoded in Core**, JPEG/WebP through WIC in the app.
2. Pixels sampled on a deterministic stride → **k-means in OKLab** (perceptual, not RGB).
3. Clusters scored for chroma / mid-lightness / population → **seed colour**.
4. Seed → hue/chroma per palette (primary, secondary, tertiary, neutral, error) →
   **tonal palettes** with gamut mapping by chroma bisection.
5. Roles resolved with Material 3 tone tables for dark/light → `MaterialScheme` (35 roles).
6. `ThemeViewModel` turns the scheme into XAML brushes; one `PropertyChanged` re-colours every
   surface. List templates get **item-level brushe**s because XAML name scope cannot see the shell.

The whole pipeline is pure C# in `Radha.Core`, which is why it runs — and is tested — on Linux.

---

## Honest limitations

- **WinUI code has not been compiled.** Expect small fixes on the first Windows build
  (XAML name scope, SDK version drift). The core logic it consumes is tested.
- The bar uses a manual window position + `SHAppBarMessage`; multi-monitor per-display bars are
  scaffolded (`DisplayService`) but not finished.
- Global hotkeys live in the native bridge and need `RadhaNative.dll` next to the app; the managed
  app works without it (buttons + CLI still function).
- The visualiser needs an active WASAPI endpoint; on RDP/VM it stays flat instead of crashing.
- Weather uses Open-Meteo and resolves an approximate city via `ipapi.co` **once** if coordinates
  are not set in config; set `services.weather.provider` to `manual` to disable that.
- No lock screen, no Explorer replacement, no tiling — by design and, for the lock screen, by
  explicit request.

See [`docs/ROADMAP.md`](docs/ROADMAP.md) for what comes next.
