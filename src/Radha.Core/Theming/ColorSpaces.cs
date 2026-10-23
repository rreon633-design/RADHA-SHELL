using System.Globalization;

namespace Radha.Core.Theming;

/// <summary>
/// 8-bit sRGB colour value with OKLab/OKLCH conversions.
/// Deliberately platform-neutral: no System.Drawing, no WinUI types,
/// so the palette engine is unit-testable on any OS.
/// </summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 8) s = s[2..]; // drop AARRGGBB alpha
        if (s.Length != 6) throw new FormatException($"Invalid hex colour: {hex}");
        return new Rgb(
            byte.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static Rgb Lerp(Rgb a, Rgb b, double t) => new(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>ARGB string as consumed by XAML colour parsing.</summary>
    public string ToArgbHex() => $"#FF{R:X2}{G:X2}{B:X2}";

    public double LinearR => SrgbToLinear(R / 255.0);
    public double LinearG => SrgbToLinear(G / 255.0);
    public double LinearB => SrgbToLinear(B / 255.0);

    public OkLab ToOkLab() => OkLab.FromRgb(this);
    public OkLch ToOkLch() => OkLab.FromRgb(this).ToLch();

    internal static double SrgbToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    internal static double LinearToSrgb(double c) =>
        c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    /// <summary>WCAG 2.1 relative luminance.</summary>
    public double RelativeLuminance => 0.2126 * LinearR + 0.7152 * LinearG + 0.0722 * LinearB;

    /// <summary>WCAG 2.1 contrast ratio between two colours (1..21).</summary>
    public double Contrast(Rgb other)
    {
        var l1 = RelativeLuminance;
        var l2 = other.RelativeLuminance;
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    public override string ToString() => ToHex();
}

/// <summary>OKLab (Björn Ottosson, 2020) — perceptual space behind the palette engine.</summary>
public readonly record struct OkLab(double L, double A, double Bb)
{
    public static OkLab FromRgb(Rgb c)
    {
        var r = c.LinearR;
        var g = c.LinearG;
        var b = c.LinearB;

        var l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
        var m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
        var s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;

        var l_ = Math.Cbrt(l);
        var m_ = Math.Cbrt(m);
        var s_ = Math.Cbrt(s);

        return new OkLab(
            0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
            1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
            0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_);
    }

    public OkLch ToLch()
    {
        var c = Math.Sqrt(A * A + Bb * Bb);
        var h = Math.Atan2(Bb, A) * 180.0 / Math.PI;
        if (h < 0) h += 360;
        return new OkLch(L, c, h);
    }

    public Rgb ToRgb()
    {
        var l_ = L + 0.3963377774 * A + 0.2158037573 * Bb;
        var m_ = L - 0.1055613458 * A - 0.0638541728 * Bb;
        var s_ = L - 0.0894841775 * A - 1.2914855480 * Bb;

        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;

        var r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        var g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        var b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        return new Rgb(ToByte(r), ToByte(g), ToByte(b));
    }

    private static byte ToByte(double linear)
    {
        var c = Rgb.LinearToSrgb(linear);
        return (byte)Math.Clamp(Math.Round(c * 255.0), 0, 255);
    }
}

public readonly record struct OkLch(double L, double C, double H)
{
    public OkLab ToOkLab()
    {
        var rad = H * Math.PI / 180.0;
        return new OkLab(L, C * Math.Cos(rad), C * Math.Sin(rad));
    }

    public Rgb ToRgb() => ToOkLab().ToRgb();

    public OkLch WithChroma(double c) => this with { C = Math.Max(0, Math.Min(c, 0.37)) };
    public OkLch WithHue(double h) => this with { H = ((h % 360) + 360) % 360 };
    public OkLch WithLightness(double l) => this with { L = Math.Clamp(l, 0, 1) };
}
