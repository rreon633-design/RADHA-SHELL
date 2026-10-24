# 13. Setup & Installation

## 13.1 Requirements

| Item | Minimum | Recommended |
|---|---|---|
| OS | Windows 10 1809 (17763) | Windows 11 23H2+ |
| Runtime (self-contained build) | none | none |
| Runtime (framework-dependent) | .NET 9 Desktop Runtime + Windows App Runtime 1.6 | — |
| Build tools | .NET 9 SDK | + VS 2022 17.12 with C++ and .NET desktop workloads |
| GPU | DX11-class (Composition) | any modern iGPU/dGPU |
| Disk | 180 MB self-contained | — |

Linux/macOS can build and test `Radha.Core` and `Radha.Tests` — that is how CI verifies the engine.

## 13.2 Get the source

```bash
git clone https://github.com/rreon633-design/RADHA-SHELL.git
cd RADHA-SHELL
```

## 13.3 Build

### Core and tests (any OS)

```bash
dotnet build src/Radha.Tests/Radha.Tests.csproj -c Release
dotnet run   --project src/Radha.Tests -c Release      # 542 assertions
```

### The shell (Windows)

```powershell
dotnet restore src/Radha.App/Radha.App.csproj -p:Platform=x64
dotnet build   src/Radha.App/Radha.App.csproj -c Release -p:Platform=x64
```

Or use the helper scripts:

```powershell
.\scripts\build.ps1 -Configuration Release -Platform x64 -Native -Test
.\scripts\run.ps1
```

### Native bridge (optional)

```powershell
cmake -B build -S src/Radha.Native -A x64
cmake --build build --config Release
copy build\Release\RadhaNative.dll <app output folder>\
```

### Shaders (optional, not yet wired)

```powershell
dxc -T ps_6_0 -E main src\Radha.Shaders\Blur.hlsl -Fo assets\Blur.cso
```

## 13.4 First run

1. `RadhaShell.exe` → the bar appears at the top of the primary display.
2. Press **Ctrl+Alt+D** (dashboard), **Ctrl+Alt+Space** (launcher) or **Ctrl+Alt+C** (control centre),
   or right-click the tray icon.
3. Right-click the tray icon → **Settings** for every option: appearance, motion, bar, notifications,
   launcher, OSD, services, the desktop layer, and power mode.

If the bar does not appear, see [BUILD.md](BUILD.md#typical-first-build-issues).

## 13.5 Configuration

| Path | Purpose |
|---|---|
| `%APPDATA%\Radha\shell.json` | main config (created on first save) |
| `%APPDATA%\Radha\monitors\<display>\shell.json` | per-monitor overrides |
| `%APPDATA%\Radha\cache\` | palette cache and derived assets |
| `%APPDATA%\Radha\logs\ipc-audit.log` | redacted command audit trail |

Start from the example:

```powershell
copy examples\shell.json "$env:APPDATA\Radha\shell.json"

# …then either edit that file, or use Settings in the tray menu — the shell reloads on save.
```

A corrupt file is moved to `shell.json.broken` and defaults are used — the shell always starts.

## 13.6 Autostart

Phase 1: add a shortcut to `shell:startup`.
Phase 3 ships a proper startup task through the MSIX package.

## 13.7 Uninstall

1. Kill `RadhaShell.exe`.
2. Delete the extracted folder.
3. Delete `%APPDATA%\Radha` (config, cache, logs).
4. Remove the `shell:startup` shortcut.

No registry keys, services or drivers are installed.

## 13.8 Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Nothing appears on the first double-click | the single file is compressed and unpacks itself into `%TEMP%\.net` on first launch; a slow disk makes that a minute of nothing | wait once — later launches reuse the cache |
| "Windows protected your PC" | the build is unsigned | More info → Run anyway, then compare against `SHA256SUMS.txt` |
| Still nothing after a minute | a start-up failure the shell could not report | `RadhaShell.exe --diagnose --out diagnose.txt` and `%APPDATA%\Radha\logs\startup.log` name the stage it stopped at |
| Bar up, no tray icon | Explorer restarted after the shell started | restart RadhaShell |
| Virtual desktops say "unavailable" | no verified layout for this Windows build, or the native probe was stopped | the reason is printed on the pill; the rest of the bar keeps working |
| `windows` CI job red | XAML/SDK drift | read the job log; the core still works |
| Bar overlaps maximised windows | app bar not registered | ensure `RadhaNative.dll` is beside the exe |
| No transparency | battery saver / transparency effects off | Settings → Personalisation → Colours |
| Weather stuck on "Locating…" | no network or blocked geolocation | set `services.weather.latitude/longitude` |
| Visualiser flat | no audio endpoint (RDP/VM) | expected |
