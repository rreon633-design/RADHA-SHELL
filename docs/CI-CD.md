# CI/CD — how a commit becomes a released build

Nothing in this pipeline needs a human. A push starts it, a green run ships it.

```
git push origin main
      │
      ▼
┌──────────────────────────────────────────────────────────────┐
│ ci.yml · triggered by: push → main, pull_request → main,      │
│                       workflow_dispatch                       │
├──────────────────────────────────────────────────────────────┤
│  ubuntu (fast, ~1 min)          windows (~3–5 min)            │
│  ├ core        build + 235 assertions                         │
│  ├ invariants  no HTML/CSS/JS, XML + JSON well-formed         │
│  ├ security    secrets, NuGet CVEs, tracked binaries, size    │
│  ├ performance p95 latency budgets on the palette engine      │
│  ├ docs        PRD docs present, links resolve, no lock screen│
│  ├ xaml-audit  handlers / resources / bindings resolve        │
│  ├ app-typecheck  all App .cs compile against WinUI refs      │
│  ├ winui       XAML → C#, MSIX packaging                      │
│  ├ native      C++/WinRT bridge under MSVC                    │
│  └ shaders     HLSL → .cso                                    │
│                          │                                    │
│                     ci-gate (required check)                  │
└──────────────────────────┼───────────────────────────────────┘
                           │  green + branch = main
                           ▼
┌──────────────────────────────────────────────────────────────┐
│ release.yml · triggered by: workflow_run(ci) on main,         │
│                             tag push v*.*.*, workflow_dispatch│
├──────────────────────────────────────────────────────────────┤
│  re-runs the test suite, publishes the shell (self-contained) │
│  + CLI + native bridge + shaders, zips, and publishes a       │
│  GitHub Release:                                              │
│    v0.0.0-dev.<ci run number>   rolling, marked prerelease    │
│    v1.2.3                       from a tag, real release      │
└──────────────────────────────────────────────────────────────┘
```

## Auto-triggering, and why it sometimes doesn't

Workflow runs are fired by GitHub, never by a local script. Two things suppress them, and neither
is a bug:

| Cause | Symptom | Fix |
|---|---|---|
| A push made with `GITHUB_TOKEN` inside a workflow | No new run (recursion guard) | expected; use a deploy key or a PAT if you need chaining |
| Push arrives but `paths-ignore` matches (`docs/**`, `**.md`) | ci intentionally skipped | touch a source file, or run *Actions → ci → Run workflow* |

Pushes made over SSH with a deploy key **do** trigger `ci` — verified in this repo: the runs on
commits `3471ecc` and `e81669f` were both `event=push`, actor `rreon633-design`, with no dispatch.

## What runs automatically

1. **push → ci.** Every push to `main`, every PR, no clicks.
2. **ci green → release.** `release.yml` listens on `workflow_run` and only proceeds when
   `conclusion == 'success'`. It checks out the exact commit CI built (`head_sha`), not the branch tip.
3. **tag → release.** `git tag v1.0.0 && git push origin v1.0.0` produces a non-prerelease release.

Failure of any gated job fails `ci-gate`, so a red CI never ships.

## Watching a run without a token

`tools/push.sh` pushes and then polls the public API until the run finishes:

```bash
tools/push.sh                 # push main + follow the CI run
tools/push.sh --tag v0.2.0    # push, tag, follow both workflows
tools/push.sh --no-watch      # push only
```

A plain `git push` is still all that's needed for the pipeline itself — the helper only prints
progress.

## Reading a failed Windows build

Job logs require repository admin, but two things are public:

- **annotations** — `ci.yml` pipes the Windows build through a parser that re-emits every compiler
  error as a `::error` annotation, and falls back to the XAML compiler's `output.json` when the
  failure is wrapped in `MSB3073`. Open the failed job and read the *Annotations* box.
- **artifacts** — `Radha.App-win-x64`, `RadhaNative-win-x64`, `shaders-cso`, `benchmarks`.

## Adding a gate

Add a job to `ci.yml`, then append it to `ci-gate.needs` **and** to the `results=( … )` array in the
gate step — otherwise the gate silently ignores it. Keep `ci-gate` as the only required check in
branch protection so new jobs inherit the gate for free.

## Required repository settings

| Setting | Value |
|---|---|
| Actions → General → Workflow permissions | *Read and write* (release needs `contents: write`) |
| Branch protection on `main` | require `ci · all checks` |
| Secrets | none — the pipeline uses the built-in `GITHUB_TOKEN` |
