# 14. Development Process

## 14.1 Golden rules

1. **Platform-neutral logic goes in `Radha.Core`.** If it can be tested on Linux, it must be.
2. **One catalog.** Never add a UI action without adding its command to `CommandCatalog`.
3. **Validate at the boundary.** Anything arriving from IPC, CLI or a file is untrusted.
4. **No new NuGet package in `Core`.** CI fails if `PackageReference` appears there.
5. **No web technology in `src/`.** CI fails on `.html/.css/.js/package.json`.
6. **No lock-screen code.** CI greps for credential-provider/biometric terms.
7. **Budgets are contracts.** If a change pushes p95 over budget, fix it or change the budget in a
   reviewed commit.

## 14.2 Verification ladder

| Step | Command | Runs where |
|---|---|---|
| Build | `dotnet build src/Radha.Tests/Radha.Tests.csproj -c Release` | any OS |
| Tests | `dotnet run --project src/Radha.Tests -c Release` | any OS |
| Benchmarks | `dotnet run --project src/Radha.Tests -c Release -- bench --gate` | any OS |
| Invariants | the `invariants` CI job | any OS |
| WinUI build | `dotnet build src/Radha.App/Radha.App.csproj -p:Platform=x64` | Windows |
| Native build | `cmake --build build --config Release` | Windows |
| Shaders | `fxc /T ps_5_0 /E main` | Windows |

Run the first four locally before opening a PR; CI runs all seven.

## 14.3 Adding a feature

```
1. Contract      Radha.Core/Services/Contracts.cs        (interface + DTO if new data)
2. Logic         Radha.Core/...                          (pure, testable)
3. Tests         Radha.Tests/...                          (happy path + hostile input)
4. Command        Modules/CommandCatalog.cs                (module/command + help text)
5. Service       Radha.App/Services/Win/...               (Windows implementation)
6. View model    Radha.App/ViewModels/...                 (observable state, theme brushes)
7. View          Radha.App/Views/*.xaml                    (layout, no logic)
8. Docs          08-features.md status + CHANGELOG entry
```

## 14.4 Style

- File-scoped namespaces, `_camelCase` private fields, `PascalCase` members (see `.editorconfig`).
- Async methods end in `Async`; `Task`-returning where I/O is involved.
- No `async void` except event handlers, and those must not throw.
- Records for DTOs; classes only for mutable state.
- Comments explain *why*, never *what*.
- JSON/YAML/XML indented with 2 spaces; C# with 4.

## 14.5 Commit convention

```
type(scope): imperative summary

Body explaining motivation and tradeoffs.
```

Types: `feat`, `fix`, `perf`, `test`, `docs`, `refactor`, `build`, `ci`, `chore`.

## 14.6 Definition of done (PR)

- [ ] builds with `-p:TreatWarningsAsErrors=true` on the core projects
- [ ] new logic has tests; hostile input has a test that asserts rejection
- [ ] `bench --gate` passes
- [ ] docs status table updated
- [ ] no secret, no build output, no file > 2 MiB added
- [ ] CI green (all 8 jobs)

## 14.7 Branching & releases

`main` is always releasable. Work happens on short-lived branches; PRs require a green CI gate.
Releases are tag-driven (`v*.*.*`) — see [18-maintenance.md](18-maintenance.md).

## 14.8 Reviewing for security

Every PR that touches `Ipc/`, `Security/`, `Config/` or any `Path` handling gets an explicit review
against the checklist in [17-security.md](17-security.md#176-review-checklist).
