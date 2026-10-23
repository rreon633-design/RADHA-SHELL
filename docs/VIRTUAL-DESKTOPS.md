# Virtual desktops — how RADHA SHELL switches them

## The problem

Windows ships a documented interface, `IVirtualDesktopManager`, and it is almost useless for a shell:

| Capability | `IVirtualDesktopManager` (documented) | `IVirtualDesktopManagerInternal` (undocumented) |
|---|---|---|
| Is this window on the current desktop? | ✅ `IsWindowOnCurrentVirtualDesktop` | ✅ |
| Which desktop owns this window? | ✅ `GetWindowDesktopId` | ✅ |
| Move a window to a desktop | ✅ `MoveWindowToDesktop(hwnd, guid)` | ✅ |
| How many desktops are there? | ❌ | ✅ |
| Which one is current? | ❌ | ✅ |
| Switch to a desktop | ❌ | ✅ |

Switching is the one thing the documented API cannot do. There is no supported alternative — the
only other route is synthesising `Win+Ctrl+←/→`, which requires already knowing where you are, so it
cannot implement "switch to desktop 4".

## What this repo does

`src/Radha.Native/src/Workspaces.cpp` talks to the shell's ImmersiveShell service provider:

```
CoCreateInstance(CLSID_ImmersiveShell, IID_IServiceProvider)
    └─ QueryService(SID_VirtualDesktopManagerInternal, IID_IVirtualDesktopManagerInternal)
           ├─ GetCount()        → how many desktops
           ├─ GetCurrentDesktop() → IVirtualDesktop, compared by GUID
           ├─ GetDesktops()     → IObjectArray of IVirtualDesktop
           └─ SwitchDesktop()   → switch
IVirtualDesktop::GetID(GUID)
    └─ + documented IVirtualDesktopManager::MoveWindowToDesktop(hwnd, guid)  → move a window
```

### The part that actually matters: it is version-dispatched

Both the IID **and** the vtable layout change between Windows releases. A single hard-coded vtable
does not survive a feature update, and calling the wrong slot faults *inside the shell process's
interface*, which is not something you can catch.

So the file carries a table, and each row is pinned to the builds it was verified against:

| Builds | `IID_IVirtualDesktopManagerInternal` | GetCount | GetCurrent | GetDesktops | Switch | First arg |
|---|---|---|---|---|---|---|
| 10240 – 19045 (Win10 1507–22H2) | `F31574D6-B682-4CDC-BD56-1827860ABEC6` | 3 | 6 | 7 | 9 | — |
| 22000 – 22635 (Win11 21H2–23H2) | `B2F925B9-5A0F-4D2E-9F4D-2B1507593C10` | 3 | 6 | 8 | 10 | `HWND`/`HMONITOR` |
| 26100+ (Win11 24H2/25H2) | `53F5CA0B-158F-4124-900C-057158060B27` | 3 | 6 | 7 | 9 | — |

*(slot numbers are absolute vtable indices; `IUnknown` occupies 0–2)*

Shared across all rows: `CLSID_ImmersiveShell = C2F03A33-21F5-47FA-B4BB-156362A2F239`,
`IID_IServiceProvider = 6D5140C1-7436-11CE-8034-00AA006009FA`,
`SID = C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B`, `IID_IVirtualDesktop = 3F07F4BE-B107-441A-AF0F-39D82529072C`,
`IID_IObjectArray = 92CA9DCD-5622-4BBA-A805-5E9F541BD8C9`.

Layouts verified against [Ciantic/VirtualDesktopAccessor](https://github.com/Ciantic/VirtualDesktopAccessor)
(MIT), [superj8888/VirtualDesktopTaskbar](https://github.com/superj8888/VirtualDesktopTaskbar) (verified
by its author on 26100.2605 and 26200), [mzomparelli/zVirtualDesktop](https://github.com/mzomparelli/zVirtualDesktop)
wiki for 22621, and mntone's `VirtualDesktop` for the Insider 20231/20241 shape.

### Three guards before a single call happens

1. **Build gate** — `RtlGetVersion` reads the real build number (not the shimmed
   `GetVersionEx`). No matching row → unsupported.
2. **IID gate** — query the service provider for that row's exact IID. A mismatch fails cleanly
   with `E_NOINTERFACE`, and a successful QI is what *identifies* which layout the object has.
3. **Value gate** — call `GetCount` and require `1 ≤ count ≤ 256`. Anything else closes the
   interface and falls back to unsupported.

At no point is a vtable slot guessed from a build range alone.

### Failure behaviour

| Situation | `RadhaWorkspaceBackend()` | Behaviour |
|---|---|---|
| Verified build, shell running | `1` | live switching |
| Unverified build (e.g. a future 27000) | `0` | count `0`, current `-1`, switch/move `FALSE` |
| Explorer restarted mid-session | — | first failing call drops the cached pointer, reopens once, then re-probes |
| `RadhaNative.dll` missing | — | `DllNotFoundException` is caught in `NativeBridge.TryProbe` |

The bar keeps rendering local placeholder pills in the unsupported case and
`WorkspaceService.BackendLabel` says so in words. It never reports a switch that did not happen.

## Verification

| Where | What it proves |
|---|---|
| `RadhaNativeSmoke.exe` (every `ci` push, windows-latest) | The DLL loads, all 16 exports resolve, and the virtual-desktop ABI is safe on **both** paths: live (count ≥ 1, current in range, every desktop reachable by index and returnable) and unsupported (all calls return the documented sentinel, no crash). Boundary calls `Switch(-1)`, `Switch(9999)`, `Move(-5)`, `Move(9999)` are asserted inert. |
| `cmake --build` on MSVC | The COM plumbing compiles under the MSVC toolchain, not just mingw. |
| `clang++ -Werror` on Linux | Cross-compiles clean, so the CI runner is not the first compiler to see it. |

GitHub's Windows runners are session-0 and have no Explorer, so `RadhaNativeSmoke` normally takes the
**unsupported** path there — it proves graceful degradation, not live switching. Live switching is
exercised by running the same executable on a desktop session; the assertions for that path are in
the binary and activate automatically because `RadhaWorkspaceBackend()` returns 1 there.

## Known limits

- **Window counts per desktop** would need `IApplicationViewCollection` + `IVirtualDesktopPinnedApps`
  to enumerate views. Not wired; `WorkspaceInfo.WindowCount` is `0` on live desktops.
- **Desktop names** live outside any documented API. The bar numbers them `Workspace N`.
- **Creating / removing desktops** exists in the same interface (`CreateDesktop`, `RemoveDesktop`)
  but is intentionally not exposed — the product only switches.
