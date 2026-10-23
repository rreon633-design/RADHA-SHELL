namespace Radha.Core.Theming;

/// <summary>Material 3 scheme variants supported by the theme engine.</summary>
public enum SchemeVariant
{
    TonalSpot,   // M3 default — balanced, calm
    Vibrant,     // punchier chroma, used by "vivid" wallpapers
    Expressive,
    Fidelity,    // stays close to the seed colour
    Content,
    Neutral,
    Monochrome
}

/// <summary>
/// A Material-3 tonal palette: one hue, chroma modulated by tone so extremes
/// stay in sRGB gamut while mid-tones keep their punch.
/// </summary>
public readonly struct TonalPalette
{
    private readonly double _hue;
    private readonly double _chroma;

    public TonalPalette(double hue, double chroma)
    {
        _hue = hue;
        _chroma = Math.Clamp(chroma, 0, 0.37);
    }

    public static TonalPalette FromSeed(OkLch seed, double chromaMultiplier = 1.0) =>
        new(seed.H, seed.C * chromaMultiplier);

    /// <summary>Tone is a CIE L* value 0..100, matching Material's tonal numbering.</summary>
    public Rgb Tone(double tone)
    {
        tone = Math.Clamp(tone, 0, 100);
        var l = ToneToOklabL(tone);
        var chroma = _chroma * ChromaScale(tone);

        // Gamut-map by pulling chroma in until the sRGB round-trip is stable.
        var candidate = new OkLch(l, chroma, _hue);
        for (var i = 0; i < 28 && chroma > 1e-4; i++)
        {
            var rgb = candidate.ToRgb();
            var back = rgb.ToOkLch();
            if (Math.Abs(back.L - candidate.L) < 0.02 && Math.Abs(back.C - candidate.C) < 0.02)
                return rgb;
            chroma *= 0.88;
            candidate = candidate.WithChroma(chroma);
        }

        return new OkLch(l, Math.Max(0, chroma), _hue).ToRgb();
    }

    public Rgb this[double tone] => Tone(tone);

    private static double ToneToOklabL(double tone)
    {
        // L* (CIE) -> relative luminance Y -> OKLab L (cube root of luminance).
        var y = tone <= 8.0 ? tone / 903.3 : Math.Pow((tone + 16.0) / 116.0, 3.0);
        return Math.Cbrt(Math.Max(0, y));
    }

    private static double ChromaScale(double tone)
    {
        // Peak chroma through the mid-tones, tapering to 0 at pure black/white.
        var x = Math.Clamp(tone, 0, 100) / 100.0;
        return Math.Pow(Math.Sin(Math.PI * x), 0.55);
    }
}

/// <summary>
/// Full Material-3 colour role set, computed from a single seed colour.
/// Mirrors material-color-utilities' scheme_tonal_spot role → tone mapping,
/// so any WinUI brush can be resolved by role name at runtime.
/// </summary>
public sealed record MaterialScheme
{
    public bool IsDark { get; init; }
    public SchemeVariant Variant { get; init; }
    public Rgb Seed { get; init; }

    public Rgb Primary { get; init; }
    public Rgb OnPrimary { get; init; }
    public Rgb PrimaryContainer { get; init; }
    public Rgb OnPrimaryContainer { get; init; }

    public Rgb Secondary { get; init; }
    public Rgb OnSecondary { get; init; }
    public Rgb SecondaryContainer { get; init; }
    public Rgb OnSecondaryContainer { get; init; }

    public Rgb Tertiary { get; init; }
    public Rgb OnTertiary { get; init; }
    public Rgb TertiaryContainer { get; init; }
    public Rgb OnTertiaryContainer { get; init; }

    public Rgb Error { get; init; }
    public Rgb OnError { get; init; }
    public Rgb ErrorContainer { get; init; }
    public Rgb OnErrorContainer { get; init; }

