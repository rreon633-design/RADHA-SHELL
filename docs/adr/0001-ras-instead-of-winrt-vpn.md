# ADR 0001 — VPN through RAS, not `Windows.Networking.Vpn`

**Status:** accepted · **Date:** 2026-10-23

## Context

Windows offers two ways to drive a VPN: the WinRT `Windows.Networking.Vpn` namespace, and the
Win32 Remote Access Service (`RasDial`, `RasEnumEntries`).

`Windows.Networking.Vpn` is the modern surface. It is also gated behind the **restricted**
`networkingVpnProvider` capability, which an unpackaged desktop application cannot hold and which
requires Microsoft approval for Store distribution. Using it would mean the feature only works in a
packaged build that most users will never install.

## Decision

Use RAS. `VpnService` enumerates the user's phonebook with `RasEnumEntriesW`, connects with
`RasDialW`, disconnects with `RasHangUpW`, and tracks live tunnels with `RasEnumConnectionsW`.

## Consequences

- Works unpackaged, with no capability, on every Windows version we support.
- We show the user's existing VPN profiles rather than inventing our own list. A machine with no
  configured VPN shows an empty state, which is honest.
- RAS does not expose per-profile protocol details or split-tunnelling configuration; we report the
  device type string and stop there.
- Credentials are never stored or passed by the shell; `RasDialW` is invoked with the entry name
  only, so Windows uses the saved credentials.
