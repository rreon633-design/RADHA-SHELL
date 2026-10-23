using System.Diagnostics;

namespace Radha.Core.Performance;

/// <summary>
/// Rolling latency recorder with percentile output. Used by the CLI benchmark and CI perf gate
/// instead of a heavyweight benchmarking package.
/// </summary>
public sealed class LatencyRecorder
{
    private readonly object _gate = new();
    private readonly List<double> _samples = new();
    private readonly int _capacity;

    public LatencyRecorder(int capacity = 10_000) => _capacity = capacity;

    public int Count { get { lock (_gate) return _samples.Count; } }

    public void Add(double milliseconds)
    {
        lock (_gate)
        {
            if (_samples.Count >= _capacity) _samples.RemoveAt(0);
            _samples.Add(milliseconds);
        }
    }

    public T Measure<T>(Func<T> action)
    {
        var started = Stopwatch.GetTimestamp();
        try { return action(); }
        finally
        {
            Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public void Measure(Action action) => Measure(() => { action(); return 0; });

    public double Percentile(double percentile)
    {
        List<double> snapshot;
        lock (_gate) snapshot = _samples.ToList();
        if (snapshot.Count == 0) return 0;

        snapshot.Sort();
        var rank = Math.Clamp(percentile, 0, 100) / 100.0 * (snapshot.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high) return snapshot[low];
        var weight = rank - low;
        return snapshot[low] * (1 - weight) + snapshot[high] * weight;
    }

    public double Mean
    {
        get
        {
            lock (_gate) return _samples.Count == 0 ? 0 : _samples.Average();
        }
    }

    public double Max { get { lock (_gate) return _samples.Count == 0 ? 0 : _samples.Max(); } }
    public double Min { get { lock (_gate) return _samples.Count == 0 ? 0 : _samples.Min(); } }

    public string Summary()
    {
        var count = Count;
        if (count == 0) return "no samples";
        return $"n={count} mean={Mean:F3}ms p50={Percentile(50):F3}ms p95={Percentile(95):F3}ms p99={Percentile(99):F3}ms max={Max:F3}ms";
    }

    public void Reset() { lock (_gate) _samples.Clear(); }
}
