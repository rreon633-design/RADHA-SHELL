namespace Radha.Core.Theming;

/// <summary>How dense the interface is. Everything spatial derives from this.</summary>
public enum Density { Compact, Comfortable }

/// <summary>
/// The design-token layer.
///
/// Every size in the UI comes from here rather than from a literal in a view: a nine-step spacing
/// scale, the same for padding and corner radius, plus typography. Each scale has a multiplier so
/// a single config value resizes the whole shell coherently — the thing "rounding scale" alone
/// could never do.
/// </summary>
public sealed record DesignTokens(
    double Scale = 1,
    double SpacingScale = 1,
    double PaddingScale = 1,
    double RoundingScale = 1,
    Density Density = Density.Comfortable,
    double BaseFontSize = 13,
    double FontScale = 1,
    string FontFamily = "Segoe UI Variable Text",
    string MonoFamily = "Cascadia Mono")
{
    public static DesignTokens Default { get; } = new();

    /// <summary>Extra multiplier applied to every spatial token.</summary>
    public double DensityFactor => Density == Density.Compact ? 0.78 : 1.0;

    private double Spatial(double value) => value * Scale * DensityFactor;

    // ---- spacing (gaps between things) ----
    public double SpacingXs => Math.Round(Spatial(2 * SpacingScale), 2);
    public double SpacingSm => Math.Round(Spatial(4 * SpacingScale), 2);
    public double SpacingMd => Math.Round(Spatial(8 * SpacingScale), 2);
    public double SpacingLg => Math.Round(Spatial(12 * SpacingScale), 2);
    public double SpacingXl => Math.Round(Spatial(16 * SpacingScale), 2);
    public double Spacing2Xl => Math.Round(Spatial(24 * SpacingScale), 2);
    public double Spacing3Xl => Math.Round(Spatial(32 * SpacingScale), 2);

    // ---- padding (space inside things) ----
    public double PaddingSm => Math.Round(Spatial(6 * PaddingScale), 2);
    public double PaddingMd => Math.Round(Spatial(10 * PaddingScale), 2);
    public double PaddingLg => Math.Round(Spatial(14 * PaddingScale), 2);
    public double PaddingXl => Math.Round(Spatial(20 * PaddingScale), 2);

    // ---- corner radius ----
    public double RadiusXs => Math.Round(Spatial(4 * RoundingScale), 2);
    public double RadiusSm => Math.Round(Spatial(8 * RoundingScale), 2);
    public double RadiusMd => Math.Round(Spatial(12 * RoundingScale), 2);
    public double RadiusLg => Math.Round(Spatial(16 * RoundingScale), 2);
    public double Radius2Lg => Math.Round(Spatial(24 * RoundingScale), 2);
    public double RadiusFull => Math.Round(Spatial(999), 2);

    // ---- typography ----
    public double FontCaption => Math.Round(BaseFontSize * FontScale * 0.72, 2);
    public double FontSmall => Math.Round(BaseFontSize * FontScale * 0.85, 2);
    public double FontBody => Math.Round(BaseFontSize * FontScale, 2);
    public double FontTitle => Math.Round(BaseFontSize * FontScale * 1.25, 2);
    public double FontHeading => Math.Round(BaseFontSize * FontScale * 1.7, 2);
    public double FontDisplay => Math.Round(BaseFontSize * FontScale * 2.4, 2);

    /// <summary>Clock and digits need tabular figures or the bar jitters every minute.</summary>
    public string NumericFontFamily => FontFamily;

    /// <summary>Minimum hit target; below this a shell is unusable on a touchscreen.</summary>
    public double MinHitTarget => Density == Density.Compact ? 28 : 32;

    public DesignTokens Normalized()
    {
        double Clamp(double v, double min, double max) => double.IsNaN(v) ? min : Math.Clamp(v, min, max);

        return this with
        {
            Scale = Clamp(Scale, 0.5, 3),
            SpacingScale = Clamp(SpacingScale, 0, 3),
            PaddingScale = Clamp(PaddingScale, 0, 3),
            RoundingScale = Clamp(RoundingScale, 0, 4),
            FontScale = Clamp(FontScale, 0.5, 3),
            BaseFontSize = Clamp(BaseFontSize, 8, 32)
        };
    }
}

/// <summary>
/// Motion tokens. Durations and curves live here so a single switch can turn every animation off
/// (reduced motion) instead of hunting through views.
/// </summary>
public sealed record MotionTokens(double Scale = 1, bool Reduced = false)
{
    public static MotionTokens Default { get; } = new();

    private TimeSpan D(int ms) => Reduced || Scale <= 0
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(Math.Round(ms * Scale));

    public TimeSpan Fast => D(120);
    public TimeSpan Normal => D(200);
    public TimeSpan Slow => D(320);
    public TimeSpan PageTransition => D(420);

    /// <summary>Everything animatable in the shell uses one of these three curves.</summary>
    public string StandardEasing => "cubic-bezier(0.2, 0, 0, 1)";
    public string EmphasisedEasing => "cubic-bezier(0.05, 0.7, 0.1, 1)";
    public string ExitEasing => "cubic-bezier(0.3, 0, 0.8, 0.15)";

    public MotionTokens Normalized() => this with { Scale = double.IsNaN(Scale) ? 1 : Math.Clamp(Scale, 0, 4) };
}
