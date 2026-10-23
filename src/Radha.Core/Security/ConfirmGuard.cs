namespace Radha.Core.Security;

/// <summary>
/// Two-step confirmation for destructive actions, with a deadline.
///
/// "Are you sure?" dialog boxes invite reflexive clicking, and a shell that can reboot the machine
/// needs better than that. Instead the first activation arms the action and the second, within
/// <see cref="Window"/>, performs it. Nothing is modal, and a forgotten arm expires by itself.
/// </summary>
public sealed class ConfirmGuard
{
    private string? _action;
    private DateTime _armedAt;

    public ConfirmGuard(TimeSpan? window = null)
    {
        Window = window ?? TimeSpan.FromSeconds(4);
        if (Window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
    }

    public TimeSpan Window { get; }

    /// <summary>What is currently armed, for the button label. Null when idle.</summary>
    public string? ArmedAction => _action;

    /// <summary>
    /// True when this activation should actually run the action.
    ///
    /// The first call arms and returns false; a second call for the same action inside the window
    /// returns true and disarms. A different action replaces the arm rather than confirming the old
    /// one, so a mis-aimed click can never upgrade into a reboot.
    /// </summary>
    public bool Confirm(string action, DateTime? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var stamp = now ?? DateTime.UtcNow;

        if (_action is not null &&
            string.Equals(_action, action, StringComparison.Ordinal) &&
            stamp - _armedAt <= Window)
        {
            Reset();
            return true;
        }

        _action = action;
        _armedAt = stamp;
        return false;
    }

    public void Reset()
    {
        _action = null;
        _armedAt = default;
    }

    /// <summary>Seconds left on the arm, or 0 when idle; used to draw a countdown ring.</summary>
    public double RemainingSeconds(DateTime? now = null)
    {
        if (_action is null) return 0;
        var left = Window - ((now ?? DateTime.UtcNow) - _armedAt);
        return left <= TimeSpan.Zero ? 0 : left.TotalSeconds;
    }
}
