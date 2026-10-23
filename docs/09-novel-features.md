# 9. Novel Features

Things that are genuinely uncommon in the Windows shell space, or that RADHA SHELL does
differently from the tools in [01-problem-statement.md](01-problem-statement.md).

## 9.1 Perceptually-correct wallpaper theming in pure C#

Most Windows themers average RGB pixels and call it a day, which produces muddy, greyish accents.
RADHA SHELL:

1. samples pixels on a deterministic stride (skipping pure black and clipped white),
2. clusters in **OKLab** — a perceptually uniform space,
3. seeds k-means from the *most chromatic* pixel, then farthest-point seeding,
4. scores clusters on chroma, mid-lightness and population,
5. maps the winner into Material 3 tonal palettes with **gamut mapping by chroma bisection**,
6. asserts WCAG AA contrast for every pair it ships.

All of it is dependency-free and covered by 20+ assertions, including the classic out-of-gamut
stress case (saturated yellow) and a grey seed.

## 9.2 Seven scheme variants, honestly gamut-mapped

TonalSpot, Vibrant, Expressive, Fidelity, Content, Neutral, Monochrome. Variants differ in chroma
multiplier and hue rotation, and every generated role is verified to be displayable in sRGB.

## 9.3 One command catalog for CLI, IPC, hotkeys and buttons

There is exactly one list of valid module/command pairs (`CommandCatalog`). The CLI validates
against it before connecting; the daemon validates again before executing; the launcher's `>`
actions are generated from it. Drift between "what the UI can do" and "what the CLI can do" is
structurally impossible, and tests assert the help text lists every module.

## 9.4 A hardened local IPC surface

Local IPC is usually an afterthought. Here it is treated as a trust boundary:

- 4 MiB frame ceiling and strict length validation (malformed frames drop the connection only),
- deny-by-default argument validation (control characters, shell metacharacters, length caps),
- per-client token-bucket rate limiting (60 burst / 20 per second),
- owner-only socket permissions on Unix (0600, verified in tests),
- exception messages redacted before they leave the process (GitHub PATs, `sk-` keys, bearer
  tokens, `api_key=` assignments),
- append-only audit log with the same redaction applied.

## 9.5 Benchmarks that gate the build

`radha bench --gate` measures palette extraction, scheme generation, launcher ranking, config I/O
and IPC loopback with p95 budgets. The `performance` CI job fails the build on regression. Shells
are usually unmeasured; this one is not.

## 9.6 Wallpaper → palette cache keyed by content identity

Keys are `path|mtime-ticks|length`, so re-selecting a wallpaper is free and an edited file
invalidates itself without a filesystem watcher. Hit ratio is exposed for diagnostics.

## 9.7 Per-monitor configuration with deep merge

`monitors/<display>/shell.json` is deep-merged over the base config. A monitor override can change
`bar.height` without redeclaring anything else, and tests assert the base file is untouched.

## 9.8 HLSL rounded-rectangle SDF with per-corner radii

`Morph.hlsl` implements a signed-distance rounded rectangle with four independently animatable
corner radii — the primitive behind the pill-to-circle morph. Provided with a dual-Kawase blur and
an animated grain effect, all with plain `fxc` build steps.

## 9.9 Verification discipline as a feature

The repository states, in the README and in CI, which code is verified and which is not. The
`winui`, `native` and `shaders` jobs exist precisely because the author could not compile them in
the development environment; they are the honest gate, not decoration.
