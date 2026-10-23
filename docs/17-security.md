# 17. Security

Security posture: **local-only, least privilege, deny by default, validate at every boundary.**

## 17.1 Threat model

| # | Threat | Actor | Impact | Control |
|---|---|---|---|---|
| T1 | Malicious local process drives the shell | other local user / malware | open panels, change settings | owner-only socket/pipe, per-client rate limit, catalog validation |
| T2 | Command injection through arguments | script author | arbitrary execution | no shell invocation anywhere; `InputGuard` rejects metacharacters; `Process.Start` uses `UseShellExecute` with fixed verbs only |
| T3 | Path traversal reading/writing outside config | IPC caller | file disclosure/overwrite | `PathGuard.IsWithinRoot`, last-segment sanitisation, explicit containment check on writes |
| T4 | Secret leakage into logs | developer/user | credential exposure | `Redactor` on every error string and audit record; CI secret scan |
| T5 | Memory exhaustion via IPC | local process | DoS | 4 MiB frame ceiling, max 16 args × 512 chars, connection dropped on violation |
| T6 | Malformed frame crashing the daemon | local process | DoS | JSON/format exceptions contained per connection (tested) |
| T7 | Config tampering to execute code | local user | self-inflicted | config has no code paths; `config set` only writes typed properties |
| T8 | Supply-chain compromise | package | RCE | `Radha.Core` has zero packages (CI-enforced); all packages pinned |
| T9 | Office/lock-screen credential capture | n/a | — | **no lock screen, no credential provider, no credentials handled at all** |
| T10 | Wallpaper decoder exploit | malformed image | memory corruption | Core decoders are managed and bounds-checked; WIC used for exotic formats |

## 17.2 Trust boundaries

```
untrusted:  CLI args · IPC payloads · config file · wallpaper file · network JSON (weather)
trusted:    in-process view models and services
```

Everything in the "untrusted" row passes through `InputGuard`, `PathGuard` or a bounds-checked
decoder before it is used.

## 17.3 Controls implemented (with tests)

| Control | Implementation | Test |
|---|---|---|
| Request validation | `InputGuard.ValidateRequest` | 11 assertions |
| Config key validation | `InputGuard.ValidateConfigKey` | 3 |
| Numeric parsing (NaN/∞) | `InputGuard.TryParseNumber/Int` | 6 |
| Colour parsing | `InputGuard.IsValidHexColor` | 5 |
| Path containment | `PathGuard.IsWithinRoot` | 3 |
| Filename sanitising | `PathGuard.SafeFileName` | 5 |
| Executable extension checks | `PathGuard.LooksExecutable` | 2 |
| Device-path rejection | `PathGuard.IsSafeLocalPath` | 2 |
| Secret redaction | `Redactor` | 7 |
| Rate limiting | `RateLimiter` | 6 |
| Socket permissions | `File.SetUnixFileMode(0600)` | 1 |
| Frame ceiling / malformed frames | `IpcFraming` | 2 |
| Redacted errors | `IpcServer` handler catch | 3 |
| Audit trail | `IpcServerOptions.Audit` | 4 |
| Config override guards | `ConfigStore.SaveMonitorOverride` | 4 |
| No lock screen | CI grep gate | 1 job |

## 17.4 Privileges

- Runs as the current user, **no elevation**, no service, no driver.
- No admin-only APIs are called. `SHAppBarMessage` and `DwmSetWindowAttribute` work unelevated.
- Window style uses `WS_EX_NOACTIVATE` for overlays so the shell cannot steal input focus.

## 17.5 Data handling

| Data | Where | Retention |
|---|---|---|
| Configuration | `%APPDATA%\Radha\shell.json` | until user deletes |
| Palette cache | memory only (LRU) | process lifetime |
| IPC audit | `%APPDATA%\Radha\logs\ipc-audit.log` | append-only, user-managed |
| Weather location | in memory; optional `ipapi.co` lookup once | not persisted |
| Media metadata | in memory (SMTC) | process lifetime |

No telemetry, no analytics, no remote logging.

## 17.6 Review checklist

- [ ] every new IPC argument is validated and length-bounded
- [ ] every new file path goes through `PathGuard`
- [ ] no `Process.Start` with user-controlled arguments
- [ ] no secret or token can reach a log or an error string
- [ ] no new NuGet package in `Core`
- [ ] no lock-screen / credential code
- [ ] new failure mode has a test

## 17.7 Reporting a vulnerability

See [SECURITY.md](../SECURITY.md). Do not open a public issue for a vulnerability; email the
maintainer listed there.

## 17.8 Known limitations

- Windows named-pipe ACL is left at the default (current user) and is not custom-set; documented
  rather than silently assumed.
- The audit log is not rotated automatically.
- Weather/lyrics requests are made without certificate pinning (standard TLS validation only).
- No code signing yet: releases are explicitly labelled unsigned development builds.
