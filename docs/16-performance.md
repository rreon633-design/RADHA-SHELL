# 16. Performance

## 16.1 Budgets and measured numbers

Measured on the reference container (Linux, .NET 9.0.21, 8 vCPU) via `radha bench --gate`.
Windows figures will differ, which is why budgets are deliberately loose relative to measurements.

| Benchmark | What it covers | n | Measured p50 | Measured p95 | Budget (p95) |
|---|---|---|---|---|---|
| `palette.extract` | decode→k-means→score on a 512×512 image | 25 | ~1.0 ms | ~5.4 ms | **120 ms** |
| `scheme.generate` | 35 roles, gamut mapping, dark+light | 500 | ~0.10 ms | ~0.12 ms | **5 ms** |
| `launcher.rank` | fuzzy scoring of 5 000 apps | 50 | ~0.4 ms | ~1.0 ms | **40 ms** |
| `config.roundtrip` | atomic save + load | 100 | ~0.11 ms | ~0.15 ms | **25 ms** |
| `ipc.roundtrip` | framing + validation + handler | 200 | ~0.04 ms | ~0.08 ms | **15 ms** |

Budgets are 10–20× the measured values so that CI noise (shared runners) never causes a false
failure, while a genuine algorithmic regression (e.g. O(n²) ranking) still trips them.

## 16.2 Design decisions that keep it fast

| Decision | Effect |
|---|---|
| Deterministic stride sampling before clustering | caps work at ~12 000 samples regardless of image size |
| k-means capped at 8 clusters / 14 iterations | predictable cost; convergence is reached earlier in practice |
| Palette cache keyed by `path|mtime|length` | re-selecting a wallpaper is a dictionary hit |
| Scheme generation is pure arithmetic | 0.1 ms means theme swaps are free; no need for async UI |
| Fuzzy ranking returns after a single pass + sort of ≤ 5 000 items | p95 < 1 ms |
| IPC frames are length-prefixed | no delimiter scanning, no JSON parse on partial data |
| Config save uses temp file + atomic move | no torn writes, no fsync storm |
| Audio FFT uses a fixed 1024-point window | constant cost per callback, independent of buffer size |
| Sensor polling at 1 Hz, not per-frame | CPU cost is negligible; UI reads cached values |
| UI never computes in a draw callback | Win2D graphs read an `ObservableCollection` snapshot |

## 16.3 Memory

- Palette buffers are 3 bytes/pixel, freed after extraction (not retained per wallpaper).
- Launcher keeps names + paths only (~5 000 entries ≈ 1 MB).
- Notification history is capped at 100 and holds no images.
- Spectrum is a fixed 48-float array, mutated in place (no per-frame allocation).
- Graphics: Win2D resources are per-control and disposed with the control.

## 16.4 Startup

Target: bar visible < 1.5 s warm. Startup path is: config load (file read + JSON), DI graph (10
singletons), three windows (no XAML parse of panels until opened — panels are constructed with the
overlay, so first open is instant), IPC bind, timers.

Everything expensive (app index, weather, sensors first sample) is deferred to after the bar is
shown.

## 16.5 Idle cost

With no panel open: one 42 px bar, a 1 s clock timer, a 1 s sensor timer, no animation loop. The
overlay window is hidden. The visualiser only runs while a media session is playing.

## 16.6 Profiling guide

- Managed: `dotnet-counters monitor --process-id <pid>` for CPU/GC/thread-pool.
- Allocation hot spots: `dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime`.
- Composition: Windows Performance Recorder with the *Composition* profile; look for dropped
  frames during panel open.
- Sensors: LHM's own UI to confirm a sensor is actually reporting before blaming our sampling.

## 16.7 Known performance risks

| Risk | Impact | Mitigation |
|---|---|---|
| Acrylic/Mica on integrated GPUs | compositor cost per frame | `appearance.transparency=false` fallback |
| LibreHardwareMonitor first poll | 100–400 ms | runs on a timer thread, never on the UI thread |
| Very large wallpapers (8K) | decode time | stride sampling caps clustering; WIC decode dominates |
| Sensor flood (many cores) | allocations per sample | single `Computer` instance, `Update()` per hardware only |
| IPC floods | UI-thread work | rate limiter (60 burst / 20 per second) |
