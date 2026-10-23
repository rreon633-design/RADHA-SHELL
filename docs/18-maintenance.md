# 18. Maintenance

## 18.1 Versioning

Semantic versioning: `MAJOR.MINOR.PATCH`.

| Change | Version bump |
|---|---|
| Breaking config key rename/removal | MAJOR |
| New module/command, new feature | MINOR |
| Fix, docs, perf | PATCH |

Tags are `v*.*.*`; a tag triggers `.github/workflows/release.yml`, which runs the test suite, builds
the shell + CLI + native bridge + shaders and attaches `RadhaShell-win-x64.zip` to a GitHub Release.

## 18.2 Release checklist

1. `CHANGELOG.md` has an entry under the new version.
2. All 8 CI jobs green on `main`.
3. Tag: `git tag v0.2.0 && git push origin v0.2.0`.
4. Verify the release workflow produced the archive and that it launches on a clean VM.
5. Update `README.md` status table if a 🟡 became ✅.

## 18.3 Configuration migrations

Config is additive by design: unknown keys are preserved on load, missing keys fall back to
defaults. A breaking change requires:

1. a migration method in `ConfigStore` (`Migrate(json, fromVersion, toVersion)`),
2. a `configVersion` field added to `shell.json`,
3. an entry in `CHANGELOG.md` and this document.

Until phase 3 there is no `configVersion` field; the schema is still pre-1.0.

## 18.4 Dependency updates

- Dependabot is configured for `nuget` and `github-actions` (weekly).
- A package update requires: CI green, `bench --gate` green, no new transitive packages in `Core`.
- WindowsAppSDK upgrades are tested against the oldest supported Windows 10 build.

## 18.5 Observability

| Signal | Where |
|---|---|
| IPC audit trail | `%APPDATA%\Radha\logs\ipc-audit.log` (redacted, tab-separated) |
| Benchmarks | CI artifact `benchmarks` (txt + json), 14-day retention |
| Build artifacts | `Radha.App-win-x64`, `RadhaNative-win-x64`, `shaders-cso` |
| Debug output | `System.Diagnostics.Debug` in the WinUI app (DebugView) |

Structured rotating logs arrive in phase 3.

## 18.6 Issue triage

| Label | Meaning | SLA (best effort) |
|---|---|---|
| `bug` | reproducible defect | triage within 7 days |
| `security` | vulnerability | private handling, see [SECURITY.md](../SECURITY.md) |
| `winui-unverified` | needs a Windows build to confirm | depends on a Windows runner |
| `good first issue` | scoped, with a pointer to the file | — |
| `needs-design` | UX decision required | — |

## 18.7 Deprecation

Announce in `CHANGELOG.md` and docs, keep the old path working for one MINOR release, remove in the
next MAJOR. CLI aliases are preferred over removals.

## 18.8 Bus factor & continuity

The project is intentionally documented so a new maintainer can continue: architecture, threat
model, benchmarks, build steps and the verification ladder are all in `docs/`. The CI gate is the
contract.
