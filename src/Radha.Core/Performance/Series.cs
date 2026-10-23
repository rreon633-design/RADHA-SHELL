namespace Radha.Core.Performance;

/// <summary>
/// Fixed-capacity history with O(1) append and no allocation after construction.
///
/// Charts read this on every tick; a `List` + `RemoveAt(0)` would allocate and move the whole
/// buffer each frame, which is exactly the kind of thing a shell cannot afford at 1 Hz × 6 series.
/// </summary>
public sealed class Series
{
    private readonly double[] _buffer;
    private int _start;   // index of the oldest sample
    private int _count;

    public Series(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new double[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count => _count;

    public double Last => _count == 0 ? 0 : _buffer[(_start + _count - 1) % _buffer.Length];

    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;

        if (_count < _buffer.Length)
        {
            _buffer[(_start + _count) % _buffer.Length] = value;
            _count++;
            return;
        }

        _buffer[_start] = value;
        _start = (_start + 1) % _buffer.Length;
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }

    /// <summary>Copies oldest→newest into <paramref name="destination"/>. Returns the number written.</summary>
    public int CopyTo(Span<double> destination)
    {
        var n = Math.Min(destination.Length, _count);
        var skip = _count - n;
        for (var i = 0; i < n; i++)
            destination[i] = _buffer[(_start + skip + i) % _buffer.Length];
        return n;
    }

    public double[] ToArray()
    {
        var result = new double[_count];
        CopyTo(result);
        return result;
    }

    public double Min
    {
        get
        {
            if (_count == 0) return 0;
            var min = double.MaxValue;
            for (var i = 0; i < _count; i++) min = Math.Min(min, _buffer[(_start + i) % _buffer.Length]);
            return min;
        }
    }

    public double Max
    {
        get
        {
            if (_count == 0) return 0;
            var max = double.MinValue;
            for (var i = 0; i < _count; i++) max = Math.Max(max, _buffer[(_start + i) % _buffer.Length]);
            return max;
        }
    }

    public double Average
    {
        get
        {
            if (_count == 0) return 0;
            double sum = 0;
            for (var i = 0; i < _count; i++) sum += _buffer[(_start + i) % _buffer.Length];
            return sum / _count;
        }
    }

    /// <summary>
    /// Normalises to 0..1 for drawing. A flat series renders mid-height rather than collapsing to
    /// the baseline, which is what a 0 % CPU chart should look like.
    /// </summary>
    public void Normalized(Span<float> destination, double floor = 0, double ceiling = 100)
    {
        var n = Math.Min(destination.Length, _count);
        var skip = _count - n;
        var range = ceiling - floor;
        if (range <= 0) range = 1;

        for (var i = 0; i < n; i++)
        {
            var value = _buffer[(_start + skip + i) % _buffer.Length];
            var normalised = (value - floor) / range;
            destination[i] = (float)Math.Clamp(normalised, 0, 1);
        }
    }
}
