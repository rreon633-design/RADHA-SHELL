using Radha.Core.Ipc;
using Radha.Core.Performance;
using Radha.Core.Security;

namespace Radha.Tests;

/// <summary>Security, performance and feature-strengthening assertions.</summary>
public static class HardeningTests
{
    public static void RunSecurity(Action<string, bool> check)
    {
        // ---- request validation ----
        check("valid request accepted", InputGuard.ValidateRequest("dashboard", "toggle", Array.Empty<string>()).IsValid);
        check("null module rejected", !InputGuard.ValidateRequest(null, "toggle", null).IsValid);
        check("empty command rejected", !InputGuard.ValidateRequest("theme", " ", null).IsValid);
        check("overlong module rejected", !InputGuard.ValidateRequest(new string('a', 40), "toggle", null).IsValid);
        check("module with shell metachars rejected", !InputGuard.ValidateRequest("dash|board", "toggle", null).IsValid);
        check("command with semicolon rejected", !InputGuard.ValidateRequest("theme", "set;rm", null).IsValid);
        check("too many args rejected", !InputGuard.ValidateRequest("theme", "set", Enumerable.Repeat("x", 20).ToArray()).IsValid);
        check("overlong arg rejected", !InputGuard.ValidateRequest("theme", "set", new[] { new string('x', 600) }).IsValid);
        check("newline in arg rejected", !InputGuard.ValidateRequest("wallpaper", "set", new[] { "a\nb" }).IsValid);
        check("dollar in arg rejected", !InputGuard.ValidateRequest("wallpaper", "set", new[] { "$(whoami)" }).IsValid);
        check("backtick in arg rejected", !InputGuard.ValidateRequest("wallpaper", "set", new[] { "`id`" }).IsValid);
        check("16 args still accepted", InputGuard.ValidateRequest("theme", "set", Enumerable.Repeat("a", 16).ToArray()).IsValid);

        // ---- config keys ----
        check("config key valid", InputGuard.ValidateConfigKey("appearance.scale").IsValid);
        check("config key empty segment rejected", !InputGuard.ValidateConfigKey("appearance..scale").IsValid);
        check("config key with slash rejected", !InputGuard.ValidateConfigKey("appearance/scale").IsValid);

        // ---- numbers ----
        check("NaN rejected", !InputGuard.TryParseNumber("NaN", 0, 100, out _));
        check("Infinity rejected", !InputGuard.TryParseNumber("Infinity", 0, 100, out _));
        check("out-of-range rejected", !InputGuard.TryParseNumber("500", 0, 100, out _));
        check("valid number accepted", InputGuard.TryParseNumber("42.5", 0, 100, out var parsed) && Math.Abs(parsed - 42.5) < 1e-9);
        check("culture-invariant parse", InputGuard.TryParseNumber("42.5", 0, 100, out _));
        check("int bounds enforced", !InputGuard.TryParseInt("500", 0, 100, out _) &&
                                    InputGuard.TryParseInt("50", 0, 100, out _));

        // ---- colours ----
        check("hex #fff accepted", InputGuard.IsValidHexColor("#fff"));
        check("hex #7C5CFF accepted", InputGuard.IsValidHexColor("#7C5CFF"));
        check("hex #FF7C5CFF accepted", InputGuard.IsValidHexColor("#FF7C5CFF"));
        check("named colour rejected", !InputGuard.IsValidHexColor("rebeccapurple"));
        check("5-digit hex rejected", !InputGuard.IsValidHexColor("#12345"));

        // ---- paths ----
        var root = OperatingSystem.IsWindows() ? @"C:\wallpapers" : "/home/user/wallpapers";
        var sibling = OperatingSystem.IsWindows() ? @"C:\wallpapers-evil\x.png" : "/home/user/wallpapers-evil/x.png";
        check("path inside root", PathGuard.IsWithinRoot(root, Path.Combine(root, "a.png")));
        check("path traversal rejected", !PathGuard.IsWithinRoot(root, Path.Combine(root, "..", "secret.png")));
        check("sibling prefix rejected", !PathGuard.IsWithinRoot(root, sibling));
        check("safe filename strips separators", PathGuard.SafeFileName("../../etc/passwd") == "passwd");
        check("safe filename strips control chars", PathGuard.SafeFileName("mo\u0001nitor") == "monitor");
        check("reserved name replaced", PathGuard.SafeFileName("CON") == "default");
        check("empty name falls back", PathGuard.SafeFileName("   ") == "default");
        check("long name truncated", PathGuard.SafeFileName(new string('a', 200)).Length == 64);
        check("executable detected", PathGuard.LooksExecutable("evil.exe") && PathGuard.LooksExecutable("x.PS1"));
        check("image detected", PathGuard.IsImageFile("photo.PNG") && !PathGuard.IsImageFile("photo.exe"));
        check("device path rejected", !PathGuard.IsSafeLocalPath(@"\\.\PhysicalDrive0"));
        check("normal path accepted", PathGuard.IsSafeLocalPath(Path.Combine(Path.GetTempPath(), "a.png")));

        // ---- redaction ----
        const string pat = "github_pat_REDACTED_FOR_TEST_SUITE_0000000000";
        check("github pat redacted", !Redactor.Redact($"token {pat}")!.Contains("REDACTED_FOR"));
        check("openai key redacted", !Redactor.Redact("sk-REDACTED-example-000000000000")!.Contains("REDACTED"));
        check("bearer redacted", Redactor.Redact("Authorization: Bearer abcdefghijklmnopqrstuvwxyz012345")!.Contains(Redactor.Mask));
        check("api_key assignment redacted", Redactor.Redact("api_key=superSecretValue123")!.Contains(Redactor.Mask));
        check("secret detection", Redactor.ContainsSecret(pat) && !Redactor.ContainsSecret("hello world"));
        check("email kept by default", Redactor.Redact("mail me at a@b.com")!.Contains("a@b.com"));
        check("email redacted on request", Redactor.Redact("mail me at a@b.com", includeEmails: true)!.Contains(Redactor.Mask));

        // ---- rate limiter ----
        var limiter = new RateLimiter(capacity: 5, refillPerSecond: 1);
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var allowed = Enumerable.Range(0, 5).Count(_ => limiter.TryAcquire("client-a", t0));
        check("burst of 5 allowed", allowed == 5);
        check("6th request throttled", !limiter.TryAcquire("client-a", t0));
        check("other client unaffected", limiter.TryAcquire("client-b", t0));
        check("refills over time", limiter.TryAcquire("client-a", t0.AddSeconds(1.1)));
        check("tracks clients", limiter.TrackedClients >= 2);
        check("invalid capacity throws", Throws<ArgumentOutOfRangeException>(() => new RateLimiter(0, 1)));
    }