    public Rgb Surface { get; init; }
    public Rgb OnSurface { get; init; }
    public Rgb SurfaceVariant { get; init; }
    public Rgb OnSurfaceVariant { get; init; }
    public Rgb SurfaceDim { get; init; }
    public Rgb SurfaceBright { get; init; }
    public Rgb SurfaceContainerLowest { get; init; }
    public Rgb SurfaceContainerLow { get; init; }
    public Rgb SurfaceContainer { get; init; }
    public Rgb SurfaceContainerHigh { get; init; }
    public Rgb SurfaceContainerHighest { get; init; }

    public Rgb Outline { get; init; }
    public Rgb OutlineVariant { get; init; }
    public Rgb InverseSurface { get; init; }
    public Rgb InverseOnSurface { get; init; }
    public Rgb InversePrimary { get; init; }
    public Rgb Shadow { get; init; }
    public Rgb Scrim { get; init; }
    public Rgb SurfaceTint { get; init; }

    public static MaterialScheme FromSeed(Rgb seed, bool dark = true, SchemeVariant variant = SchemeVariant.TonalSpot)
        => FromSeed(seed.ToOkLch(), seed, dark, variant);

    public static MaterialScheme FromSeed(OkLch seed, bool dark = true, SchemeVariant variant = SchemeVariant.TonalSpot)
        => FromSeed(seed, seed.ToRgb(), dark, variant);

    private static MaterialScheme FromSeed(OkLch seed, Rgb seedRgb, bool dark, SchemeVariant variant)
    {
        var primaryHue = seed.H;
        var primaryChroma = seed.C;
        double tertiaryHueShift = variant switch
        {
            SchemeVariant.Expressive => 105,
            SchemeVariant.Vibrant => 72,
            SchemeVariant.Fidelity => 18,
            SchemeVariant.Content => 32,
            _ => 60
        };
        var chromaMultiplier = variant switch
        {
            SchemeVariant.Vibrant => 1.45,
            SchemeVariant.Expressive => 1.25,
            SchemeVariant.Fidelity => 1.0,
            SchemeVariant.Content => 1.1,
            SchemeVariant.Neutral => 0.35,
            SchemeVariant.Monochrome => 0.0,
            _ => 1.0
        };

        var p = new TonalPalette(primaryHue, Math.Clamp(primaryChroma * chromaMultiplier, 0.012, 0.26));
        var s = new TonalPalette(primaryHue, Math.Clamp(primaryChroma * chromaMultiplier * 0.38, 0.008, 0.10));
        var t = new TonalPalette(primaryHue + tertiaryHueShift, Math.Clamp(primaryChroma * chromaMultiplier * 0.72, 0.010, 0.18));
        var n = new TonalPalette(primaryHue, variant == SchemeVariant.Neutral ? 0.012 : 0.022);
        var nv = new TonalPalette(primaryHue, variant == SchemeVariant.Monochrome ? 0.0 : 0.045);
        var e = new TonalPalette(25, 0.20);

        return dark ? Dark(p, s, t, n, nv, e, seedRgb, variant) : Light(p, s, t, n, nv, e, seedRgb, variant);
    }

    private static MaterialScheme Light(TonalPalette p, TonalPalette s, TonalPalette t,
        TonalPalette n, TonalPalette nv, TonalPalette e, Rgb seed, SchemeVariant variant) => new()
    {
        IsDark = false, Variant = variant, Seed = seed,
        Primary = p[40], OnPrimary = p[100], PrimaryContainer = p[90], OnPrimaryContainer = p[10],
        Secondary = s[40], OnSecondary = s[100], SecondaryContainer = s[90], OnSecondaryContainer = s[10],
        Tertiary = t[40], OnTertiary = t[100], TertiaryContainer = t[90], OnTertiaryContainer = t[10],
        Error = e[40], OnError = e[100], ErrorContainer = e[90], OnErrorContainer = e[10],
        Surface = n[98], OnSurface = n[10], SurfaceVariant = nv[90], OnSurfaceVariant = nv[30],
        SurfaceDim = n[87], SurfaceBright = n[98],
        SurfaceContainerLowest = n[100], SurfaceContainerLow = n[96],
        SurfaceContainer = n[94], SurfaceContainerHigh = n[92], SurfaceContainerHighest = n[90],
        Outline = nv[50], OutlineVariant = nv[80],
        InverseSurface = n[20], InverseOnSurface = n[95], InversePrimary = p[80],
        Shadow = new Rgb(0, 0, 0), Scrim = new Rgb(0, 0, 0), SurfaceTint = p[40]
    };

