namespace Radha.Core.Power;

/// <summary>A battery threshold crossing, fired once per descent.</summary>
public sealed record BatteryWarning(int Level, bool Critical, string Title, string Message);

/// <summary>
/// Battery thresholds with hysteresis.
///
/// The point of this type is the re-arm rule: a laptop hovering at 19–20 % must not produce a
/// notification every sample. Each level fires once, and only re-arms after the charge rises
/// <see cref="RearmMargin"/> points above it (or the machine starts charging).
/// </summary>
public sealed class BatteryWatch
{
    public const int RearmMargin = 5;

    private readonly int _low;
    private readonly int _critical;
    private bool _lowFired;
    private bool _criticalFired;

    public BatteryWatch(int low = 20, int critical = 10)
    {
        if (critical < 0) throw new ArgumentOutOfRangeException(nameof(critical));
        if (low <= critical) throw new ArgumentException("low must be above critical", nameof(low));
        _low = low;
        _critical = critical;
    }

    /// <summary>Feed a sample. Returns a warning on the exact sample where a threshold is crossed.</summary>
    public BatteryWarning? Update(int? percent, bool charging)
    {
        if (percent is not { } value) return null;

        if (charging || value >= _low + RearmMargin)
        {
            _lowFired = false;
            _criticalFired = false;
            return null;
        }

        if (value <= _critical && !_criticalFired)
        {
            _criticalFired = true;
            _lowFired = true;               // don't follow critical with a "low" toast
            return new BatteryWarning(_critical, true,
                $"Battery critical — {value}%",
                "Plug in now. Background services are the first to be suspended.");
        }

        if (value <= _low && !_lowFired)
        {
            _lowFired = true;
            return new BatteryWarning(_low, false,
                $"Battery low — {value}%",
                "Consider plugging in. Radha drops to a reduced refresh rate below 15%.");
        }

        return null;
    }

    /// <summary>Current reduced-power state, used to decide whether to slow polling.</summary>
    public bool InReducedPower(int? percent, bool charging) => !charging && percent is { } p && p <= _low;
}

public enum IdleAction { None, DimDisplay, TurnOffDisplay }

public readonly record struct IdleInputs(
    TimeSpan IdleFor,
    bool MediaPlaying,
    bool Charging,
    bool FullscreenApp,
    bool UserInhibited);

public sealed record IdleConfig(
    bool Enabled = true,
    bool InhibitWhenAudio = true,
    bool InhibitWhenCharging = false,
    bool InhibitInFullscreen = true,
    TimeSpan DisplayOffAfter = default,
    bool DimInsteadOfOff = true)
{
    public static IdleConfig Default { get; } = new(DisplayOffAfter: TimeSpan.FromMinutes(10));
}

public readonly record struct IdleDecision(
    bool InhibitDisplay,
    bool InhibitSystem,
    IdleAction Action,
    string Reason);

/// <summary>
/// Decides whether the shell should keep the display awake and whether it should ask Windows to
/// power it down. Deliberately does **not** lock the session: locking is out of scope for this
/// project by instruction, so <see cref="IdleAction"/> only ever dims or powers off the display.
/// </summary>
public static class IdlePolicy
{
    public static IdleDecision Evaluate(IdleInputs inputs, IdleConfig config)
    {
        if (!config.Enabled)
            return new IdleDecision(false, false, IdleAction.None, "idle policy disabled");

        if (inputs.UserInhibited)
            return new IdleDecision(true, true, IdleAction.None, "inhibited by user");

        if (config.InhibitWhenAudio && inputs.MediaPlaying)
            return new IdleDecision(true, true, IdleAction.None, "media playing");

        if (config.InhibitInFullscreen && inputs.FullscreenApp)
            return new IdleDecision(true, false, IdleAction.None, "fullscreen application");

        if (config.InhibitWhenCharging && inputs.Charging)
            return new IdleDecision(true, true, IdleAction.None, "charging");

        if (config.DisplayOffAfter <= TimeSpan.Zero)
            return new IdleDecision(false, false, IdleAction.None, "no timeout configured");

        if (inputs.IdleFor < config.DisplayOffAfter)
            return new IdleDecision(false, false, IdleAction.None, "active");

        return new IdleDecision(false, false,
            config.DimInsteadOfOff ? IdleAction.DimDisplay : IdleAction.TurnOffDisplay,
            config.DimInsteadOfOff ? "idle: dimming" : "idle: display off");
    }
}

/// <summary>
/// Polling cadence derived from the machine's state. Every service that samples on a timer asks
/// this instead of hard-coding an interval, so a laptop on battery genuinely costs less.
/// </summary>
public static class RefreshProfile
{
    public static TimeSpan For(bool onBattery, bool reducedPower, bool windowVisible, TimeSpan normal)
    {
        var factor = 1.0;
        if (onBattery) factor *= 2;
        if (reducedPower) factor *= 3;
        if (!windowVisible) factor *= 4;
        return TimeSpan.FromTicks((long)(normal.Ticks * factor));
    }
}
