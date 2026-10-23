using System.IO.Compression;
using System.Text.Json.Nodes;
using Radha.Core.Config;
using Radha.Core.Diagnostics;
using Radha.Core.I18n;
using Radha.Core.Launcher;
using Radha.Core.Media;
using Radha.Core.Net;
using Radha.Core.Notifications;
using Radha.Core.Performance;
using Radha.Core.Power;
using Radha.Core.Security;
using Radha.Core.Theming;
using Radha.Core.Wallpaper;
using Radha.Core.Services;
using Radha.Core.Updates;

namespace Radha.Tests;

/// <summary>
/// Tests for the feature batch that closed the caelestia gap list: launcher actions and calculator,
/// notification policy, power rules, config migration/doctor/hot-reload, lyrics, Wi-Fi parsing,
/// localisation, update checks and diagnostics.
/// </summary>
public static class FeatureTests
{
    public static void Run(Action<string, bool> check)
    {
        CalculatorTests(check);
        LauncherTests(check);
        NotificationTests(check);
        PowerTests(check);
        ConfigTests(check);
        LyricsTests(check);
        WifiTests(check);
        LocaleTests(check);
        UpdateTests(check);
        SeriesTests(check);
        DiagnosticsTests(check);
        TokenTests(check);
        ProfileTests(check);
        ClockTests(check);
        LaunchGuardTests(check);
        WallpaperTests(check);
        PowerAndConfirmTests(check);
        CrashReportTests(check);
        SelfTestTests(check);
    }

    // ---------------- design tokens ----------------

    private static void TokenTests(Action<string, bool> check)
    {
        var tokens = DesignTokens.Default;
        check("tokens: spacing is ordered", tokens.SpacingXs < tokens.SpacingSm && tokens.SpacingSm < tokens.SpacingMd);
        check("tokens: radius is ordered", tokens.RadiusXs < tokens.RadiusSm && tokens.RadiusSm < tokens.RadiusLg);
        check("tokens: typography is ordered", tokens.FontCaption < tokens.FontBody && tokens.FontBody < tokens.FontHeading);
        check("tokens: minimum hit target", tokens.MinHitTarget >= 28);

        var dense = DesignTokens.Default with { Density = Density.Compact };
        check("tokens: compact shrinks spacing", dense.SpacingMd < tokens.SpacingMd);
        check("tokens: compact shrinks hit target", dense.MinHitTarget < tokens.MinHitTarget);

        var scaled = DesignTokens.Default with { Scale = 1.5 };
        check("tokens: scale multiplies", Math.Abs(scaled.SpacingMd - tokens.SpacingMd * 1.5) < 0.01);

        var bad = DesignTokens.Default with { Scale = double.NaN, FontScale = 99 };
        var fixedTokens = bad.Normalized();
        check("tokens: NaN scale normalised", fixedTokens.Scale == 0.5);
        check("tokens: font scale clamped", fixedTokens.FontScale == 3);

        var motion = MotionTokens.Default;
        check("motion: durations ordered", motion.Fast < motion.Normal && motion.Normal < motion.Slow);
        check("motion: scale multiplies", Math.Abs((MotionTokens.Default with { Scale = 2 }).Normal.TotalMilliseconds - 400) < 1);
        check("motion: reduced motion zeroes durations", MotionTokens.Default with { Reduced = true } is { } r &&
                                                            r.Fast == TimeSpan.Zero && r.PageTransition == TimeSpan.Zero);
        check("motion: zero scale zeroes durations", (MotionTokens.Default with { Scale = 0 }).Slow == TimeSpan.Zero);
    }

    // ---------------- appearance profiles ----------------

