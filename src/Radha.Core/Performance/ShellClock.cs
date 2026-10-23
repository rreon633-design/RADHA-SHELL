using System.Diagnostics;

namespace Radha.Core.Performance;

/// <summary>
/// One clock for the whole shell.
///
/// Every widget used to own a timer, so a static bar still woke six dispatchers a second and the
/// UI thread received a burst of unrelated invalidations. This runs a single timer and hands every
/// subscriber the same instant; a handler that overruns the interval causes the next tick to be
/// skipped rather than queued, so a slow frame cannot snowball into a backlog.
/// </summary>
public sealed class ShellClock : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private Timer? _timer;
    private int _inTick;
    private long _ticks;
    private long _skipped;
    private bool _disposed;

    public ShellClock(TimeSpan? interval = null)
    {
        _interval = interval ?? TimeSpan.FromSeconds(1);
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
    }

    /// <summary>Raised once per interval, with the wall-clock instant the tick represents.</summary>
    public event Action<DateTimeOffset>? Tick;

    public TimeSpan Interval => _interval;
    public long Ticks => Interlocked.Read(ref _ticks);
    public long Skipped => Interlocked.Read(ref _skipped);
    public int SubscriberCount => Tick?.GetInvocationList().Length ?? 0;
    public bool IsRunning => _timer is not null;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _timer ??= new Timer(_ => Fire(), null, _interval, _interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Runs the handlers once, immediately. Used by tests and by "refresh now" actions.</summary>
    public void Pulse() => Fire();

    private void Fire()
    {
        // Overrun protection: a tick that takes longer than the interval must not stack up.
        if (Interlocked.Exchange(ref _inTick, 1) == 1)
        {
            Interlocked.Increment(ref _skipped);
            return;
        }

        try
        {
            var handlers = Tick;
            if (handlers is null) return;

            var now = DateTimeOffset.Now;
            var started = Stopwatch.GetTimestamp();

            foreach (Action<DateTimeOffset> handler in handlers.GetInvocationList())
            {
                try { handler(now); }
                catch (Exception) { /* one bad subscriber must not stop the clock */ }
            }

            Interlocked.Increment(ref _ticks);
            _ = Stopwatch.GetElapsedTime(started);
        }
        finally
        {
            Interlocked.Exchange(ref _inTick, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