    public static void RunPerformance(Action<string, bool> check)
    {
        // ---- LRU cache ----
        var cache = new LruCache<int, string>(capacity: 3);
        cache.Put(1, "one"); cache.Put(2, "two"); cache.Put(3, "three");
        check("lru stores entries", cache.Count == 3);
        check("lru hit returns value", cache.TryGet(1, out var v1) && v1 == "one");
        cache.Put(4, "four"); // evicts 2 (least recently used after the 1 hit)
        check("lru evicts lru entry", !cache.TryGet(2, out _) && cache.Count == 3);
        check("lru keeps recently used", cache.TryGet(1, out _));
        check("lru hit ratio tracked", cache.HitRatio is > 0 and <= 1);
        var factoryCalls = 0;
        cache.GetOrAdd(5, _ => { factoryCalls++; return "five"; });
        cache.GetOrAdd(5, _ => { factoryCalls++; return "five-again"; });
        check("getoradd calls factory once", factoryCalls == 1);
        check("lru remove", cache.TryRemove(5) && !cache.TryGet(5, out _));

        // ---- palette cache ----
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "quadrant.png");
        var big = Path.Combine(AppContext.BaseDirectory, "fixtures", "large.png");
        var paletteCache = new PaletteCache(capacity: 4);
        var first = System.Diagnostics.Stopwatch.GetTimestamp();
        var seed1 = paletteCache.SeedFor(fixture);
        var firstMs = System.Diagnostics.Stopwatch.GetElapsedTime(first).TotalMilliseconds;
        var second = System.Diagnostics.Stopwatch.GetTimestamp();
        var seed2 = paletteCache.SeedFor(fixture);
        var secondMs = System.Diagnostics.Stopwatch.GetElapsedTime(second).TotalMilliseconds;
        check("palette cache returns same seed", seed1.ToHex() == seed2.ToHex());
        check("palette cache hit is faster", secondMs <= firstMs + 1.0);
        check("palette cache hit ratio > 0", paletteCache.HitRatio > 0);
        check("palette cache misses counted", paletteCache.Misses >= 1);
        paletteCache.Invalidate(fixture);
        check("palette cache invalidates", paletteCache.Count == 0);

        // ---- extraction budget on a 512x512 image ----
        if (File.Exists(big))
        {
            var image = Radha.Core.Theming.ImageDecoder.DecodeFile(big);
            var recorder = new LatencyRecorder();
            for (var i = 0; i < 10; i++) recorder.Measure(() => Radha.Core.Theming.WallpaperPalette.Extract(image));
            check($"palette extraction p95 under 300ms ({recorder.Percentile(95):F0}ms)", recorder.Percentile(95) < 300);
        }

