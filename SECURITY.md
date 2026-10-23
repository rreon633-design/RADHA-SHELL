# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| `main` (pre-1.0) | ✅ security fixes |
| tagged releases < 1.0 | ✅ best effort |

## Reporting a vulnerability

**Do not open a public issue.** Email the maintainer:

- **Email:** rreon633@gmail.com
- **Subject:** `[RADHA SHELL security] <short summary>`

Include: affected component, reproduction steps, impact, and whether the finding requires local
access. You will get an acknowledgement within **72 hours** and a remediation plan within **7 days**
for confirmed issues.

Please do not test against other people's machines, and do not include real credentials in reports.

## Scope

In scope:
- command injection through IPC/CLI arguments
- path traversal in config, monitor overrides, wallpaper handling
- denial of service against the daemon (frame flooding, crash via malformed input)
- secret leakage into logs, errors, or audit records
- privilege issues (the shell must never request elevation)

Out of scope:
- issues requiring a compromised administrator account
- SmartScreen warnings on unsigned development builds
- the absence of code signing (tracked for phase 3)
- **lock screen / authentication** — no such feature exists; reports about missing lock-screen
  hardening are not applicable

## Security design (summary)

| Control | Implementation |
|---|---|
| Boundary validation | `Radha.Core/Security/InputGuard.cs` |
| Path containment | `PathGuard` (root containment, segment sanitising, device-path rejection) |
| Secret redaction | `Redactor` (GitHub PATs, `sk-` keys, bearer tokens, `api_key=` assignments) |
| Rate limiting | `RateLimiter` (token bucket per client) |
| IPC framing limits | 4 MiB ceiling, strict length validation |
| Socket permissions | `0600` on Unix sockets |
| Audit trail | append-only, redacted, `%APPDATA%\Radha\logs\ipc-audit.log` |
| Supply chain | `Radha.Core` has zero packages (CI-enforced); all packages pinned |
| CI gates | secret scan, vulnerability scan, no-binary, file-size ceiling |

Details and the threat model: [docs/17-security.md](docs/17-security.md).

## Disclosure policy

We follow coordinated disclosure: a fix is prepared and released, and the report is credited (with
permission) in `CHANGELOG.md`. Please allow up to 90 days before public disclosure.
