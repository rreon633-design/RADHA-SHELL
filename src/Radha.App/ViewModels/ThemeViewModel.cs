using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Radha.Core.Theming;
using Windows.UI;

namespace Radha.App.ViewModels;

/// <summary>
/// Immutable snapshot of the current Material 3 scheme as XAML brushes.
/// The shell replaces the whole instance when the wallpaper changes, so a single
/// PropertyChanged on <c>Theme</c> re-colours every bound view at once — which is
/// also what lets us cross-fade the change in <see cref="ShellHost"/>.
/// </summary>
public sealed partial class ThemeViewModel : ObservableObject
{
    public ThemeViewModel(MaterialScheme scheme)
    {
        Scheme = scheme;
        var r = scheme.ToRoleMap();

        Primary = Brush(r["primary"]);
        OnPrimary = Brush(r["onPrimary"]);
        PrimaryContainer = Brush(r["primaryContainer"]);
        OnPrimaryContainer = Brush(r["onPrimaryContainer"]);
        SecondaryContainer = Brush(r["secondaryContainer"]);
        OnSecondaryContainer = Brush(r["onSecondaryContainer"]);
        Tertiary = Brush(r["tertiary"]);
        Error = Brush(r["error"]);
        OnError = Brush(r["onError"]);

        Surface = Brush(r["surface"]);
        SurfaceDim = Brush(r["surfaceDim"]);
        SurfaceContainerLow = Brush(r["surfaceContainerLow"]);
        SurfaceContainer = Brush(r["surfaceContainer"]);
        SurfaceContainerHigh = Brush(r["surfaceContainerHigh"]);
        SurfaceContainerHighest = Brush(r["surfaceContainerHighest"]);
        OnSurface = Brush(r["onSurface"]);
        OnSurfaceVariant = Brush(r["onSurfaceVariant"]);
        Outline = Brush(r["outline"]);
        OutlineVariant = Brush(r["outlineVariant"]);
        InverseSurface = Brush(r["inverseSurface"]);
        OnInverseSurface = Brush(r["inverseOnSurface"]);
        Scrim = Brush(r["scrim"]);

        // Translucent surfaces: the "glass" the panels sit on.
        SurfaceGlass = Brush(scheme.Surface, 0xD8);
        SurfaceGlassStrong = Brush(scheme.SurfaceContainer, 0xEE);
        BarGlass = Brush(scheme.Surface, 0x9A);
        TileOff = Brush(scheme.SurfaceContainerHighest, 0xE8);
        SuccessBrush = Brush(new Rgb(67, 209, 122));
        WarningBrush = Brush(new Rgb(255, 176, 74));
    }

    public MaterialScheme Scheme { get; }
    public bool IsDark => Scheme.IsDark;

    // Raw colours for Win2D / Composition, which take Windows.UI.Color rather than brushes.
    public Color PrimaryColor => ToColor(Scheme.Primary);
    public Color TertiaryColor => ToColor(Scheme.Tertiary);
    public Color OnSurfaceColor => ToColor(Scheme.OnSurface);
    public Color SurfaceColor => ToColor(Scheme.Surface);

    public SolidColorBrush Primary { get; }
    public SolidColorBrush OnPrimary { get; }
    public SolidColorBrush PrimaryContainer { get; }
    public SolidColorBrush OnPrimaryContainer { get; }
    public SolidColorBrush SecondaryContainer { get; }
    public SolidColorBrush OnSecondaryContainer { get; }
    public SolidColorBrush Tertiary { get; }
    public SolidColorBrush Error { get; }
    public SolidColorBrush OnError { get; }

    public SolidColorBrush Surface { get; }
    public SolidColorBrush SurfaceDim { get; }
    public SolidColorBrush SurfaceContainerLow { get; }
    public SolidColorBrush SurfaceContainer { get; }
    public SolidColorBrush SurfaceContainerHigh { get; }
    public SolidColorBrush SurfaceContainerHighest { get; }
    public SolidColorBrush OnSurface { get; }
    public SolidColorBrush OnSurfaceVariant { get; }
    public SolidColorBrush Outline { get; }
    public SolidColorBrush OutlineVariant { get; }
    public SolidColorBrush InverseSurface { get; }
    public SolidColorBrush OnInverseSurface { get; }
    public SolidColorBrush Scrim { get; }

    public SolidColorBrush SurfaceGlass { get; }
    public SolidColorBrush SurfaceGlassStrong { get; }
    public SolidColorBrush BarGlass { get; }
    public SolidColorBrush TileOff { get; }
    public SolidColorBrush SuccessBrush { get; }
    public SolidColorBrush WarningBrush { get; }

    /// <summary>Gradient used for the logo, active tiles and the media artwork placeholder.</summary>
    public LinearGradientBrush AccentGradient
    {
        get
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1)
            };
            gradient.GradientStops.Add(new GradientStop { Color = ToColor(Scheme.Primary), Offset = 0 });
            gradient.GradientStops.Add(new GradientStop { Color = ToColor(Scheme.Tertiary), Offset = 1 });
            return gradient;
        }
    }

    public static Color ToColor(Rgb rgb) =>
        Color.FromArgb(255, rgb.R, rgb.G, rgb.B);

    private static SolidColorBrush Brush(Rgb rgb, byte alpha = 0xFF)
    {
        var color = ToColor(rgb);
        if (alpha != 0xFF) color.A = alpha;
        return new SolidColorBrush(color);
    }
}
