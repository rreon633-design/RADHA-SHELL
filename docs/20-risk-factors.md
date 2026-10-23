# 20. Risk Factors

Likelihood: L/M/H · Impact: L/M/H · Score = rough product.

| # | Risk | Likelihood | Impact | Score | Mitigation | Status |
|---|---|---|---|---|---|---|
| R1 | WinUI code fails to compile on first Windows build | H | M | H | `winui` CI job exists; core logic split out and tested; defensive XAML | 🟡 open |
| R2 | Virtual desktop COM breaks across Windows builds | H | M | H | per-build vtable table, IID-identified, probe-validated with GetCount; unverified builds report unsupported; documented MoveWindowToDesktop for moves; `RadhaNativeSmoke` runs on every push | 🟢 mitigated |
| R3 | System toast listener needs MSIX identity | H | L | M | notification centre works without it; documented as ⬜ | 🟡 open |
| R4 | Sensor coverage gaps on laptops | M | L | L | provider label + graceful zeros | 🟡 open |
| R5 | App-bar leaves dead strip / mispositions | M | M | M | native isolation + fallback path; verify on multi-monitor | 🟡 open |
| R6 | Acrylic unavailable (battery saver, remote session) | M | L | L | opaque tonal fallback | ✅ mitigated |
| R7 | Package version drift (WindowsAppSDK, Win2D) | M | M | M | pinned versions, Dependabot PRs, CI | ✅ mitigated |
| R8 | Security regression in IPC | L | H | M | validation + rate limit + tests + CI secret scan | ✅ mitigated |
| R9 | Secret committed by a contributor | M | H | H | CI secret scan on every push | ✅ mitigated |
| R10 | Performance regression in ranking/palette | M | M | M | `bench --gate` in CI | ✅ mitigated |
| R11 | Config corruption on crash | L | M | L | atomic save + `.broken` fallback | ✅ mitigated |
| R12 | Scope creep into Explorer replacement / tiling | M | H | M | explicit non-goals in docs and CI invariants | ✅ mitigated |
| R13 | Lock-screen feature requested later | L | M | L | CI gate would fail; needs an explicit product decision | ✅ mitigated |
| R14 | Maintainer bandwidth | M | H | H | full docs set, single-command verification, CI gate | 🟡 open |
| R15 | Windows API deprecation (SMTC/Composition) | L | M | L | interfaces isolate OS calls | ✅ mitigated |
| R16 | Unsigned binaries flagged by SmartScreen | H | L | M | signed MSIX in phase 3; release notes state unsigned | 🟡 open |

## Top three risks and what happens if they materialise

1. **R1 (WinUI build)** — if the first Windows build fails badly, the shell UI slips a phase while
   the engine, CLI and docs remain usable. No architecture change required; patches are local.
2. **R14 (bandwidth)** — the mitigation is disclosure: every claim in the README carries an honest
   status, so the project is never in the position of pretending something works.
3. **R2 (virtual desktops)** — the bar degrades to local workspace pills; nothing else depends on the
   COM interface.

## Risk review cadence

Re-read this register at the end of each phase; promote 🟡 to ✅ only when a CI job or a verified
manual test proves the mitigation.
