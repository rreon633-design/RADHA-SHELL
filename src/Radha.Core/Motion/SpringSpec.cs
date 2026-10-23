namespace Radha.Core.Motion;

/// <summary>
/// Spring parameters for the Compositor's natural-motion animations.
///
/// WinUI's <c>SpringScalarNaturalMotionAnimation</c> takes damping ratio + period; these
/// presets mirror the feel of Caelestia's Anim/CAnim components (snappy pills, softer sheets)
/// and expose the analytic step response so the UI can pick a duration for reduced-motion users.
/// </summary>
public readonly record struct SpringSpec(double DampingRatio, double PeriodSeconds, string Name)
{
    public static readonly SpringSpec Snappy = new(0.85, 0.32, "snappy");      // pills, toggles
    public static readonly SpringSpec Sheet = new(0.92, 0.46, "sheet");        // dashboard, control centre
    public static readonly SpringSpec Gentle = new(0.97, 0.60, "gentle");      // OSD, tooltips
    public static readonly SpringSpec Bouncy = new(0.55, 0.38, "bouncy");      // launcher open
    public static readonly SpringSpec Reduced = new(1.0, 0.10, "reduced");     // prefers-reduced-motion

    public static SpringSpec For(string? name) => (name ?? "").ToLowerInvariant() switch
    {
        "sheet" => Sheet,
        "gentle" => Gentle,
        "bouncy" => Bouncy,
        "reduced" => Reduced,
        _ => Snappy
    };

    /// <summary>Clamped copy that is always physically valid.</summary>
    public SpringSpec Normalized() => new(
        Math.Clamp(DampingRatio, 0.05, 1.5),
        Math.Clamp(PeriodSeconds, 0.05, 3.0),
        Name);

    public double AngularFrequency => 2 * Math.PI / Math.Clamp(PeriodSeconds, 0.05, 3.0);

    /// <summary>Time (seconds) until the response stays within ±2% of the target.</summary>
    public double SettleTime(double tolerance = 0.02)
    {
        var zeta = Math.Clamp(DampingRatio, 0.05, 1.5);
        var omega = AngularFrequency;
        tolerance = Math.Clamp(tolerance, 0.001, 0.5);

        if (zeta >= 1.0) return 4.0 / omega;
        var decay = zeta * omega;
        var oscillation = omega * Math.Sqrt(1 - zeta * zeta);

        // Envelope decays as e^(-decay*t); solve for e^(-decay*t) = tolerance.
        var envelope = Math.Log(1.0 / tolerance) / decay;
        // One extra oscillation period makes the "stays within" claim true for underdamped springs.
        return envelope + Math.PI / Math.Max(oscillation, 1e-6);
    }

    /// <summary>Normalised step response at time t (1.0 = settled). Useful for tests and previews.</summary>
    public double StepResponse(double t)
    {
        var zeta = Math.Clamp(DampingRatio, 0.05, 1.5);
        var omega = AngularFrequency;
        if (t <= 0) return 0;

        if (zeta >= 1.0)
        {
            // Critically damped: 1 - (1 + ωt)e^(-ωt)
            return 1 - (1 + omega * t) * Math.Exp(-omega * t);
        }

        var decay = zeta * omega;
        var damped = omega * Math.Sqrt(1 - zeta * zeta);
        return 1 - Math.Exp(-decay * t) *
                   (Math.Cos(damped * t) + decay / damped * Math.Sin(damped * t));
    }
}
