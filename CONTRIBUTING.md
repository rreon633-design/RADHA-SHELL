# Contributing to RADHA SHELL

Thanks for helping. This project has an unusually strict quality bar for a desktop shell — please
read this before opening a PR.

## Ground rules

1. **Native only.** No HTML, CSS, JavaScript, Electron, Tauri or WebView2 in `src/`. CI fails on it.
2. **No lock-screen code.** Lock screen, credential providers and biometric auth are out of scope.
   CI greps for them.
3. **`Radha.Core` stays dependency-free.** No `PackageReference` in that project.
4. **One command catalog.** A new UI action needs a matching entry in
   `src/Radha.Core/Modules/CommandCatalog.cs`.
5. **Validate at the boundary.** Anything from IPC, CLI, config or a file is untrusted.
6. **Budgets are contracts.** `radha bench --gate` must pass.

## Before you open a PR

```bash
dotnet build src/Radha.Tests/Radha.Tests.csproj -c Release
dotnet run   --project src/Radha.Tests -c Release           # expect: 235 passed, 0 failed
dotnet run   --project src/Radha.Tests -c Release -- bench --gate
```

On Windows, additionally:

```powershell
dotnet build src/Radha.App/Radha.App.csproj -c Release -p:Platform=x64
cmake -B build -S src/Radha.Native -A x64 ; cmake --build build --config Release
```

## Adding a feature

Follow the ladder in [docs/14-development-process.md](docs/14-development-process.md#143-adding-a-feature):
contract → logic → tests → command → service → view model → view → docs.

## Tests

- Tests live in `src/Radha.Tests` (a console harness, no NuGet).
- Every new branch of logic needs an assertion; every new rejection path needs a hostile-input test.
- Prefer deterministic fixtures in `src/Radha.Tests/fixtures`.

## Style

`.editorconfig` is authoritative: file-scoped namespaces, 4-space C#, 2-space JSON/XAML/YAML,
`_camelCase` private fields. Comments explain *why*.

## Commits

```
type(scope): imperative summary
```

`feat`, `fix`, `perf`, `test`, `docs`, `refactor`, `build`, `ci`, `chore`.

## PR checklist

- [ ] builds with `-p:TreatWarningsAsErrors=true`
- [ ] tests added for new logic and for new rejection paths
- [ ] `bench --gate` passes
- [ ] `docs/08-features.md` status updated if behaviour changed
- [ ] `CHANGELOG.md` entry under *Unreleased*
- [ ] no secrets, no build output, no file > 2 MiB in the diff
- [ ] CI green (all 8 jobs)

## Reporting bugs

Use the issue template: what you ran, what you expected, what happened, `radha system status`
output, and the relevant lines from `%APPDATA%\Radha\logs\ipc-audit.log` (it is redacted).
