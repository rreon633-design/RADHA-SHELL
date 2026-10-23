# ADR 0003 — What CI is allowed to claim

**Status:** accepted · **Date:** 2026-10-23

## Context

GitHub's Windows runners execute in **session 0**, with no Explorer shell, no interactive desktop
and no DWM composition for our windows. A WinUI shell lives entirely in that missing environment.

We still want every push to prove as much as it honestly can.

## Decision

Split verification into four tiers and be explicit about each:

| Tier | Runs on | Proves |
|---|---|---|
| unit + property tests | Linux | every rule that is not Win32 |
| XAML audit + WinUI type-check | Linux | the markup and the C# compile against genuine WinUI metadata |
| MSVC / HLSL / MSIX manifest | Windows runner | the Windows-only toolchain accepts the code |
| native smoke (advisory) | Windows runner | the DLL loads and its ABI is safe on the unsupported path |

Anything that needs a real desktop — tray callbacks, hotkey delivery, window placement, DWM
behaviour — is **not** claimed by CI. It ships with a documented manual checklist instead.

## Consequences

- We do not pretend a green run means the shell renders correctly.
- The advisory smoke test never blocks a merge, because a session-0 failure says nothing about a
  user's machine.
- Bugs that only appear on a real desktop are found by the manual checklist, and each one gets a
  regression note in `docs/19-problematic-areas.md`.
