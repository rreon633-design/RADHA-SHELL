# 21. Advantages

## 21.1 Against existing Windows tools

| | RADHA SHELL | Rainmeter | Cairo Shell | TranslucentTB | Start11 | Lively | EliteBar/Zebar | Caelestia (Linux) |
|---|---|---|---|---|---|---|---|---|
| Native GPU composition | ✅ Composition | ❌ GDI | ⚠ WPF | ✅ | ⚠ | ✅ | ❌ web | ✅ |
| Material 3 dynamic theming | ✅ 7 variants | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Bar + dashboard + launcher + CC + OSD | ✅ all | ⚠ widgets only | ⚠ partial | ❌ | ❌ | ❌ | ⚠ bar only | ✅ (Linux) |
| Media transport + visualiser | ✅ SMTC + FFT | ⚠ plugin | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Sensor dashboard | ✅ LHM | ⚠ plugin | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Scriptable CLI + IPC | ✅ 60+ commands | ❌ | ❌ | ❌ | ❌ | ⚠ | ❌ | ✅ |
| No web runtime | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Open source | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ |
| Maintained / modern stack | ✅ WinUI 3 | ⚠ legacy | ❌ stagnant | ✅ | ✅ | ✅ | ⚠ | ✅ |
| Performance budgets in CI | ✅ | ❌ | ❌ | ❌ | — | ❌ | ❌ | ❌ |

## 21.2 Engineering advantages

**Verified core.** 235 assertions cover colour math, palette extraction, config, IPC, security and
performance budgets. The palette engine is dependency-free C# that runs anywhere.

**Honest status.** The README and `docs/08-features.md` distinguish verified from unverified work.
There is no "demo mode" pretending features exist.

**Security treated as a boundary.** Validation, rate limiting, owner-only sockets, redacted errors
and audit logs are implemented and tested — unusual for a desktop cosmetic tool.

**One command surface.** CLI, IPC, hotkeys and buttons converge on one catalog and one executor, so
behaviour cannot drift.

**Replaceable OS layer.** Every Windows dependency sits behind an interface in `Radha.Core.Services`.
A future port needs new implementations, not a rewrite.

**Configuration that cannot brick the app.** Atomic writes, corruption fallback, clamping,
per-monitor deep merge, size-capped overrides.

## 21.3 User-facing advantages

- Wallpaper change re-themes everything in ~0.1 ms of computation.
- Launcher ranking is sub-millisecond on 5 000 apps — no lag on keystrokes.
- Panels open without spawning windows; idle cost is one 42 px bar.
- Everything is keyboard-reachable and scriptable.
- No account, no telemetry, no network dependency for core features.

## 21.4 Honest disadvantages

- Not compiled on Windows in the author's environment (yet) — a real adoption risk today.
- No installer, no code signing (phase 3).
- No system-toast mirroring and no virtual-desktop switching yet.
- Requires .NET 9 SDK to build from source.
- Documentation-heavy: the project expects you to read status tables, not marketing claims.
