# 11. API & IPC Layer

## 11.1 Transport

| Platform | Endpoint | Permissions |
|---|---|---|
| Windows | named pipe `\\.\pipe\radha-shell` | pipe ACL, current user |
| Linux/macOS (tests, dev) | `<tmp>/radha/radha-shell.sock` | `chmod 0600` (verified in tests) |

Framing: **4-byte little-endian length + UTF-8 JSON**. Maximum frame 4 MiB. A frame whose length
is ≤ 0 or > the ceiling terminates that connection only.

## 11.2 Messages

```jsonc
// request
{ "id": "a1b2c3d4", "module": "dashboard", "command": "toggle", "args": [] }

// success
{ "id": "a1b2c3d4", "ok": true, "payload": "ok", "error": null, "ms": 0.42 }

// failure
{ "id": "a1b2c3d4", "ok": false, "payload": null, "error": "unknown command 'explode' for theme", "ms": 0.11 }
```

`id` is opaque; the server echoes it. `ms` is server-side handling time, useful for latency
diagnostics from scripts.

## 11.3 Request lifecycle

1. **Rate limit** — token bucket per client (60 burst, 20/s). Rejection: `rate limit exceeded — slow down`.
2. **Validate** — `InputGuard.ValidateRequest`: module/command charset and length, ≤ 16 args,
   ≤ 512 chars each, no control characters, no shell metacharacters.
3. **Catalog check** — `CommandCatalog.IsValid(module, command)`.
4. **Handle** — `ShellHost.Execute`, always on the UI thread via `DispatcherQueue`.
5. **Audit** — outcome (`Accepted`/`Rejected`/`Throttled`/`Failed`) written to
   `%APPDATA%\Radha\logs\ipc-audit.log` with redaction.
6. **Respond** — exceptions become `ok:false` with a **redacted** message.

## 11.4 Error model

| Class | Example | Client action |
|---|---|---|
| Validation | `argument 0 contains a forbidden character` | fix input |
| Unknown module/command | `unknown command 'explode' for theme` | check `radha help` |
| Rate limit | `rate limit exceeded — slow down` | back off |
| Handler failure | redacted exception text | inspect daemon log |
| Transport | connection closed / `not running` | start `Radha.App` |

The CLI maps these to exit codes: `0` ok, `1` usage error, `2` daemon not running, `3` command
failed.

## 11.5 Command catalog

Single source of truth: `src/Radha.Core/Modules/CommandCatalog.cs`.

```text
bar            show | hide | toggle | reload
dashboard      open | close | toggle | tab
launcher       open | close | toggle | query
controlcenter  open | close | toggle
notifications  open | close | toggle | clear | dnd-on | dnd-off | dnd-toggle
osd            volume <0-100> | brightness <0-100> | mic | show
session        menu | logout | suspend | reboot | shutdown
media          play-pause | next | previous | seek <s> | mute | status
wallpaper      set <path> | next | random | list | current
theme          set <#hex> | variant <v> | dark | light | toggle | status | extract
network        status | wifi-toggle | vpn-toggle
bluetooth      status | toggle
system         status | volume <0-100> | brightness <0-100> | uptime
config         get | set <path> <value> | path | reload | monitor-override <monitor> <json>
```

Offline CLI-only commands: `scheme extract`, `bench`, `help`.

## 11.6 Examples

```bash
radha dashboard toggle
radha theme variant vibrant
radha theme set "#7C5CFF"
radha wallpaper next
radha media status            # → {"title":"…","artist":"…","state":"Playing","position":41.2}
radha system status           # → {"cpu":12.4,"gpu":3.1,"memoryUsedGb":9.8,…}
radha config set bar.height 48
radha config monitor-override "DELL U2723QE" '{"bar":{"height":52}}'
radha scheme extract photo.png --variant vibrant --json
radha bench --gate
```

## 11.7 Client library

`IpcClient` (single request), `IpcClientPool` (reused connection with one automatic reconnect on
daemon restart). Both are in `Radha.Core.Ipc` and usable from any .NET app.

## 11.8 Security properties (tested)

- oversized frame does not kill the daemon,
- garbage JSON drops only that connection,
- handler exceptions are returned redacted, never logged raw,
- rate limiting is per client id, and new connections get a fresh budget,
- socket is not group/world readable,
- shell-metacharacter module names (`dashboard; rm -rf /`) are rejected before execution.
