# Building RADHA SHELL

## What builds where

| Project | Linux / CI | Windows | Notes |
|---|---|---|---|
| `Radha.Core` | ✅ | ✅ | `net9.0`, no OS APIs |
| `Radha.Cli` | ✅ | ✅ | produces `radha.exe` |
| `Radha.Tests` | ✅ | ✅ | console harness, no NuGet test packages |
| `Radha.App` (C# type-check) | ✅ | ✅ | `tools/TypeCheck` — compiles every `.cs` against the real WinUI reference assemblies |
| `Radha.App` (XAML → runnable shell) | ❌ | ✅ | needs `XamlCompiler.exe` + MSIX; Windows-only |
| `Radha.Native` | ✅ (cross) | ✅ | CMake + MSVC on Windows; `clang++ -target x86_64-w64-windows-gnu` on Linux |
| `Radha.Shaders` | ✅ (cross) | ✅ | `dxc -T ps_6_0`; `fxc /T ps_5_0` on the Windows runner |

### Why `Radha.App` is split

`XamlCompiler.exe` — the step that turns `.xaml` into C# and emits the `InitializeComponent()` body —
ships as a Windows PE executable inside the `Microsoft.WindowsAppSDK` package, and the MSIX/PRI tasks
load Windows-only MSBuild assemblies. Neither runs on Linux. Everything else in `Radha.App` is
ordinary C# that can be compiled anywhere, so CI proves as much as it can off-Windows:

```bash
# tier 1 — static audit: x:Class, handlers, resource keys, binding roots/paths, namespaces
dotnet run --project tools/XamlAudit -c Release

# tier 2 — type-check all App C# against Microsoft.WinUI.dll + WinRT projections
dotnet build tools/TypeCheck/Radha.App.TypeCheck.csproj -c Release -p:Platform=x64
```

Tier 2 needs no Windows SDK and no XAML compiler: `tools/TypeCheck/generate-stubs.py` reads the
markup and emits an `InitializeComponent()` plus one field per `x:Name`, typed from the element, so
the real `csc` sees exactly the member surface the XAML compiler would have produced. The generated
files land in `tools/TypeCheck/generated/` (git-ignored) automatically during the build.

`MSYS`/`WSL` note: because the PRI and MSIX targets are switched off in that project, it produces a
library — it is a *type check*, not a runnable app. Only the `winui` CI job (or a Windows machine)
can build the shell.

### Cross-compiling `Radha.Native` on Linux

Any LLVM-MinGW toolchain works and links against the same import libraries MSVC uses:

```bash
x86_64-w64-mingw32-clang++ -std=c++20 -O2 -Wall -Wextra -Werror \
  -DUNICODE -D_UNICODE -DWIN32_LEAN_AND_MEAN -DNOMINMAX -DRADHANATIVE_EXPORTS -Iinclude \
  -shared src/*.cpp src/internal/*/*.cpp \
  -o RadhaNative.dll -luser32 -lshell32 -ldwmapi -lole32 -lshcore
```

### Compiling the shaders on Linux

```bash
LD_LIBRARY_PATH=<dxc-linux>/lib <dxc-linux>/bin/dxc -T ps_6_0 -E main -Fo Blur.dxil src/Blur.hlsl
```

The Windows job uses `fxc /T ps_5_0`; the source compiles under both without changes.

## Windows App SDK 1.8 or newer — a hard requirement

`Microsoft.WindowsAppSDK` is pinned to **1.8.x**. Do not drop back to 1.6 or 1.7: on those
versions `XamlCompiler.exe` writes its diagnostics only on success
([microsoft-ui-xaml#9813](https://github.com/microsoft/microsoft-ui-xaml/issues/9813),
[#10027](https://github.com/microsoft/microsoft-ui-xaml/issues/10027)), so any XAML error
surfaces as a bare `MSB3073 ... exited with code 1` with no file, line, or message — and no
`output.json`. The fix landed in **1.8.8** (and 2.1.3 on the 2.x line). 1.8 is also the first line
with first-class .NET 9 support, which this project targets.

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
Microsoft.WindowsAppSDK        1.8.260921001
Microsoft.Windows.SDK.BuildTools 10.0.26100.4654
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

Output: `src/Radha.App/bin/x64/Release/net9.0-windows10.0.19041.0/win-x64/RadhaShell.exe`

`WindowsAppSDKSelfContained=true` is set, so the Windows App SDK runtime is copied next to the
app — no machine-wide install needed. If you prefer framework-dependent deployment, set it to
`false` and install the Windows App Runtime 1.6 redistributable.

## First run

```powershell
.\build\RadhaShell.exe        # bar appears at the top of the primary display
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
| `WindowsAppSDK` restore fails | update the version to the latest 1.8.x on nuget.org |
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

## Shipping one executable

`dotnet publish` produces a folder by default. The release pipeline asks for the single-file form:

```pwsh
dotnet publish src/Radha.App/Radha.App.csproj -c Release -p:Platform=x64 -r win-x64 `
  --self-contained true -p:WindowsAppSDKSelfContained=true -p:RadhaSingleFile=true -o publish/exe
```

That yields `RadhaShell.exe` and nothing else: the .NET runtime and the Windows App SDK are inside
the bundle, and WinRT content is unpacked to `%TEMP%\.net\RadhaShell\…` on first launch (that is
what `IncludeAllContentForSelfExtract` buys — WinRT activation resolves file paths, not bundle
offsets).

Because that is a ~310 MB self-extracting binary (the .NET runtime plus the whole Windows App SDK), it is **not** the default for local iteration.
Use it only when you are building something to hand to someone.

### Verifying a build without a desktop

The executable carries its own headless smoke test:

```pwsh
.\publish\exe\RadhaShell.exe --selftest --out "$env:TEMP\selftest.json"
```

Exit code 0 means the bundle is intact: the runtime loaded, the Windows App SDK projection resolved,
and the Core contracts (command catalog, calculator, config, theme, launch guard, IPC endpoint) all
hold. The release workflow runs exactly this against the published binary and refuses to publish if
it fails. It never creates a window, so it is valid on a session-0 CI runner.