    private static void ProfileTests(Action<string, bool> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "radha-profiles-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new ProfileStore(dir);
            check("profile: empty store", store.List().Count == 0);

            var config = new ShellConfig();
            config.Appearance.Mode = "dark";
            config.Bar.Height = 55;

            var saved = store.SaveFrom("Work", config);
            check("profile: name sanitised", saved.Name == "Work");
            check("profile: file written", store.PathFor("Work") is var p && File.Exists(p));

            var loaded = store.List();
            check("profile: listed", loaded.Count == 1);
            check("profile: carries appearance", loaded[0].Json.Contains("appearance", StringComparison.Ordinal));
            check("profile: omits services", !loaded[0].Json.Contains("services", StringComparison.Ordinal));

            var target = new ShellConfig();
            target.Bar.Height = 30;
            var applied = store.Get("work")!.ApplyTo(target);
            check("profile: applies stored value", applied.Bar.Height == 55);
            check("profile: keeps unrelated values", applied.Appearance.Mode == "dark");

            check("profile: delete", store.Delete("Work") && store.List().Count == 0);
            check("profile: delete missing", !store.Delete("nope"));

            check("profile: traversal sanitised", !ProfileStore.Sanitize("../../etc/passwd").Contains('/'));
            check("profile: sanitised name resolves inside", store.PathFor("../../evil").StartsWith(Path.GetFullPath(dir), StringComparison.Ordinal));

            var corrupt = new AppearanceProfile("bad", "{ not json", DateTimeOffset.Now);
            check("profile: corrupt profile ignored", ReferenceEquals(corrupt.ApplyTo(config), config) ||
                                                        corrupt.ApplyTo(config).Bar.Height == config.Bar.Height);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------- shell clock ----------------

    private static void ClockTests(Action<string, bool> check)
    {
        using var clock = new ShellClock(TimeSpan.FromMilliseconds(25));
        var ticks = 0;
        clock.Tick += _ => Interlocked.Increment(ref ticks);

        check("clock: subscriber counted", clock.SubscriberCount == 1);
        check("clock: not running initially", !clock.IsRunning);

        clock.Start();
        check("clock: running after start", clock.IsRunning);
        Thread.Sleep(120);
        clock.Stop();

        var observed = Volatile.Read(ref ticks);
        check("clock: ticks fire", observed >= 2);
        check("clock: counter matches", clock.Ticks >= 2);

        using var manual = new ShellClock();
        var manualTicks = 0;
        manual.Tick += _ => manualTicks++;
        manual.Pulse();
        manual.Pulse();
        check("clock: pulse runs handlers", manualTicks == 2);

        using var bad = new ShellClock();
        bad.Tick += _ => throw new InvalidOperationException("boom");
        var survived = 0;
        bad.Tick += _ => survived++;
        bad.Pulse();
        check("clock: a throwing subscriber does not stop others", survived == 1);
    }

    // ---------------- launch guard ----------------

    private static void LaunchGuardTests(Action<string, bool> check)
    {
        check("guard: allowlisted exe", LaunchGuard.IsAllowed("wt.exe", LaunchGuard.DefaultAllowlist, out _));
        check("guard: allowlisted with path", LaunchGuard.IsAllowed(@"C:\Windows\System32\notepad.exe", null, out _) ||
                                              LaunchGuard.IsAllowed("/usr/bin/notepad.exe", null, out _) is false);
        check("guard: traversal rejected", !LaunchGuard.IsAllowed(@"..\..\evil.exe", null, out var traversal) &&
                                           traversal.Contains("traversal", StringComparison.Ordinal));
        check("guard: unc rejected", !LaunchGuard.IsAllowed(@"\\server\share\evil.exe", null, out _));
        check("guard: unknown binary rejected", !LaunchGuard.IsAllowed("definitely-not-allowed.exe", null, out var unknown) &&
                                                unknown.Contains("allowlist", StringComparison.Ordinal));
        check("guard: empty rejected", !LaunchGuard.IsAllowed("", null, out _));
        check("guard: uri scheme allowed", LaunchGuard.IsAllowed("ms-settings:display", null, out _));
        check("guard: arbitrary scheme rejected", !LaunchGuard.IsAllowed("javascript:alert(1)", null, out _));
        check("guard: drive path is not a scheme", LaunchGuard.IsAllowed(@"C:\Windows\System32\notepad.exe", null, out var drive) ||
                                                   drive.Contains("not exist", StringComparison.Ordinal));
        check("guard: custom allowlist honoured", LaunchGuard.IsAllowed("my-tool.exe", new[] { "my-tool.exe" }, out _));
        check("guard: custom allowlist excludes others", !LaunchGuard.IsAllowed("cmd.exe", new[] { "my-tool.exe" }, out _));
    }

    // ---------------- calculator ----------------

    private static void CalculatorTests(Action<string, bool> check)
    {
        bool Eval(string expr, out double value) => Calculator.TryEvaluate(expr, out value);

        check("calc: addition", Eval("2+2", out var a) && Math.Abs(a - 4) < 1e-9);
        check("calc: precedence", Eval("2+3*4", out var b) && Math.Abs(b - 14) < 1e-9);
        check("calc: parentheses", Eval("(2+3)*4", out var c) && Math.Abs(c - 20) < 1e-9);
        check("calc: right-associative power", Eval("2^3^2", out var d) && Math.Abs(d - 512) < 1e-9);
        check("calc: unary minus", Eval("-5+2", out var e) && Math.Abs(e + 3) < 1e-9);
        check("calc: sqrt", Eval("sqrt(9)", out var f) && Math.Abs(f - 3) < 1e-9);
        check("calc: constants", Eval("round(pi*100)/100", out var g) && Math.Abs(g - 3.14) < 1e-9);
        check("calc: modulo", Eval("10%3", out var h) && Math.Abs(h - 1) < 1e-9);
        check("calc: functions with two args", Eval("max(3,7)", out var i) && Math.Abs(i - 7) < 1e-9);

        check("calc: division by zero rejected", !Eval("1/0", out _));
        check("calc: unknown name rejected", !Eval("firefox(2)", out _));
        check("calc: unbalanced parens rejected", !Eval("(2+3", out _));
        check("calc: plain words rejected", !Eval("files", out _));
        check("calc: empty rejected", !Eval("", out _));
        check("calc: ** rejected", !Eval("2**3", out _));

        check("calc: looks like expression (2+2)", Calculator.LooksLikeExpression("2+2"));
        check("calc: does not look like expression (firefox)", !Calculator.LooksLikeExpression("firefox"));
        check("calc: does not look like expression (11)", !Calculator.LooksLikeExpression("11"));
        check("calc: format trims noise", Calculator.Format(4.0000000001) == "4");
        check("calc: format keeps decimals", Calculator.Format(2.5) == "2.5");
    }

    // ---------------- launcher ----------------

    private static void LauncherTests(Action<string, bool> check)
    {
        var apps = new[]
        {
            new LauncherEntry("firefox", LauncherKind.App, "Firefox", "Browser", "globe", null),
            new LauncherEntry("files", LauncherKind.App, "Files", "Explorer", "folder", null),
            new LauncherEntry("terminal", LauncherKind.App, "Terminal", "Shell", "prompt", null),
            new LauncherEntry("secret", LauncherKind.App, "Secret Tool", "Hidden", "lock", null)
        };

        var actions = new[]
        {
            new LauncherEntry("reload", LauncherKind.Action, "Reload config", "Radha", "refresh", "config reload"),
            new LauncherEntry("shutdown", LauncherKind.Action, "Shut down", "Power", "power", "system shutdown", Dangerous: true)
        };

        var prefs = new LauncherPrefs(new[] { "terminal" }, new[] { "secret" }, EnableDangerous: false);
        var engine = new LauncherEngine(apps, actions, null, prefs);

        var blank = engine.Query("");
        check("launcher: favourites float on empty query", blank.Count > 0 && blank[0].Entry.Id == "terminal");
        check("launcher: hidden app never listed", blank.All(h => h.Entry.Id != "secret"));

        var fuzzy = engine.Query("fir");
        check("launcher: prefix match wins", fuzzy.Count > 0 && fuzzy[0].Entry.Id == "firefox");

        var calc = engine.Query("12*12");
        check("launcher: calculator row comes first", calc.Count > 0 && calc[0].Entry.Kind == LauncherKind.Calculator);
        check("launcher: calculator value", calc[0].Entry.Title == "144");

        var action = engine.Query(">reload");
        check("launcher: action prefix", action.Count == 1 && action[0].Entry.Id == "reload");

        var dangerous = engine.Query(">shut");
        check("launcher: dangerous action gated", dangerous.Count == 0);

        var enabled = new LauncherEngine(apps, actions, null,
            new LauncherPrefs(Array.Empty<string>(), Array.Empty<string>(), EnableDangerous: true));
        var allowed = enabled.Query(">shut");
        check("launcher: dangerous action when enabled", allowed.Count == 1 && allowed[0].Entry.Dangerous);

        var nothing = engine.Query("zzzzzz");
        check("launcher: no results for noise", nothing.Count == 0);

        check("launcher: vim j moves down", LauncherEngine.TryNavigate('j', true, 0, 5, out var down) && down == 1);
        check("launcher: vim k clamps at top", !LauncherEngine.TryNavigate('k', true, 0, 5, out var up) && up == 0);
        check("launcher: j without vim ignored", !LauncherEngine.TryNavigate('j', false, 0, 5, out _));
    }

    // ---------------- notifications ----------------

    private static void NotificationTests(Action<string, bool> check)
    {
        var now = DateTimeOffset.Parse("2026-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        ShellNotification N(string id, string app, NotificationUrgency urgency, int secondsAgo, string[]? actions = null) =>
            new(id, app, app, "t" + id, "body " + id, urgency, now.AddSeconds(-secondsAgo), actions ?? Array.Empty<string>());

        var policy = NotificationPolicy.Default;

        check("notif: low urgency expires", NotificationRules.ShouldExpire(N("1", "a", NotificationUrgency.Low, 30), policy, now));
        check("notif: fresh normal does not expire", !NotificationRules.ShouldExpire(N("2", "a", NotificationUrgency.Normal, 1), policy, now));
        check("notif: high urgency never expires", !NotificationRules.ShouldExpire(N("3", "a", NotificationUrgency.High, 9999), policy, now));
        check("notif: actionable never expires",
            !NotificationRules.ShouldExpire(N("4", "a", NotificationUrgency.Low, 9999, new[] { "open" }), policy, now));
        check("notif: dnd suppresses expiry",
            !NotificationRules.ShouldExpire(N("5", "a", NotificationUrgency.Low, 9999), policy with { DoNotDisturb = true }, now));
        check("notif: dnd suppresses toasts", !NotificationRules.ShouldToast(N("6", "a", NotificationUrgency.Normal, 0), policy with { DoNotDisturb = true }));
        check("notif: dnd still toasts high urgency", NotificationRules.ShouldToast(N("7", "a", NotificationUrgency.High, 0), policy with { DoNotDisturb = true }));
        check("notif: clear after window",
            NotificationRules.ShouldClear(N("8", "a", NotificationUrgency.Low, 60 * 60 * 24 * 4), policy, now));

        var items = new[]
        {
            N("a1", "mail", NotificationUrgency.Normal, 10),
            N("b1", "chat", NotificationUrgency.High, 5, new[] { "reply" }),
            N("a2", "mail", NotificationUrgency.Normal, 20),
            N("a3", "mail", NotificationUrgency.Low, 30),
            N("a4", "mail", NotificationUrgency.Low, 40)
        };

        var capped = policy with { MaxPerGroup = 3 };
        var (groups, hidden) = NotificationRules.Group(items, capped);
        check("notif: grouped by app", groups.Count == 2);
        check("notif: newest group first", groups[0].AppId == "chat");
        check("notif: group capped", groups[1].Items.Count == capped.MaxPerGroup);
        check("notif: hidden counted", hidden == 1);
        check("notif: group total is real total", groups[1].Total == 4);
        check("notif: actions detected", groups[0].HasActions);
        check("notif: action label", NotificationRules.ActionLabel("reply") == "Reply");

        var (all, noneHidden) = NotificationRules.Group(items, policy with { MaxPerGroup = 10, MaxVisible = 10 });
        check("notif: bigger cap shows all", all.Sum(g => g.Items.Count) == 5 && noneHidden == 0);
    }

    // ---------------- power ----------------

    private static void PowerTests(Action<string, bool> check)
    {
        var watch = new BatteryWatch(low: 20, critical: 10);

        check("battery: no warning above threshold", watch.Update(50, false) is null);
        var low = watch.Update(19, false);
        check("battery: low fires", low is { Critical: false });
        check("battery: low fires once", watch.Update(18, false) is null);
        check("battery: critical fires", watch.Update(9, false) is { Critical: true });
        check("battery: critical fires once", watch.Update(8, false) is null);
        check("battery: charging re-arms", watch.Update(9, true) is null && watch.Update(19, false) is not null);

        var fresh = new BatteryWatch();
        check("battery: re-arm needs margin",
            fresh.Update(21, false) is null && fresh.Update(20, false) is { Critical: false } &&
            fresh.Update(22, false) is null && fresh.Update(19, false) is null);

        check("battery: reduced power on low battery", new BatteryWatch().InReducedPower(15, false));
        check("battery: not reduced while charging", !new BatteryWatch().InReducedPower(15, true));

        var cfg = IdleConfig.Default;
        var idle = new IdleInputs(TimeSpan.FromMinutes(30), false, false, false, false);

        check("idle: media inhibits",
            IdlePolicy.Evaluate(idle with { MediaPlaying = true }, cfg) is { InhibitDisplay: true, InhibitSystem: true });
        check("idle: fullscreen inhibits display only",
            IdlePolicy.Evaluate(idle with { FullscreenApp = true }, cfg) is { InhibitDisplay: true, InhibitSystem: false });
        check("idle: user inhibitor wins",
            IdlePolicy.Evaluate(idle with { UserInhibited = true }, cfg) is { InhibitDisplay: true });
        check("idle: timeout dims",
            IdlePolicy.Evaluate(idle, cfg).Action == IdleAction.DimDisplay);
        check("idle: off mode",
            IdlePolicy.Evaluate(idle, cfg with { DimInsteadOfOff = false }).Action == IdleAction.TurnOffDisplay);
        check("idle: recent activity does nothing",
            IdlePolicy.Evaluate(idle with { IdleFor = TimeSpan.FromSeconds(5) }, cfg).Action == IdleAction.None);
        check("idle: disabled policy is inert",
            IdlePolicy.Evaluate(idle, cfg with { Enabled = false }).Action == IdleAction.None);

        var normal = TimeSpan.FromSeconds(1);
        check("refresh: battery doubles interval", RefreshProfile.For(true, false, true, normal) == TimeSpan.FromSeconds(2));
        check("refresh: hidden window is slower", RefreshProfile.For(false, false, false, normal) == TimeSpan.FromSeconds(4));
        check("refresh: reduced power compounds", RefreshProfile.For(true, true, true, normal) == TimeSpan.FromSeconds(6));
    }

    // ---------------- config ----------------

    private static void ConfigTests(Action<string, bool> check)
    {
        // migrations
        var legacy = JsonNode.Parse("""
        {
          "mode": "dark",
          "blurRadius": 24,
          "bar": { "showSeconds": true, "height": 40 },
          "customFutureKey": { "keep": "me" }
        }
        """)!.AsObject();

        var applied = ConfigMigrations.Migrate(legacy);
        check("migrate: two steps applied", applied.Count == 2);
        check("migrate: version stamped", ConfigMigrations.VersionOf(legacy) == ConfigMigrations.Current);
        check("migrate: appearance nested", legacy["appearance"]?["mode"]?.GetValue<string>() == "dark");
        check("migrate: legacy key removed", !legacy.ContainsKey("mode"));
        check("migrate: renamed clock key", legacy["bar"]!["showClockSeconds"]!.GetValue<bool>());
        check("migrate: unknown key survives", legacy["customFutureKey"]?["keep"]?.GetValue<string>() == "me");

        check("migrate: idempotent", ConfigMigrations.Migrate(legacy).Count == 0);
        check("migrate: future detected",
            ConfigMigrations.IsFromFuture(JsonNode.Parse($$"""{"schemaVersion": {{ConfigMigrations.Current + 1}}}""")!.AsObject()));

        // doctor
        var doctor = new ConfigDoctor();
        var clean = doctor.Scan("""{"schemaVersion": 2, "bar": {"height": 40}}""");
        check("doctor: clean config has no errors", clean.Healthy);
        check("doctor: clean config summary", clean.Summary.Contains("clean") || clean.Summary.Contains("valid"));

        var broken = doctor.Scan("""{"schemaVersion": 2, "bar": {"height": 4000}, "nonsense": 1}""");
        check("doctor: out-of-range detected", broken.Findings.Any(f => f.Path == "bar.height"));
        check("doctor: unknown key warned", broken.Findings.Any(f => f.Path == "nonsense"));
        check("doctor: fixable", broken.Fixable);

        var (fixedJson, fixedReport) = doctor.Fix("""{"schemaVersion": 2, "bar": {"height": 4000}, "nonsense": 1}""");
        var reparsed = JsonNode.Parse(fixedJson)!.AsObject();
        check("doctor: clamps value", reparsed["bar"]!["height"]!.GetValue<double>() == 120);
        check("doctor: removes unknown", !reparsed.ContainsKey("nonsense"));
        check("doctor: reports fixes", fixedReport.Fixes.Count >= 2);
        check("doctor: re-scan is clean", doctor.Scan(fixedJson).Healthy);

        var invalid = doctor.Scan("{ not json");
        check("doctor: invalid json is an error", invalid.Errors > 0);
        check("doctor: invalid json not fixable", !invalid.Fixable);

        var future = doctor.Scan($$"""{"schemaVersion": {{ConfigMigrations.Current + 1}}}""");
        check("doctor: future schema flagged", future.Findings.Any(f => f.Severity == DoctorSeverity.Error));

        // save preserving unknown keys
        var dir = Path.Combine(Path.GetTempPath(), "radha-cfg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "shell.json");
            File.WriteAllText(path, """
            { "appearance": { "mode": "dark" }, "futureExperimental": { "flag": true } }
            """);

            var store = new ConfigStore(path);
            var config = store.Load();
            config.Bar.Height = 44;
            store.SavePreservingUnknown(config);

            var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            check("save: typed change persisted", saved["bar"]?["height"]?.GetValue<int>() == 44);
            check("save: unknown key preserved", saved["futureExperimental"]?["flag"]?.GetValue<bool>() == true);
            check("save: version stamped", saved["schemaVersion"]?.GetValue<int>() == ConfigMigrations.Current);

            // watcher
            using var watcher = new ConfigWatcher(path, TimeSpan.FromMilliseconds(60));
            var raised = new ManualResetEventSlim(false);
            watcher.Changed += _ => raised.Set();
            watcher.Start();
            File.WriteAllText(path, """{"schemaVersion":2,"bar":{"height":46}}""");
            check("watch: change raises after debounce", raised.Wait(TimeSpan.FromSeconds(5)));

            var rejected = new ManualResetEventSlim(false);
            using var badWatcher = new ConfigWatcher(path, TimeSpan.FromMilliseconds(60));
            badWatcher.Rejected += _ => rejected.Set();
            badWatcher.Start();
            File.WriteAllText(path, "{ broken");
            check("watch: invalid json is rejected", rejected.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------- lyrics ----------------

    private static void LyricsTests(Action<string, bool> check)
    {
        var lrc = """
        [ar:Someone]
        [ti:A Song]
        [offset:-500]
        [00:12.34]first line
        [00:15.00][01:20.00]repeated line
        [00:18.9]third line
        """;

        var doc = Lrc.Parse(lrc);
        check("lrc: metadata artist", doc.Artist == "Someone");
        check("lrc: metadata title", doc.Title == "A Song");
        check("lrc: offset parsed", doc.Offset == TimeSpan.FromMilliseconds(-500));
        check("lrc: synced flag", doc.Synced);
        check("lrc: repeated timestamp expands", doc.Lines.Count == 4);
        check("lrc: sorted by time", doc.Lines[0].At < doc.Lines[^1].At);
        check("lrc: first line text", doc.Lines[0].Text == "first line");
        check("lrc: hundredths parsed", Math.Abs(doc.Lines[0].At.TotalSeconds - 12.34) < 0.01);
        check("lrc: short fraction parsed", Math.Abs(doc.Lines.First(l => l.Text == "third line").At.TotalSeconds - 18.9) < 0.01);

        check("lrc: index before first is -1", Lrc.IndexAt(doc.Lines, TimeSpan.FromSeconds(1)) == -1);
        check("lrc: index finds current", Lrc.IndexAt(doc.Lines, TimeSpan.FromSeconds(13)) == 0);
        check("lrc: index after repeat", Lrc.IndexAt(doc.Lines, TimeSpan.FromSeconds(85)) == 3);

        var (current, next) = Lrc.At(doc.Lines, TimeSpan.FromSeconds(16));
        check("lrc: current at position", current == "repeated line");
        check("lrc: next at position", next == "third line");

        check("lrc: empty input safe", Lrc.Parse(null).Lines.Count == 0);
        check("lrc: garbage input safe", Lrc.Parse("[not-a-time] text").Lines.Count == 0);
        check("lrc: round trip", Lrc.Parse(Lrc.ToLrc(doc.Lines)).Lines.Count == doc.Lines.Count);

        var url = LyricsQuery.Search("A Song", "Some One", "Album", 200);
        check("lrc: url encodes spaces", url.Contains("track_name=A%20Song", StringComparison.Ordinal));
        check("lrc: url carries duration", url.Contains("duration=200", StringComparison.Ordinal));
    }

    // ---------------- wifi ----------------

    private static void WifiTests(Action<string, bool> check)
    {
        var output = """
        Interface name : Wi-Fi
        There are 2 networks currently visible.

        SSID 1 : HomeNet
            Network type            : Infrastructure
            Authentication          : WPA2-Personal
            Encryption              : CCMP
            BSSID 1                 : aa:bb:cc:dd:ee:01
                 Signal             : 60%
            BSSID 2                 : aa:bb:cc:dd:ee:02
                 Signal             : 84%
            Radio type              : 802.11ax

        SSID 2 : Cafe
            Network type            : Infrastructure
            Authentication          : Open
            Encryption              : None
            BSSID 1                 : aa:bb:cc:dd:ee:03
                 Signal             : 30%
            Radio type              : 802.11n
        """;

        var nets = WifiScanParser.Parse(output);
        check("wifi: two networks parsed", nets.Count == 2);
        check("wifi: strongest first", nets[0].Ssid == "HomeNet");
        check("wifi: duplicate bssid collapses to strongest", nets[0].SignalPercent == 84);
        check("wifi: secure flag", nets[0].Secure);
        check("wifi: open network not secure", !nets[1].Secure);
        check("wifi: radio type kept", nets[0].Radio == "802.11ax");
        check("wifi: bars computed", nets[0].Bars == 4 && nets[1].Bars == 1);
        check("wifi: empty input safe", WifiScanParser.Parse("").Count == 0);
        check("wifi: location hint detected", WifiScanParser.NeedsLocationPermission("needs location permission"));
    }

    // ---------------- locale ----------------

    private static void LocaleTests(Action<string, bool> check)
    {
        var en = new Translator("en", new Dictionary<string, string>
        {
            ["bar.clock"] = "Clock",
            ["items.one"] = "{0} item",
            ["items.other"] = "{0} items",
            ["only.english"] = "Only English"
        });

        check("i18n: lookup", en.Tr("bar.clock") == "Clock");
        check("i18n: missing key falls back to key", en.Tr("missing.key") == "missing.key");
        check("i18n: plural one", en.TrPlural("items", 1) == "1 item");
        check("i18n: plural other", en.TrPlural("items", 5) == "5 items");
        check("i18n: culture normalised", Translator.Normalize("en_GB") == "en-GB");
        check("i18n: invalid tag falls back", Translator.Normalize("!!") == "en");
        check("i18n: script subtag cased", Translator.Normalize("zh_hans_cn") == "zh-Hans-cn");

        var ru = new Translator("ru", new Dictionary<string, string>
        {
            ["items.one"] = "{0} предмет",
            ["items.few"] = "{0} предмета",
            ["items.many"] = "{0} предметов"
        }, new Dictionary<string, string> { ["only.english"] = "Only English" });

        check("i18n: ru plural one", ru.TrPlural("items", 1).EndsWith("предмет", StringComparison.Ordinal));
        check("i18n: ru plural few", ru.TrPlural("items", 3).EndsWith("предмета", StringComparison.Ordinal));
        check("i18n: ru plural many", ru.TrPlural("items", 11).EndsWith("предметов", StringComparison.Ordinal));
        check("i18n: fallback catalogue used", ru.Tr("only.english") == "Only English");

        var fromJson = Translator.FromJson("de", """{"bar.clock":"Uhr","bad":12}""", new Dictionary<string, string> { ["bar.clock"] = "Clock" });
        check("i18n: json catalogue loaded", fromJson.Tr("bar.clock") == "Uhr");
        check("i18n: json ignores non-strings", fromJson.Count == 1);
        check("i18n: broken json keeps fallback", Translator.FromJson("de", "{oops").Tr("x") == "x");

        check("units: 24h clock", Units.FormatClock(new DateTimeOffset(2026, 1, 1, 15, 4, 0, TimeSpan.Zero), ClockFormat.TwentyFourHour) == "15:04");
        check("units: 12h clock", Units.FormatClock(new DateTimeOffset(2026, 1, 1, 15, 4, 0, TimeSpan.Zero), ClockFormat.TwelveHour).StartsWith("3:04", StringComparison.Ordinal));
        check("units: temperature celsius", Units.FormatTemperature(21.4, TemperatureUnit.Celsius) == "21°C");
        check("units: temperature fahrenheit", Units.FormatTemperature(21.4, TemperatureUnit.Fahrenheit) == "71°F");
        check("units: size binary", Units.FormatSize(1536, DataUnit.Binary) == "1.5 KiB");
        check("units: size decimal", Units.FormatSize(1500, DataUnit.Decimal) == "1.5 kB");
        check("units: size bytes", Units.FormatSize(512, DataUnit.Binary) == "512 B");
    }

    // ---------------- updates ----------------

    private static void UpdateTests(Action<string, bool> check)
    {
        check("update: parse v tag", ReleaseCheck.TryParse("v1.2.3", out var v, out var pre) && v == new Version(1, 2, 3) && !pre);
        check("update: parse prerelease", ReleaseCheck.TryParse("v0.0.0-dev.15", out _, out var pre2) && pre2);
        check("update: parse two-part", ReleaseCheck.TryParse("1.4", out var shortV, out _) && shortV == new Version(1, 4, 0));
        check("update: parse garbage rejected", !ReleaseCheck.TryParse("nightly", out _, out _));
        check("update: parse null rejected", !ReleaseCheck.TryParse(null, out _, out _));

        var releases = new[]
        {
            new ReleaseInfo("v0.0.0-dev.16", true, Published: DateTimeOffset.Parse("2026-02-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
            new ReleaseInfo("v0.1.0", false, Published: DateTimeOffset.Parse("2026-02-02T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
            new ReleaseInfo("v0.0.9", false)
        };

        var stable = ReleaseCheck.Pick(releases, new Version(0, 0, 5), currentIsPrerelease: false);
        check("update: stable picks newest stable", stable.State == UpdateState.Available && stable.Available == new Version(0, 1, 0));
        check("update: summary mentions version", stable.Summary.Contains("0.1.0", StringComparison.Ordinal));

        var current = ReleaseCheck.Pick(releases, new Version(0, 1, 0), currentIsPrerelease: false);
        check("update: up to date", current.State == UpdateState.None);

        var prerelease = ReleaseCheck.Pick(releases, new Version(0, 0, 0), currentIsPrerelease: true);
        check("update: prerelease channel sees prereleases", prerelease.Available == new Version(0, 1, 0));

        var onlyPre = ReleaseCheck.Pick(new[] { new ReleaseInfo("v2.0.0-beta.1", true) }, new Version(1, 0, 0), false);
        check("update: prerelease only reported separately", onlyPre.State == UpdateState.PrereleaseOnly);
        check("update: current version parsed from metadata", ReleaseCheck.CurrentOrDefault("v0.3.1+abcdef") == new Version(0, 3, 1));
    }

    // ---------------- series ----------------

    private static void SeriesTests(Action<string, bool> check)
    {
        var series = new Series(4);
        for (var i = 1; i <= 3; i++) series.Add(i);
        check("series: count", series.Count == 3);
        check("series: last", series.Last == 3);
        check("series: min/max", series.Min == 1 && series.Max == 3);
        check("series: average", Math.Abs(series.Average - 2) < 1e-9);

        series.Add(4);
        series.Add(5);
        check("series: capacity respected", series.Count == 4);
        check("series: oldest evicted", series.ToArray() is [2, 3, 4, 5]);
        check("series: last after wrap", series.Last == 5);

        series.Add(double.NaN);
        check("series: NaN sanitised", series.Last == 0);

        var buffer = new float[4];
        var full = new Series(4);
        full.Add(0);
        full.Add(50);
        full.Add(100);
        full.Add(100);
        full.Normalized(buffer);
        check("series: normalised floor", Math.Abs(buffer[0]) < 1e-6);
        check("series: normalised middle", Math.Abs(buffer[1] - 0.5f) < 1e-6);
        check("series: normalised ceiling", Math.Abs(buffer[2] - 1f) < 1e-6);
    }

    // ---------------- diagnostics ----------------

    private static void DiagnosticsTests(Action<string, bool> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "radha-diag-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        try
        {
            // Deliberately contains "example": the repo secret scanner exempts obvious fixtures,
            // and the redactor still has to strip it from the bundle.
            var secret = "api_key=sk-example-not-a-real-key-000000";
            File.WriteAllText(Path.Combine(dir, "app.log"), "line one\n" + secret + "\n");

            var bundle = new DiagnosticsBundle()
                .WithEnvironment(version: "0.1.0", configPath: "/x/shell.json")
                .WithInfo("note", "manual test")
                .WithTextFile("logs/app.log", Path.Combine(dir, "app.log"))
                .WithTextFile("logs/missing.log", Path.Combine(dir, "nope.log"))
                .WithText("../../escape.txt", "should be flattened");

            var zipPath = Path.Combine(dir, "bundle.zip");
            bundle.Build(zipPath);

            check("diag: bundle written", File.Exists(zipPath));

            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Select(e => e.FullName).ToList();

            check("diag: environment included", names.Contains("environment.txt"));
            check("diag: log included", names.Contains("logs/app.log"));
            check("diag: missing file skipped", !names.Contains("logs/missing.log"));
            check("diag: traversal flattened", names.Contains("escape.txt") && names.All(n => !n.Contains("..")));

            using var reader = new StreamReader(archive.GetEntry("logs/app.log")!.Open());
            var content = reader.ReadToEnd();
            check("diag: secret redacted", !content.Contains("sk-example-not-a-real-key-000000", StringComparison.Ordinal));
            check("diag: log content kept", content.Contains("line one", StringComparison.Ordinal));

            using var env = new StreamReader(archive.GetEntry("environment.txt")!.Open());
            var envText = env.ReadToEnd();
            check("diag: environment carries version", envText.Contains("0.1.0", StringComparison.Ordinal));
            check("diag: environment carries runtime", envText.Contains("runtime", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------- wallpaper ----------------

    private static void WallpaperTests(Action<string, bool> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "radha-wall-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "aurora.png"), new byte[64]);
            File.WriteAllBytes(Path.Combine(dir, "Basalt.JPG"), new byte[128]);
            File.WriteAllBytes(Path.Combine(dir, "notes.txt"), new byte[8]);
            File.WriteAllBytes(Path.Combine(dir, "empty.png"), Array.Empty<byte>());

            var entries = WallpaperLibrary.Scan(dir);
            check("wallpaper: supported extensions only", entries.Count == 2);
            check("wallpaper: empty files skipped", entries.All(e => e.Name != "empty"));
            check("wallpaper: case-insensitive extension", entries.Any(e => e.Name == "Basalt"));
            check("wallpaper: ordinal-ignore-case ordering", entries[0].Name == "aurora");
            check("wallpaper: missing directory is empty", WallpaperLibrary.Scan(Path.Combine(dir, "nope")).Count == 0);
            check("wallpaper: null root is empty", WallpaperLibrary.Scan(null).Count == 0);
            check("wallpaper: cap respected", WallpaperLibrary.Scan(dir, maxEntries: 1).Count == 1);

            var first = entries[0];
            var keyA = WallpaperLibrary.ThumbnailKey(first, 240, 135);
            check("wallpaper: thumbnail key is stable", keyA == WallpaperLibrary.ThumbnailKey(first, 240, 135));
            check("wallpaper: thumbnail key varies by size", keyA != WallpaperLibrary.ThumbnailKey(first, 320, 180));
            var touched = first with { ModifiedUtc = first.ModifiedUtc.AddSeconds(5) };
            check("wallpaper: thumbnail key tracks mtime", keyA != WallpaperLibrary.ThumbnailKey(touched, 240, 135));
            check("wallpaper: thumbnail key is filename-safe", keyA.Length == 20 && keyA.EndsWith(".png"));

            check("wallpaper: fit parsed", WallpaperLibrary.ParseFit(" SPAN ") == WallpaperFit.Span);
            check("wallpaper: unknown fit falls back to fill", WallpaperLibrary.ParseFit("diagonal") == WallpaperFit.Fill);
            check("wallpaper: null fit falls back", WallpaperLibrary.ParseFit(null) == WallpaperFit.Fill);
            check("wallpaper: fit round-trips", WallpaperLibrary.ParseFit(WallpaperLibrary.ToConfigString(WallpaperFit.Center)) == WallpaperFit.Center);

            check("wallpaper: rooted drive path", WallpaperLibrary.IsRootedImagePath(@"C:\Pictures\a.png"));
            check("wallpaper: relative path rejected", !WallpaperLibrary.IsRootedImagePath(@"Pictures\a.png"));
            check("wallpaper: wrong extension rejected", !WallpaperLibrary.IsRootedImagePath(@"C:\Pictures\a.txt"));

            var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISPLAY#ABC123#1"] = @"C:\Pictures\one.png",
                ["*"] = @"C:\Pictures\fallback.png",
                ["  "] = @"C:\Pictures\ghost.png",
                ["MON2"] = "relative.png"
            };
            var clean = WallpaperLibrary.SanitizeAssignments(assignments);
            check("wallpaper: blank monitor dropped", clean.Count == 2);
            check("wallpaper: relative assignment dropped", !clean.ContainsKey("MON2"));
            check("wallpaper: unknown monitor kept", clean.ContainsKey("DISPLAY#ABC123#1"));

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISPLAY#ABC123#1"] = @"C:\a.png",
                ["*"] = @"C:\wild.png"
            };
            check("wallpaper: monitor assignment wins", WallpaperLibrary.Resolve("DISPLAY#ABC123#1", map, @"C:\global.png") == @"C:\a.png");
            check("wallpaper: monitor id case-insensitive", WallpaperLibrary.Resolve("display#abc123#1", map, null) == @"C:\a.png");
            check("wallpaper: wildcard catches unknown monitor", WallpaperLibrary.Resolve("MON9", map, @"C:\global.png") == @"C:\wild.png");
            check("wallpaper: fallback when no map", WallpaperLibrary.Resolve("MON9", null, @"C:\global.png") == @"C:\global.png");
            check("wallpaper: nothing configured is null", WallpaperLibrary.Resolve("MON9", null, "") is null);

            var fade = WallpaperLibrary.PlanTransition(TransitionMode.CrossFade, MotionTokens.Default);
            check("wallpaper: crossfade blends", fade.Blend && fade.Duration > TimeSpan.Zero);
            var calm = WallpaperLibrary.PlanTransition(TransitionMode.CrossFade, new MotionTokens(Reduced: true));
            check("wallpaper: reduced motion cuts", !calm.Blend && calm.Duration == TimeSpan.Zero);
            check("wallpaper: none mode cuts", !WallpaperLibrary.PlanTransition(TransitionMode.None, MotionTokens.Default).Blend);
            check("wallpaper: zero-scale motion cuts", !WallpaperLibrary.PlanTransition(TransitionMode.CrossFade, new MotionTokens(Scale: 0)).Blend);

            check("wallpaper: sequential rotation wraps", WallpaperLibrary.NextIndex(2, 3, shuffle: false) == 0);
            check("wallpaper: single image keeps index", WallpaperLibrary.NextIndex(0, 1, shuffle: false) == 0);
            var repeated = Enumerable.Range(0, 64).Select(_ => WallpaperLibrary.NextIndex(1, 3, shuffle: true, new Random(7))).ToList();
            check("wallpaper: shuffle never repeats current", repeated.All(i => i != 1 && i >= 0 && i < 3));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------- power profiles + confirmations ----------------

    private static void PowerAndConfirmTests(Action<string, bool> check)
    {
        check("power: saver guid", PowerProfiles.ToGuid(PowerProfile.Saver) == "961cc777-2547-4f9d-8174-7d86181b8a7a");
        check("power: performance guid", PowerProfiles.ToGuid(PowerProfile.Performance) == "ded574b5-45a0-4f42-8737-46345c09c238");
        check("power: balanced is the zero guid", PowerProfiles.ToGuid(PowerProfile.Balanced) == "00000000-0000-0000-0000-000000000000");

        check("power: parses alias", PowerProfiles.TryParse("EFFICIENCY", out var saver) && saver == PowerProfile.Saver);
        check("power: parses performance", PowerProfiles.TryParse(" performance ", out var perf) && perf == PowerProfile.Performance);
        check("power: rejects nonsense", !PowerProfiles.TryParse("turbo-boost", out _));
        check("power: null is rejected", !PowerProfiles.TryParse(null, out _));
        check("power: config string round-trips",
            PowerProfiles.TryParse(PowerProfiles.ToConfigString(PowerProfile.Performance), out var round) && round == PowerProfile.Performance);

        var args = PowerProfiles.PowercfgArguments(PowerProfiles.SaverGuid);
        check("power: powercfg arguments", args.Count == 2 && args[0] == "/overlaysetactive" && args[1] == PowerProfiles.SaverGuid);
        check("power: irrelevant on a desktop", !PowerProfiles.IsRelevant(hasBattery: false));

        var guard = new ConfirmGuard(TimeSpan.FromSeconds(4));
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        check("confirm: first press only arms", !guard.Confirm("reboot", t0));
        check("confirm: arm is visible", guard.ArmedAction == "reboot");
        check("confirm: second press confirms", guard.Confirm("reboot", t0.AddSeconds(1)));
        check("confirm: disarmed after use", guard.ArmedAction is null);
        check("confirm: third press re-arms", !guard.Confirm("reboot", t0.AddSeconds(2)));

        var stale = new ConfirmGuard(TimeSpan.FromSeconds(4));
        stale.Confirm("shutdown", t0);
        check("confirm: expiry forces a new arm", !stale.Confirm("shutdown", t0.AddSeconds(10)));

        var swapped = new ConfirmGuard(TimeSpan.FromSeconds(4));
        swapped.Confirm("shutdown", t0);
        check("confirm: different action replaces the arm", !swapped.Confirm("reboot", t0.AddSeconds(1)));
        check("confirm: replaced arm is the new action", swapped.ArmedAction == "reboot");

        check("confirm: remaining shrinks", Math.Abs(swapped.RemainingSeconds(t0.AddSeconds(3)) - 2) < 0.01);
        check("confirm: idle has no countdown", new ConfirmGuard().RemainingSeconds() == 0);

        var threw = false;
        try { new ConfirmGuard(TimeSpan.Zero); } catch (ArgumentOutOfRangeException) { threw = true; }
        check("confirm: zero window rejected", threw);
    }

    // ---------------- crash reports ----------------

    private static void CrashReportTests(Action<string, bool> check)
    {
        var env = new Dictionary<string, string?>
        {
            ["version"] = "0.1.0",
            ["runtime"] = "9.0.0",
            ["os"] = "Windows 11"
        };

        Exception Build()
        {
            try { throw new InvalidOperationException("outer", new IOException("inner")); }
            catch (Exception ex) { return ex; }
        }

        var text = CrashReport.Format(Build(), env, "clicked: Wallpaper tab");
        check("crash: header", text.Contains("RADHA SHELL — crash report", StringComparison.Ordinal));
        check("crash: outer type", text.Contains("System.InvalidOperationException", StringComparison.Ordinal));
        check("crash: outer message", text.Contains("outer", StringComparison.Ordinal));
        check("crash: inner exception", text.Contains("inner", StringComparison.Ordinal));
        check("crash: context", text.Contains("clicked: Wallpaper tab", StringComparison.Ordinal));
        check("crash: environment", text.Contains("runtime", StringComparison.Ordinal) && text.Contains("9.0.0", StringComparison.Ordinal));
        check("crash: no exception object still formats", CrashReport.Format(null, env).Contains("no exception object", StringComparison.Ordinal));

        var leaky = new Exception("request failed for key sk-example-not-a-real-key-000000");
        var redacted = CrashReport.Format(leaky, env);
        check("crash: secrets redacted", !redacted.Contains("sk-example-not-a-real-key-000000", StringComparison.Ordinal));

        var name = CrashReport.FileName(new InvalidOperationException(), new DateTimeOffset(2026, 5, 4, 3, 2, 1, TimeSpan.Zero));
        check("crash: filename carries type", name.Contains("InvalidOperationException", StringComparison.Ordinal) && name.EndsWith(".log"));
        check("crash: filename is sortable", name.StartsWith("crash-20260504-030201", StringComparison.Ordinal));
        check("crash: unknown exception still names a file", CrashReport.FileName(null, DateTimeOffset.Now).Contains("Unknown", StringComparison.Ordinal));

        var dir = Path.Combine(Path.GetTempPath(), "radha-crash-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var path = CrashReport.Write(dir, Build(), env, "boot");
            check("crash: written to disk", path is not null && File.Exists(path));
            check("crash: written file has the report", path is not null && File.ReadAllText(path).Contains("outer", StringComparison.Ordinal));
            check("crash: second write is a separate file",
                CrashReport.Write(dir, new ArgumentException("x"), env) is { } second &&
                Path.GetDirectoryName(second) is { } folder &&
                Directory.GetFiles(folder).Length >= 2);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------- published-binary self-test ----------------

    private static void SelfTestTests(Action<string, bool> check)
    {
        var report = SelfTest.Run();
        check("selftest: passes on this host", report.Passed);
        check("selftest: has checks", report.Checks.Count >= 8);
        check("selftest: no failures", report.Failures == 0);
        check("selftest: runtime check present", report.Checks.Any(c => c.Name == "runtime"));
        check("selftest: windows sdk skipped without a probe", report.Checks.Any(c => c.Name == "windows app sdk" && c.Skipped));
        check("selftest: text names the result", report.ToText().Contains("PASS", StringComparison.Ordinal));
        check("selftest: json parses", System.Text.Json.JsonDocument.Parse(report.ToJson()).RootElement.TryGetProperty("passed", out _));

        var broken = SelfTest.Run(() => "Microsoft.WinUI.dll missing");
        check("selftest: a bad runtime probe fails the run", !broken.Passed && broken.Failures == 1);

        var throwing = SelfTest.Run(() => throw new InvalidOperationException("no runtime"));
        check("selftest: a throwing probe is a failure, not a crash", !throwing.Passed);

        var good = SelfTest.Run(() => null);
        check("selftest: a clean probe passes", good.Passed);
    }
}