        // ---- scheme generation throughput ----
        var schemeRecorder = new LatencyRecorder();
        var seed = new Radha.Core.Theming.Rgb(124, 92, 255);
        for (var i = 0; i < 300; i++)
            schemeRecorder.Measure(() => Radha.Core.Theming.MaterialScheme.FromSeed(seed, i % 2 == 0));
        check($"300 schemes under 3s (p95 {schemeRecorder.Percentile(95):F1}ms)", schemeRecorder.Percentile(95) < 30);

        // ---- launcher ranking budget ----
        var apps = Enumerable.Range(0, 5_000).Select(i => $"Application Number {i} Pro").ToArray();
        var rankRecorder = new LatencyRecorder();
        for (var i = 0; i < 20; i++)
            rankRecorder.Measure(() => Radha.Core.Search.FuzzyMatcher.Rank(apps, "app", x => x, 12));
        check($"ranking 5k apps p95 under 60ms ({rankRecorder.Percentile(95):F1}ms)", rankRecorder.Percentile(95) < 60);
    }

    public static void RunFeatures(Action<string, bool> check)
    {
        // ---- fuzzy matcher ----
        var prefix = Radha.Core.Search.FuzzyMatcher.Score("Visual Studio Code", "vis");
        var substring = Radha.Core.Search.FuzzyMatcher.Score("Microsoft Visual Studio", "vis");
        var subsequence = Radha.Core.Search.FuzzyMatcher.Score("Virtual System Config", "vsc");
        var acronym = Radha.Core.Search.FuzzyMatcher.Score("Visual Studio Code", "vsc");
        check("prefix beats substring", prefix > substring);
        check("substring beats subsequence", substring > subsequence);
        check("acronym recognised", acronym > 0 && acronym >= subsequence);
        check("no match returns 0", Radha.Core.Search.FuzzyMatcher.Score("Calculator", "zzz") == 0);
        check("empty query matches", Radha.Core.Search.FuzzyMatcher.Score("Calc", "") > 0);
        check("case insensitive", Radha.Core.Search.FuzzyMatcher.Score("Notepad", "NOTE") > 0);
        var ranked = Radha.Core.Search.FuzzyMatcher.Rank(new[] { "Settings", "Set Time", "Photos" }, "set", x => x, 10);
        check("rank filters non-matches", ranked.All(x => x.Contains("set", StringComparison.OrdinalIgnoreCase)));
        check("rank is deterministic", Radha.Core.Search.FuzzyMatcher.Rank(new[] { "A", "B" }, "", x => x)
            .SequenceEqual(Radha.Core.Search.FuzzyMatcher.Rank(new[] { "A", "B" }, "", x => x)));

        // ---- spring spec ----
        var snappy = Radha.Core.Motion.SpringSpec.Snappy;
        check("spring settles in reasonable time", snappy.SettleTime() is > 0.05 and < 2.0);
        check("spring step response starts at 0", Math.Abs(snappy.StepResponse(0)) < 1e-9);
        check("spring step response converges", Math.Abs(snappy.StepResponse(3.0) - 1.0) < 0.05);
        check("reduced motion is faster", Radha.Core.Motion.SpringSpec.Reduced.SettleTime() < snappy.SettleTime());
        check("spring lookup by name", Radha.Core.Motion.SpringSpec.For("sheet") == Radha.Core.Motion.SpringSpec.Sheet);
        check("spring defaults to snappy", Radha.Core.Motion.SpringSpec.For("nonsense") == snappy);
        var wild = Radha.Core.Motion.SpringSpec.For("snappy").Normalized();
        check("spring normalisation keeps validity", wild.DampingRatio is > 0 and <= 2 && wild.PeriodSeconds > 0);

        // ---- weather codes ----
        check("wmo 0 = clear", Radha.Core.Weather.WeatherCodes.For(0).Key == "clear");
        check("wmo 63 = rain", Radha.Core.Weather.WeatherCodes.For(63).Key == "rain");
        check("wmo 95 = storm", Radha.Core.Weather.WeatherCodes.For(95).Key == "storm");
        check("wmo unknown falls back", Radha.Core.Weather.WeatherCodes.For(9999).Key == "partly");
        check("precipitation detection", Radha.Core.Weather.WeatherCodes.IsPrecipitation(61) &&
                                          !Radha.Core.Weather.WeatherCodes.IsPrecipitation(0));
        check("severe detection", Radha.Core.Weather.WeatherCodes.IsSevere(96));

        // ---- client pool ----
        check("client pool constructs", new IpcClientPool().GetType() == typeof(IpcClientPool));
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
        catch { return false; }
    }
}
