# 19. Problematic Areas

Stated plainly. These are the parts of the project that are hard, unverified, or dependent on
undocumented behaviour.

## 19.1 The WinUI code has not been compiled by the author

**Problem:** the development environment for this repository is Linux. `Radha.App` targets
`net9.0-windows10.0.19041.0` and cannot be built there.

**Consequence:** XAML name-scope issues, SDK version drift and package-version mismatches are
discovered by the `winui` CI job, not locally. That is exactly why that job exists.

**Mitigation:** all platform-neutral logic lives in `Radha.Core` and is tested (235 assertions); the
XAML is written defensively (no `ElementName` bindings inside templates, item-level brushes, explicit
`x:Name` roots).

## 19.2 Virtual desktop switching

**Problem:** Windows exposes no supported API to enumerate or switch virtual desktops.
`IVirtualDesktopManager` only reports which desktop owns a window. Switching requires
`IVirtualDesktopManagerInternal`, an undocumented COM interface whose IID changes across Windows
builds.

**Decision:** do not ship a hard-coded vtable that silently corrupts on the next feature update.
`WorkspaceService` implements the documented contract with local workspace pills; the native bridge
returns "unsupported" for the undocumented calls.

**Path forward:** version-dispatched COM implementation in phase 2, behind the existing interface.

## 19.3 System toast mirroring

**Problem:** `UserNotificationListener` requires packaged identity and the
`userNotificationListener` capability; an unpackaged Win32 app cannot request it reliably.

**Decision:** the notification centre works today for shell-generated notifications and for
anything pushed over IPC; system toasts are documented as ⬜, not faked.

## 19.4 App-bar docking correctness

**Problem:** `SHAppBarMessage` behaves differently across multi-monitor layouts, DPI changes and
fullscreen apps. A mis-registered app bar can leave a dead strip at the top of the screen.

**Mitigation:** registration is isolated in the native bridge with an explicit unregister path; the
managed fallback positions the window without reserving work area (no dead strip, just overlap).

## 19.5 XAML data-template theming

**Problem:** a `DataTemplate` cannot see the view model of the containing view (name scope), so
`{Binding DataContext.Theme.Primary}` inside a template silently fails at runtime.

**Mitigation:** list-item view models (`TileViewModel`, `LauncherItemVm`, `NotificationItemVm`,
`WeatherHourVm`) carry their own `SolidColorBrush` properties, refreshed whenever the theme
changes. This is more code but it is correct and observable.

## 19.6 DWM materials vary by build

Acrylic/Mica availability depends on Windows build, transparency settings and battery saver. The
system can also silently return an opaque surface.

**Mitigation:** every surface has an opaque-tonal fallback (`SurfaceContainer*` roles) so the UI is
readable when translucency disappears.

## 19.7 Sensor coverage

LibreHardwareMonitor cannot read every laptop's EC (especially dGPU-equipped laptops with
vendor-controlled fans). GPU load can report 0 % on some Intel configurations.

**Mitigation:** the performance cards show a provider label; "no GPU sensor" is a documented
outcome, not a bug.

## 19.8 Audio on RDP / VMs

There may be no render endpoint at all. WASAPI loopback init throws.

**Mitigation:** the audio service catches and degrades to a flat spectrum; volume falls back to 50 %.

## 19.9 IPC concurrency

The daemon handles one request at a time per connection and marshals all work to the UI thread.
A pathological client issuing hundreds of toggles is bounded by the rate limiter (60 burst / 20 per
second) but each accepted request still does UI work.

**Mitigation:** rate limiting is tested; panel toggles are cheap (visibility + composition); future
work could coalesce repeated toggles within a frame.

## 19.10 Benchmarks on shared runners

CI runners are noisy; a p95 budget that is tight enough to catch regressions can also flake.

**Mitigation:** budgets are 10–20× measured values, and the benchmark prints raw numbers so a human
can tell noise from a real regression.

## 19.11 Not implemented at all

| Area | Status |
|---|---|
| Lock screen / authentication | excluded by decision, enforced by CI |
| Explorer replacement | out of scope |
| Tiling window management | out of scope |
| Plugin/scripting runtime | out of scope for v1 |
| macOS/Linux shell UI | out of scope |