    private static MaterialScheme Dark(TonalPalette p, TonalPalette s, TonalPalette t,
        TonalPalette n, TonalPalette nv, TonalPalette e, Rgb seed, SchemeVariant variant) => new()
    {
        IsDark = true, Variant = variant, Seed = seed,
        Primary = p[80], OnPrimary = p[20], PrimaryContainer = p[30], OnPrimaryContainer = p[90],
        Secondary = s[80], OnSecondary = s[20], SecondaryContainer = s[30], OnSecondaryContainer = s[90],
        Tertiary = t[80], OnTertiary = t[20], TertiaryContainer = t[30], OnTertiaryContainer = t[90],
        Error = e[80], OnError = e[20], ErrorContainer = e[30], OnErrorContainer = e[90],
        Surface = n[6], OnSurface = n[90], SurfaceVariant = nv[30], OnSurfaceVariant = nv[80],
        SurfaceDim = n[6], SurfaceBright = n[24],
        SurfaceContainerLowest = n[4], SurfaceContainerLow = n[10],
        SurfaceContainer = n[12], SurfaceContainerHigh = n[17], SurfaceContainerHighest = n[22],
        Outline = nv[60], OutlineVariant = nv[30],
        InverseSurface = n[90], InverseOnSurface = n[20], InversePrimary = p[40],
        Shadow = new Rgb(0, 0, 0), Scrim = new Rgb(0, 0, 0), SurfaceTint = p[80]
    };

    /// <summary>Role → colour map used by the XAML token writer and the CLI.</summary>
    public IReadOnlyDictionary<string, Rgb> ToRoleMap() => new SortedDictionary<string, Rgb>(StringComparer.Ordinal)
    {
        ["primary"] = Primary,
        ["onPrimary"] = OnPrimary,
        ["primaryContainer"] = PrimaryContainer,
        ["onPrimaryContainer"] = OnPrimaryContainer,
        ["secondary"] = Secondary,
        ["onSecondary"] = OnSecondary,
        ["secondaryContainer"] = SecondaryContainer,
        ["onSecondaryContainer"] = OnSecondaryContainer,
        ["tertiary"] = Tertiary,
        ["onTertiary"] = OnTertiary,
        ["tertiaryContainer"] = TertiaryContainer,
        ["onTertiaryContainer"] = OnTertiaryContainer,
        ["error"] = Error,
        ["onError"] = OnError,
        ["errorContainer"] = ErrorContainer,
        ["onErrorContainer"] = OnErrorContainer,
        ["surface"] = Surface,
        ["onSurface"] = OnSurface,
        ["surfaceVariant"] = SurfaceVariant,
        ["onSurfaceVariant"] = OnSurfaceVariant,
        ["surfaceDim"] = SurfaceDim,
        ["surfaceBright"] = SurfaceBright,
        ["surfaceContainerLowest"] = SurfaceContainerLowest,
        ["surfaceContainerLow"] = SurfaceContainerLow,
        ["surfaceContainer"] = SurfaceContainer,
        ["surfaceContainerHigh"] = SurfaceContainerHigh,
        ["surfaceContainerHighest"] = SurfaceContainerHighest,
        ["outline"] = Outline,
        ["outlineVariant"] = OutlineVariant,
        ["inverseSurface"] = InverseSurface,
        ["inverseOnSurface"] = InverseOnSurface,
        ["inversePrimary"] = InversePrimary,
        ["shadow"] = Shadow,
        ["scrim"] = Scrim,
        ["surfaceTint"] = SurfaceTint
    };

    /// <summary>Minimal sanity check used by tests and by the theme service before applying a scheme.</summary>
    public bool HasReadableContrast(double minimum = 4.5) =>
        Primary.Contrast(OnPrimary) >= minimum &&
        Surface.Contrast(OnSurface) >= minimum &&
        PrimaryContainer.Contrast(OnPrimaryContainer) >= minimum &&
        Error.Contrast(OnError) >= minimum;
}
