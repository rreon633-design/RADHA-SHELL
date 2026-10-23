# Building RADHA SHELL

## What builds where

| Project | Linux / CI | Windows | Notes |
|---|---|---|---|
| `Radha.Core` | ✅ | ✅ | `net9.0`, no OS APIs |
| `Radha.Cli` | ✅ | ✅ | produces `radha.exe` |
| `Radha.Tests` | ✅ | ✅ | console harness, no NuGet test packages |
| `Radha.App` | ❌ | ✅ | WinUI 3, `net9.0-windows10.0.19041.0` |
| `Radha.Native` | ❌ | ✅ | CMake + MSVC |

## Windows prerequisites

- Windows 10 1809 (17763) or newer, Windows 11 recommended
- **.NET 9 SDK** (`dotnet --version` → 9.0.x)
- **Visual Studio 2022 17.12+** with:
  - *Desktop development with C++*
  - *.NET desktop development*
  - *Windows application development* (installs the Windows App SDK targets)
- WebView2 runtime is **not** required

Restore is limited to these packages (versions in `Radha.App.csproj`):

```
Microsoft.WindowsAppSDK        1.6.250228001
Microsoft.Windows.SDK.BuildTools 10.0.26100.1742
CommunityToolkit.Mvvm          8.4.0
Microsoft.Extensions.DependencyInjection 9.0.0
Microsoft.Graphics.Win2D       1.3.2
NAudio                         2.2.1
LibreHardwareMonitorLib        0.9.4
System.Reactive                6.0.1
```

## Build the shell

```powershell
git clone <your-fork> radha-shell
cd radha-shell

dotnet restore src/Radha.App/Radha.App.csproj

# x64 is the default platform for WinUI 3 unpackaged apps
dotnet build src/Radha.App/Radha.App.csproj -c Release -p:Platform=x64
```

Output: `src/Radha.App/bin/x64/Release/net9.0-windows10.0.19041.0/win-x64/Radha.App.exe`

`WindowsAppSDKSelfContained=true` is set, so the Windows App SDK runtime is copied next to the
app — no machine-wide install needed. If you prefer framework-dependent deployment, set it to
`false` and install the Windows App Runtime 1.6 redistributable.

## First run

```powershell
.\build\Radha.App.exe        # bar appears at the top of the primary display
radha dashboard toggle       # or use the logo button in the bar
```

If the bar does not appear:

1. Check `%APPDATA%\Radha\logs` (once logging lands) and the Visual Studio output window.
2. Run from a console with `dotnet run --project src/Radha.App -p:Platform=x64` to see exceptions.
3. Unpackaged WinUI apps need the `Microsoft.WindowsAppRuntime` DLLs beside the executable —
   they are copied automatically when self-contained.

## Native bridge (optional in phase 1)

```powershell
cmake -B build -S src/Radha.Native -A x64
cmake --build build --config Release
copy build\Release\RadhaNative.dll <app-output-folder>\
```

Provides app-bar docking fallbacks, DWM blur helpers and **global hotkeys**. Without it the shell
still runs; hotkeys registered through `RegisterHotKey` are simply absent.

Default hotkeys (implemented in `Hotkeys.cpp`, rebinding planned via config):

| Chord | Action |
|---|---|
| `Win + Space` | launcher |
| `Win + Tab` | dashboard |
| `Win + A` | control centre |
| `Win + N` | notifications |
| `Win + W` | next wallpaper |

## Shaders (optional)

```powershell
dxc -T ps_6_0 -E main src\Radha.Shaders\Blur.hlsl  -Fo assets\Blur.cso
dxc -T ps_6_0 -E main src\Radha.Shaders\Morph.hlsl -Fo assets\Morph.cso
dxc -T ps_6_0 -E main src\Radha.Shaders\Grain.hlsl -Fo assets\Grain.cso
```

Not wired into the app yet — phase 1 uses DWM acrylic/Mica.

## Typical first-build issues

| Symptom | Fix |
|---|---|
| `WindowsAppSDK` restore fails | update the version to the latest 1.6.x on nuget.org |
| `XamlCompiler error WMC1006` | open the solution once in VS so the WinUI targets are registered |
| App starts then exits immediately | an exception in `OnLaunched`; run under the debugger |
| Acrylic looks solid grey | transparency disabled by *Settings → Personalisation → Colours*, or on battery saver |
| No weather | set `services.weather.latitude/longitude` in `shell.json`, or allow location/network access |
| Spectrum flat | no active audio endpoint (RDP/VM), or nothing is playing |

## Running the verified core anywhere

```bash
dotnet run --project src/Radha.Tests     # 130 assertions
dotnet run --project src/Radha.Cli -- scheme extract sample.png --json
```
