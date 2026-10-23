using System.Diagnostics;
using System.Text.Json;
using Radha.Core.Config;
using Radha.Core.Ipc;
using Radha.Core.Performance;
using Radha.Core.Search;
using Radha.Core.Theming;

namespace Radha.Cli;

/// <summary>
/// <c>radha bench</c> — micro-benchmarks that gate performance regressions in CI.
/// Runs offline (no daemon): palette extraction, scheme generation, launcher ranking,
/// config I/O and an in-process IPC loopback.
/// </summary>
public static class Bench
{
    private sealed record Result(string Name, int Iterations, double MeanMs, double P50Ms, double P95Ms, double MaxMs, double BudgetP95Ms, string Note);

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        var json = args.Contains("--json");
        var gate = args.Contains("--gate");

        var fixture = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        if (fixture is null)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "fixtures", "large.png"),
                Path.Combine(AppContext.BaseDirectory, "fixtures", "quadrant.png"),
                "src/Radha.Tests/fixtures/quadrant.png"
            };
            fixture = candidates.FirstOrDefault(File.Exists);
        }

        var results = new List<Result>();

        // 1. Palette extraction (the most expensive pure-C# path in the shell).
        if (fixture is not null && File.Exists(fixture))
        {
            var image = ImageDecoder.DecodeFile(fixture);
            var recorder = new LatencyRecorder();
            for (var i = 0; i < 25; i++) recorder.Measure(() => WallpaperPalette.Extract(image));
            results.Add(new Result("palette.extract", 25, recorder.Mean, recorder.Percentile(50),
                recorder.Percentile(95), recorder.Max, 120, $"{image.Width}x{image.Height}"));
        }

        // 2. Scheme generation from one seed (runs on every wallpaper/theme change).
        {
            var seed = new Rgb(124, 92, 255);
            var recorder = new LatencyRecorder();
            for (var i = 0; i < 500; i++)
                recorder.Measure(() => MaterialScheme.FromSeed(seed, i % 2 == 0, SchemeVariant.Vibrant));
            results.Add(new Result("scheme.generate", 500, recorder.Mean, recorder.Percentile(50),
                recorder.Percentile(95), recorder.Max, 5, "35 roles"));
        }

        // 3. Launcher ranking (fires on every keystroke).
        {
            var apps = Enumerable.Range(0, 5_000).Select(i => $"Application Number {i} Pro").ToArray();
            var recorder = new LatencyRecorder();
            for (var i = 0; i < 50; i++)
                recorder.Measure(() => FuzzyMatcher.Rank(apps, "app", x => x, 12));
            results.Add(new Result("launcher.rank", 50, recorder.Mean, recorder.Percentile(50),
                recorder.Percentile(95), recorder.Max, 40, "5,000 apps"));
        }

        // 4. Config round-trip (save + load includes atomic swap).
        {
            var temp = Path.Combine(Path.GetTempPath(), "radha-bench-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(temp);
            try
            {
                var store = new ConfigStore(Path.Combine(temp, "shell.json"));
                var config = ConfigStore.Defaults();
                var recorder = new LatencyRecorder();
                for (var i = 0; i < 100; i++)
                {
                    config.Bar.Height = 42;
                    config.Appearance.SchemeVariant = i % 2 == 0 ? "vibrant" : "tonalSpot";
                    recorder.Measure(() => { store.Save(config); return store.Load(); });
                }
                results.Add(new Result("config.roundtrip", 100, recorder.Mean, recorder.Percentile(50),
                    recorder.Percentile(95), recorder.Max, 25, "save+load"));
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        // 5. IPC loopback (framing + validation + handler, same process).
        if (!OperatingSystem.IsWindows())
        {
            var endpoint = Path.Combine(Path.GetTempPath(), $"radha-bench-{Guid.NewGuid():N}.sock");
            var server = new IpcServer(endpoint, (request, _) => Task.FromResult(IpcResponse.Ok_(request.Id, "ok")),
                new IpcServerOptions { Limiter = null, RestrictToCurrentUser = true });
            try
            {
                await server.StartAsync();
                await Task.Delay(80, ct);
                await using var client = new IpcClient(endpoint);
                await client.ConnectAsync(TimeSpan.FromSeconds(3), ct);

                var recorder = new LatencyRecorder();
                for (var i = 0; i < 200; i++)
                {
                    await recorder.MeasureAsync(() => client.SendCommandAsync("system", "status"));
                }
                results.Add(new Result("ipc.roundtrip", 200, recorder.Mean, recorder.Percentile(50),
                    recorder.Percentile(95), recorder.Max, 15, "in-process"));
            }
            finally
            {
                await server.DisposeAsync();
            }
        }

        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow,
                runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(),
                results = results.Select(r => new
                {
                    name = r.Name,
                    iterations = r.Iterations,
                    meanMs = Math.Round(r.MeanMs, 3),
                    p50Ms = Math.Round(r.P50Ms, 3),
                    p95Ms = Math.Round(r.P95Ms, 3),
                    maxMs = Math.Round(r.MaxMs, 3),
                    budgetP95Ms = r.BudgetP95Ms,
                    note = r.Note,
                    pass = r.P95Ms <= r.BudgetP95Ms
                })
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            stdout.WriteLine("RADHA SHELL · benchmarks");
            stdout.WriteLine($"runtime {Environment.Version} · {Environment.OSVersion}");
            stdout.WriteLine();
            stdout.WriteLine($"{"benchmark",-20}{"n",6}{"mean",10}{"p50",10}{"p95",10}{"max",10}{"budget",9}  result");
            foreach (var r in results)
            {
                var pass = r.P95Ms <= r.BudgetP95Ms;
                stdout.WriteLine($"{r.Name,-20}{r.Iterations,6}{r.MeanMs,9:F2}ms{r.P50Ms,9:F2}ms{r.P95Ms,9:F2}ms{r.MaxMs,9:F2}ms{r.BudgetP95Ms,7:F0}ms  {(pass ? "PASS" : "FAIL")}  {r.Note}");
            }
        }

        var failures = gate ? results.Count(r => r.P95Ms > r.BudgetP95Ms) : 0;
        if (failures > 0)
        {
            stderr.WriteLine($"{failures} benchmark(s) exceeded their p95 budget");
            return 1;
        }
        return 0;
    }

    private static async Task<T> MeasureAsync<T>(this LatencyRecorder recorder, Func<Task<T>> action)
    {
        var started = Stopwatch.GetTimestamp();
        try { return await action(); }
        finally { recorder.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }
}
