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

**Decision:** ship the real thing, but never a single hard-coded vtable. `Radha.Native` carries a
table of layouts, each pinned to the builds it was verified on (Win10 10240–19045, Win11 22000–22635,
Win11 26100+), and identifies which one is live by the IID it successfully queries — then validates
with `GetCount` before any further call. A build with no verified layout reports "unsupported" and
the bar keeps labelled placeholder pills rather than pretending.

Moves use the *documented* `IVirtualDesktopManager::MoveWindowToDesktop`, fed a GUID that came from
the internal interface — so only enumeration and switching are undocumented.

**Verified by:** `RadhaNativeSmoke.exe` on every push (see [`VIRTUAL-DESKTOPS.md`](VIRTUAL-DESKTOPS.md)).
**Residual risk:** a future Windows release that reuses an old IID with a different layout. The IID
gate cannot catch that; `GetCount` sanity-checking bounds the damage, and the smoke test fails loudly
on a desktop session.

## 19.3 System toast mirroring

**Problem:** `UserNotificationListener` requires packaged identity and the restricted
`userNotificationListener` capability; an unpackaged Win32 app cannot request it reliably.

**Decision:** implement it properly and be explicit about when it applies. `Package.appxmanifest`
declares the identity, `runFullTrust` and `userNotificationListener`; the packaged build is opt-in
(`-p:RadhaPackaged=true`) so the default unpackaged build is unchanged. `ToastMirrorService` asks for
access once, mirrors every toast into the store through the unit-tested `ToastMirror` projection, and
on an unpackaged run reports *why* it is off (`UnavailableReason`) instead of silently doing nothing.
Restricted capabilities need Partner Center approval before Store distribution — noted in the manifest.

## 19.4 VPN and Bluetooth

**Problem:** `Windows.Networking.Vpn` needs the restricted `networkingVpnProvider` capability, and
Bluetooth APIs are capability-gated for packaged apps.

**Decision:** use APIs that work for a desktop process. VPN goes through the documented RAS API
(`RasEnumEntriesW` / `RasDialW` / `RasHangUp`) — real Windows VPN profiles, no capability. Bluetooth
uses `DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true))` for the
paired-device list plus `Radio.SetStateAsync` for the adapter toggle. The projection rules (dedupe,
ordering, kind inference) live in `Radha.Core.Services.RasPhonebook` and are unit-tested on Linux;
limitations (no per-desktop window counts, no forced classic-Bluetooth connect) are documented rather
than papered over.

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
