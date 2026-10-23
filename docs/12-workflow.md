# 12. Workflow & Working

## 12.1 Cold start

```
Radha.App.exe
  ├─ App.OnLaunched → Ui.Initialize() (DispatcherQueue)
  ├─ ShellHost ctor
  │    ├─ ConfigStore.Load()            → defaults if missing; .broken backup if corrupt
  │    ├─ DI container: 10 services
  │    ├─ ThemeService.Initialize(cfg)  → seed #7C5CFF until a wallpaper is applied
  │    ├─ ShellState + view models       (workspaces, tiles, launcher index is lazy)
  │    └─ BarWindow / OverlayWindow / OsdWindow (hidden)
  └─ ShellHost.StartAsync()
       ├─ Bar.Activate(), app-bar registration
       ├─ SMTC session attach
       ├─ IpcServer.StartAsync()          → pipe + rate limiter + audit
       ├─ clock timer (1 s) + weather timer (20 min)
       ├─ DashboardViewModel.InitializeAsync() → sensors 1 Hz, weather refresh
       ├─ wallpaper configured? → palette → theme applied
       └─ welcome notification
```

Target: bar visible < 1.5 s from process start on a warm machine.

## 12.2 Interaction flows

**Wallpaper change → everything re-tints**

```
control centre "next wallpaper" (or `radha wallpaper next`)
   → WallpaperService.SetAsync(path)  [SystemParametersInfo + PaletteCache]
   → ThemeService.Apply(seed, dark, variant)  [~0.1 ms, measured]
   → MaterialScheme.FromSeed (35 roles)
   → ShellState.ApplyTheme → ThemeViewModel swap
   → every bound surface re-renders; list items re-colour via item brushes
```

**Launcher keystroke → ranked results**

```
keystroke → TextBox.TextChanged → LauncherViewModel.Query
   → FuzzyMatcher.Rank(5000 apps, query, limit 12)   [p95 < 1 ms, measured]
   → Results replaced (LauncherItemVm carries theme brushes)
   → Enter → Activate → Process.Start(shell) or command routed through IPC
```

**Volume key → OSD**

```
audio change → IAudioService.Changed → ShellState.ShowOsd("Volume", v)
   → OsdWindow.Show() + ScheduleOsdHide (1.6 s timer)
   → window is click-through, never steals focus
```

## 12.3 Command flow (external)

Covered in [11-api-ipc.md](11-api-ipc.md#113-request-lifecycle). The important property: the CLI
and the GUI converge on `ShellHost.Execute`, so there is exactly one implementation of every action.

## 12.4 Sleep / resume, display changes

- `DisplayArea.PrimaryChanged` → `DisplayService` refreshes the monitor list (bar repositions).
- Sensors resume automatically (timer-based); the first sample after resume is noisy and is
  deliberately not smoothed.
- Weather refresh happens on the next timer tick (no wake-from-sleep network burst).

## 12.5 Shutdown

`ShellHost.DisposeAsync`:
1. stop clock and weather timers,
2. dispose IPC server (cancel accept loop, drain 2 s, delete socket file),
3. dispose sensors (`Computer.Close()`),
4. dispose audio (stop loopback, dispose devices),
5. dispose DI container.

The app-bar registration is removed by the OS when the window is destroyed.

## 12.6 Degraded modes

| Condition | Behaviour |
|---|---|
| Daemon not running | CLI exits 2 with a clear message |
| config locked / read-only | save throws; runtime keeps working with in-memory config |
| no network | weather and lyrics silently unavailable, everything else works |
| no GPU sensors | GPU card shows 0 % with a "LibreHardwareMonitor" footnote |
| battery saver | DWM may disable transparency; surfaces fall back to opaque tones |
