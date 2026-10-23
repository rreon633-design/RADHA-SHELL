# 23. Success Metrics

## 23.1 Engineering metrics (measurable now)

| Metric | Target | Current | How measured |
|---|---|---|---|
| Core test assertions | ≥ 200 | **235** | `dotnet run --project src/Radha.Tests` |
| Test failures | 0 | **0** | same |
| Colour-role contrast | ≥ 4.5:1 for all pairs, 7 variants × 2 modes | pass | `MaterialScheme.HasReadableContrast` |
| Palette extraction p95 | < 120 ms (512²) | **~5.4 ms** | `radha bench --gate` |
| Scheme generation p95 | < 5 ms | **~0.12 ms** | same |
| Launcher ranking p95 (5 000 apps) | < 40 ms | **~1.0 ms** | same |
| Config round-trip p95 | < 25 ms | **~0.15 ms** | same |
| IPC round-trip p95 | < 15 ms | **~0.08 ms** | same |
| NuGet dependencies in `Radha.Core` | 0 | **0** | CI check |
| Web artefacts in `src/` | 0 | **0** | CI invariant |
| Lock-screen code in `src/` | 0 | **0** | CI invariant |
| Secrets committed | 0 | **0** | CI secret scan |

## 23.2 Product metrics (targets for phase 2+)

| Metric | Target | Instrumentation |
|---|---|---|
| Cold start to visible bar | < 1.5 s | stopwatch in `ShellHost.StartAsync` (added phase 2) |
| Panel open latency | < 100 ms | composition timing |
| Idle CPU (bar only) | < 1 % | Task Manager / `dotnet-counters` |
| Idle working set | < 120 MB | Task Manager |
| Dropped frames during panel animation | 0 | WPR composition profile |
| Theme apply → visual update | < 250 ms perceived | manual + composition timing |

## 23.3 Adoption metrics (once released)

| Metric | Target | Source |
|---|---|---|
| Release downloads (30 days) | 500 | GitHub Releases |
| CI pass rate on `main` | > 95 % | Actions |
| Time to first green PR | < 30 min | Actions |
| Issues triaged < 7 days | 100 % | GitHub |
| Contributors beyond the author | ≥ 3 | GitHub |

## 23.4 Quality metrics

| Metric | Target |
|---|---|
| Regressions caught by `bench --gate` | all p95 regressions |
| Bugs found by tests vs reported | ratio tracked; tests should dominate |
| Docs freshness | feature status updated in the same PR that changes it |
| Escaped secrets | 0 |

## 23.5 What "success" is *not*

- Stars, followers or hype.
- Number of features. A feature marked 🟡 that never compiles is worth less than a tested ⬜.
- Line count.

The honest measure: **can a stranger clone this repository, run one command, and get a green
suite; then build on Windows and get a working bar?** When both are true, phase 1 is complete.
