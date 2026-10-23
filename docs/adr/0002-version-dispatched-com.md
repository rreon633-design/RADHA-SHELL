# ADR 0002 — Version-dispatched COM instead of one hard-coded vtable

**Status:** accepted · **Date:** 2026-10-23

## Context

Virtual-desktop switching has no public API for changing desktop. The shell exposes
`IVirtualDesktopManagerInternal`, whose vtable layout changes between Windows releases, and whose
interface IID changes with it. Widely copied snippets hard-code one layout and break on the next
feature update.

## Decision

Dispatch on the OS build at runtime, with three verified layouts (10240–19045, 22000–22635,
26100+) and a documented fallback to the *public* `IVirtualDesktopManager` for window moves. A build
that matches no layout reports "unsupported" and leaves the UI in its labelled fallback state.

## Consequences

- Adding a Windows release means adding a layout table row, not rewriting the feature.
- The failure mode is a clear "not supported on this build" rather than a crash or, worse, a call
  through a wrong function pointer.
- Every exported function validates its arguments before touching a vtable: a wrong index is a
  memory-safety bug, and this is the one place in the project where that is possible.
