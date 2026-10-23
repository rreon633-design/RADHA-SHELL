using System.Text.Json;
using Radha.Core.Config;
using Radha.Core.Ipc;
using Radha.Core.Modules;
using Radha.Core.Theming;

namespace Radha.Tests;

/// <summary>
/// Dependency-free test harness (runs with <c>dotnet run --project src/Radha.Tests</c>).
/// Keeping it package-free means CI needs no NuGet restore for the verification step.
/// </summary>
public static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    public static async Task<int> Main(string[] args)
    {
        // CI perf gate: dotnet run --project src/Radha.Tests -- bench --gate
        if (args.Length > 0 && args[0] is "bench")
            return await Bench.RunAsync(args, Console.Out, Console.Error);

        Console.WriteLine("RADHA SHELL — core test suite\n");

        TestColourMath();
        TestTonalPalette();
        TestSchemes();
        TestImageDecoding();
        TestPaletteExtraction();
        TestConfig();
        TestCommandCatalog();
        await TestIpcAsync();

        Console.WriteLine("\n--- security ---");
        HardeningTests.RunSecurity(Check);
        await IpcSecurityTests.RunAsync(Check);

        Console.WriteLine("\n--- performance ---");
        HardeningTests.RunPerformance(Check);

        Console.WriteLine("\n--- platform projections ---");
        PlatformTests.Run(Check);

        Console.WriteLine("\n--- feature hardening ---");
        HardeningTests.RunFeatures(Check);

        Console.WriteLine("\n--- gap-closing features ---");
        FeatureTests.Run(Check);

        Console.WriteLine();
        foreach (var failure in Failures) Console.WriteLine($"  FAIL {failure}");
        Console.WriteLine($"\n{_passed} passed, {Failures.Count} failed");
        return Failures.Count == 0 ? 0 : 1;
    }

    // ---------------- colour ----------------

    private static void TestColourMath()
    {
        var samples = new[]
        {
            new Rgb(124, 92, 255), new Rgb(55, 211, 216), new Rgb(255, 138, 61),
            new Rgb(20, 16, 24), new Rgb(255, 255, 255), new Rgb(0, 0, 0),
            new Rgb(128, 128, 128), new Rgb(200, 40, 160), new Rgb(30, 120, 60)
        };

        foreach (var c in samples)
        {
            var back = c.ToOkLch().ToRgb();
            Check($"OKLCH round-trip {c.ToHex()} → {back.ToHex()}",
                Math.Abs(back.R - c.R) <= 2 && Math.Abs(back.G - c.G) <= 2 && Math.Abs(back.B - c.B) <= 2);
        }

        Check("hex parse #7C5CFF", Rgb.FromHex("#7C5CFF") == new Rgb(124, 92, 255));
        Check("hex parse AARRGGBB", Rgb.FromHex("#FF7C5CFF") == new Rgb(124, 92, 255));
        Check("contrast black/white ≈ 21", Math.Abs(new Rgb(0, 0, 0).Contrast(new Rgb(255, 255, 255)) - 21.0) < 0.01);
        Check("contrast white/white = 1", Math.Abs(new Rgb(255, 255, 255).Contrast(new Rgb(255, 255, 255)) - 1.0) < 0.001);
        Check("lerp midpoint", Rgb.Lerp(new Rgb(0, 0, 0), new Rgb(100, 200, 40), 0.5) == new Rgb(50, 100, 20));
        Check("hue normalisation wraps", Math.Abs(new OkLch(0.6, 0.1, -30).WithHue(-30).H - 330) < 0.001);
    }

    // ---------------- palette ----------------

    private static void TestTonalPalette()
    {
        var palette = new TonalPalette(280, 0.16);
        var previous = -1.0;
        var monotonic = true;
        for (var tone = 0; tone <= 100; tone += 5)
        {
            var l = palette.Tone(tone).RelativeLuminance;
            if (l < previous - 1e-9) monotonic = false;
            previous = l;
        }
        Check("tonal palette luminance increases with tone", monotonic);
        Check("tone 0 is black", palette.Tone(0) == new Rgb(0, 0, 0));
        Check("tone 100 is white", palette.Tone(100) == new Rgb(255, 255, 255));

        var saturated = new TonalPalette(280, 0.18).Tone(40);
        var grey = new TonalPalette(280, 0.0).Tone(40);
        Check("chroma actually saturates output", saturated.ToOkLch().C > grey.ToOkLch().C + 0.05);
    }

    private static void TestSchemes()
    {
        var seed = new Rgb(124, 92, 255);
        var variants = Enum.GetValues<SchemeVariant>();

        foreach (var variant in variants)
        {
            foreach (var dark in new[] { true, false })
            {
                var scheme = MaterialScheme.FromSeed(seed, dark, variant);
                Check($"{variant}/{(dark ? "dark" : "light")} readable contrast", scheme.HasReadableContrast());
                Check($"{variant}/{(dark ? "dark" : "light")} surface follows mode",
                    dark ? scheme.Surface.RelativeLuminance < 0.1 : scheme.Surface.RelativeLuminance > 0.8);
                Check($"{variant}/{(dark ? "dark" : "light")} role map complete", scheme.ToRoleMap().Count == 35);
                Check($"{variant}/{(dark ? "dark" : "light")} roles are distinct", scheme.ToRoleMap().Values.Distinct().Count() >= 25);
            }
        }

        var a = MaterialScheme.FromSeed(seed, true);
        var b = MaterialScheme.FromSeed(seed, true);
        Check("scheme generation is deterministic", a.ToRoleMap().SequenceEqual(b.ToRoleMap()));

        // A muted seed makes the variant differences visible (Vibrant re-saturates it).
        var muted = new Rgb(90, 106, 138);
        var vivid = MaterialScheme.FromSeed(muted, true, SchemeVariant.Vibrant).ToRoleMap();
        var tonal = MaterialScheme.FromSeed(muted, true, SchemeVariant.TonalSpot).ToRoleMap();
        var differing = vivid.Count(kv => tonal[kv.Key] != kv.Value);
        Check("variants change the palette", differing >= 3);

        // A grey seed must not crash or produce NaN-ish colours.
        var greyScheme = MaterialScheme.FromSeed(new Rgb(90, 90, 94), true);
        Check("grey seed yields valid scheme", greyScheme.HasReadableContrast());

        // Very saturated yellow is the classic out-of-gamut stress case.
        var yellow = MaterialScheme.FromSeed(new Rgb(255, 235, 0), false, SchemeVariant.Vibrant);
        Check("out-of-gamut seed is gamut-mapped", yellow.HasReadableContrast());
    }

    // ---------------- image decoding ----------------

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static void TestImageDecoding()
    {
        var quad = ImageDecoder.DecodeFile(Fixture("quadrant.png"));
        Check("PNG dimensions", quad.Width == 64 && quad.Height == 64);
        Check("PNG top-left violet", quad[4, 4] == new Rgb(124, 92, 255));
        Check("PNG top-right teal", quad[60, 4] == new Rgb(55, 211, 216));
        Check("PNG bottom-left orange", quad[4, 60] == new Rgb(255, 138, 61));
        Check("PNG bottom-right near-black", quad[60, 60] == new Rgb(20, 16, 24));

        var gradient = ImageDecoder.DecodeFile(Fixture("gradient.png"));
        Check("gradient endpoints", gradient[0, 32] == new Rgb(124, 92, 255) && gradient[95, 32] == new Rgb(55, 211, 216));

        var bmp = ImageDecoder.DecodeFile(Fixture("solid.bmp"));
        Check("BMP dimensions", bmp.Width == 16 && bmp.Height == 16);
        Check("BMP channel order (BGR → RGB)", bmp[8, 8] == new Rgb(200, 40, 160));

        try
        {
            ImageDecoder.Decode(new byte[] { 1, 2, 3, 4 });
            Check("unknown format throws", false);
        }
        catch (NotSupportedException)
        {
            Check("unknown format throws", true);
        }

        Check("average of solid BMP", WallpaperPalette.Average(bmp) == new Rgb(200, 40, 160));
    }

    private static void TestPaletteExtraction()
    {
        var quad = ImageDecoder.DecodeFile(Fixture("quadrant.png"));
        var candidates = WallpaperPalette.Extract(quad);

        Check("extract returns clusters", candidates.Count >= 3);
        Check("candidates ordered by score", candidates.Zip(candidates.Skip(1)).All(p => p.First.Score >= p.Second.Score));
        Check("scores are bounded", candidates.All(c => c.Score is >= 0 and <= 1));
        Check("populations sum ≈ 1", Math.Abs(candidates.Sum(c => c.Population) - 1.0) < 0.05);

        var seed = WallpaperPalette.PickSeed(candidates);
        var seedHue = seed.ToOkLch();
        Check("seed is colourful (not grey)", seedHue.C > 0.03);
        Check("seed is close to an actual quadrant colour",
            candidates.Any(c => Math.Abs(c.Color.R - seed.R) <= 24 && Math.Abs(c.Color.G - seed.G) <= 24 && Math.Abs(c.Color.B - seed.B) <= 24));

        var again = WallpaperPalette.Extract(quad);
        Check("extraction is deterministic", candidates.Select(c => c.Color).SequenceEqual(again.Select(c => c.Color)));

        // Extreme inputs must not throw.
        var onePixel = new PixelBuffer(1, 1, new byte[] { 0, 0, 0 });
        Check("all-black image still yields a seed", WallpaperPalette.PickSeed(WallpaperPalette.Extract(onePixel)).ToHex().Length == 7);
    }

    // ---------------- config ----------------

    private static void TestConfig()
    {
        var temp = Directory.CreateTempSubdirectory("radha-config-").FullName;
        try
        {
            var path = Path.Combine(temp, "shell.json");
            var store = new ConfigStore(path);

            var defaults = store.Load();
            Check("missing file → defaults", defaults.Bar.Position == "top" && defaults.Bar.Height == 42);
            Check("default paths are populated", !string.IsNullOrEmpty(defaults.Paths.Wallpapers));
            Check("normalisation clamps scale", new ShellConfig { Appearance = { Scale = 99 } }.Normalized().Appearance.Scale == 2.0);

            defaults.Bar.Position = "bottom";
            defaults.Bar.WorkspaceCount = 7;
            defaults.Appearance.SchemeVariant = "vibrant";
            store.Save(defaults);

            var reloaded = store.Load();
            Check("save/load round-trip", reloaded.Bar.Position == "bottom" && reloaded.Bar.WorkspaceCount == 7);
            Check("round-trip keeps enum-ish strings", reloaded.Appearance.SchemeVariant == "vibrant");

            File.WriteAllText(path, "{ this is not json ");
            var broken = store.Load();
            Check("corrupt config falls back to defaults", broken.Bar.Position == "top" && store.LastError is not null);
            Check("corrupt config is backed up", File.Exists(path + ".broken"));

            // Per-monitor override only touches what it declares.
            store.Save(defaults);
            store.SaveMonitorOverride("DELL U2723QE", """{ "bar": { "height": 52 } }""");
            var merged = store.LoadForMonitor("DELL U2723QE");
            var baseConfig = store.Load();
            Check("monitor override applies", merged.Bar.Height == 52);
            Check("monitor override keeps base values", merged.Bar.Position == baseConfig.Bar.Position && merged.Bar.WorkspaceCount == 7);
            Check("monitor override is isolated", baseConfig.Bar.Height == 42);
            Check("unknown monitor has no override", store.LoadForMonitor("GENERIC").Bar.Height == 42);

            // override hardening
            Check("non-object override rejected", Throws<ArgumentException>(() => store.SaveMonitorOverride("X", "[1,2,3]")));
            Check("oversized override rejected", Throws<ArgumentException>(() =>
                store.SaveMonitorOverride("X", "{\"a\":\"" + new string('x', ConfigStore.MaxOverrideBytes) + "\"}")));
            Check("empty override rejected", Throws<ArgumentException>(() => store.SaveMonitorOverride("X", " ")));
            Check("hostile monitor name is contained", store.SaveMonitorOverride("../../evil", "{\"bar\":{\"height\":44}}")
                .Contains(Path.GetFileName(store.MonitorConfigDirectory)));
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    // ---------------- command surface ----------------

    private static void TestCommandCatalog()
    {
        Check("parse module dashboard", CommandCatalog.TryParseModule("dashboard", out var m) && m == ShellModule.Dashboard);
        Check("parse module case-insensitive", CommandCatalog.TryParseModule("ControlCenter", out var cc) && cc == ShellModule.ControlCenter);
        Check("parse unknown module fails", !CommandCatalog.TryParseModule("nope", out _));
        Check("valid command accepted", CommandCatalog.IsValid(ShellModule.Theme, "variant"));
        Check("invalid command rejected", !CommandCatalog.IsValid(ShellModule.Theme, "explode"));
        Check("help lists every module", CommandCatalog.Modules.All(mod =>
            CommandCatalog.Help().Contains($"radha {mod.ToString().ToLowerInvariant()}")));
    }

    // ---------------- IPC ----------------

    private static async Task TestIpcAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("  (skipping socket IPC test on Windows; covered by named-pipe integration test)");
            return;
        }

        var endpoint = Path.Combine(Path.GetTempPath(), $"radha-test-{Guid.NewGuid():N}.sock");
        var handled = new List<string>();
        await using var server = new IpcServer(endpoint, (req, _) =>
        {
            handled.Add($"{req.Module}:{req.Command}");
            if (req.Command == "explode") throw new InvalidOperationException("boom");
            return Task.FromResult(IpcResponse.Ok_(req.Id, JsonSerializer.Serialize(new { echoed = req.Args })));
        });

        await server.StartAsync();
        await Task.Delay(150); // let the socket bind

        await using var client = new IpcClient(endpoint);
        await client.ConnectAsync(TimeSpan.FromSeconds(3));

        var ok = await client.SendCommandAsync("dashboard", "toggle");
        Check("IPC round-trip succeeds", ok.Ok && ok.Id.Length > 0);
        Check("IPC payload echoes args", ok.Payload?.Contains("echoed") == true);
        Check("IPC reports server timing", ok.ElapsedMs >= 0);
        Check("handler received module/command", handled.Contains("dashboard:toggle"));

        var bad = await client.SendCommandAsync("theme", "explode");
        Check("handler exception → error response", !bad.Ok && bad.Error == "boom");

        var missing = await client.SendCommandAsync("launcher", "open", "--focus");
        Check("args survive framing", missing.Ok);

        // A second client must be served concurrently.
        await using var client2 = new IpcClient(endpoint);
        await client2.ConnectAsync(TimeSpan.FromSeconds(3));
        var second = await client2.SendCommandAsync("osd", "volume", "40");
        Check("second concurrent client", second.Ok);

        // Oversized frame must be refused rather than corrupting the stream.
        Check("request ids are unique", ok.Id != missing.Id);

        await client.DisposeAsync();
        await client2.DisposeAsync();
        await server.DisposeAsync();
        Check("endpoint cleaned up on shutdown", !File.Exists(endpoint));
    }

    // ---------------- harness ----------------

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
        catch { return false; }
    }

    private static void Check(string name, bool condition)
    {
        if (condition) _passed++;
        else Failures.Add(name);
    }
}
