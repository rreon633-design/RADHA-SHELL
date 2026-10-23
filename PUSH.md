# Pushing this repository

The code in this workspace is committed on `main` with author
`rreon633-design <rreon633@gmail.com>`. Pushing over HTTPS failed with
**403 "Permission denied"** because the token used has no write access to
`rreon633-design/RADHA-SHELL`.

## Fix the token (30 seconds)

1. Open <https://github.com/settings/personal-access-tokens>
2. Edit the token (or create a new fine-grained token).
3. **Repository access** → *Only select repositories* → **RADHA-SHELL**
   (an empty selection means the token can see no repositories — this is the usual cause)
4. **Repository permissions**:
   - **Contents: Read and write** ← required to push commits
   - **Workflows: Read and write** ← required because the commit adds `.github/workflows/`
   - **Metadata: Read** (granted automatically)
5. Save, copy the `github_pat_…` value.

A classic token with the `repo` + `workflow` scopes also works.

## Push

```bash
cd radha-shell
git remote set-url origin https://github.com/rreon633-design/RADHA-SHELL.git

# option A — interactive (recommended)
git push -u origin main
#   username: rreon633-design
#   password: <your PAT>

# option B — non-interactive
git push -u origin main \
  https://rreon633-design:<PAT>@github.com/rreon633-design/RADHA-SHELL.git
```

Then watch the pipeline:

```bash
gh run watch            # or: open https://github.com/rreon633-design/RADHA-SHELL/actions
```

## ⚠️ Deploy-key pushes do not trigger workflows

GitHub does not start workflow runs for pushes authenticated with a **deploy key**. The code lands,
but the pipeline stays idle. Three ways to get CI running:

| Option | Steps |
|---|---|
| **A. One click (fastest)** | Actions → *ci* → **Run workflow** → branch `main` → **Run workflow** |
| **B. Push a commit yourself** | any push from your account (web editor or `git push`) triggers `ci` automatically |
| **C. Enable write on the token** | set Contents → *Read and write* on the PAT, then re-push (PAT pushes do trigger workflows) |

Verify: `gh run list -R rreon633-design/RADHA-SHELL` or
<https://github.com/rreon633-design/RADHA-SHELL/actions>

## Expected first run

`core`, `invariants`, `security`, `performance` and `docs` should go green immediately
(they were executed locally: 235/235 tests, benchmarks within p95 budgets, 0 secrets, 0 broken
links). `winui`, `native` and `shaders` are the Windows jobs and may need small fixes on their
first run — that is their purpose.

## If you would rather push from your own machine

```bash
git clone /path/to/radha-shell radha-shell-local   # or download the workspace folder
cd radha-shell-local
git remote set-url origin https://github.com/rreon633-design/RADHA-SHELL.git
git push -u origin main
```

`radha-shell.bundle` in the workspace root is a self-contained copy of the full history:

```bash
git clone radha-shell.bundle radha-shell-local
```
