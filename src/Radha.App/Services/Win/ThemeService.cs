using Radha.Core.Services;
using Radha.Core.Theming;

namespace Radha.App.Services.Win;

/// <summary>Holds the active Material 3 scheme and notifies every surface when it changes.</summary>
public sealed class ThemeService : IThemeService
{
    private bool _dark = true;
    private SchemeVariant _variant = SchemeVariant.TonalSpot;
    private Rgb _seed = new(124, 92, 255);

    public MaterialScheme Current { get; private set; } = MaterialScheme.FromSeed(new Rgb(124, 92, 255), true);

    public event EventHandler<MaterialScheme>? Changed;

    public void Initialize(Config.ShellConfig config)
    {
        _variant = Enum.TryParse<SchemeVariant>(config.Appearance.SchemeVariant, true, out var variant)
            ? variant
            : SchemeVariant.TonalSpot;
        _dark = config.Appearance.Mode switch
        {
            "light" => false,
            "dark" => true,
            _ => DateTime.Now.Hour is < 7 or >= 19
        };
        Recompute(raise: false);
    }

    public void Apply(RgbSeed seed, bool dark, SchemeVariant variant)
    {
        _seed = new Rgb(seed.R, seed.G, seed.B);
        _dark = dark;
        _variant = variant;
        Recompute();
    }

    public void SetMode(bool dark)
    {
        _dark = dark;
        Recompute();
    }

    public void SetVariant(SchemeVariant variant)
    {
        _variant = variant;
        Recompute();
    }

    private void Recompute(bool raise = true)
    {
        Current = MaterialScheme.FromSeed(_seed, _dark, _variant);
        if (raise) Changed?.Invoke(this, Current);
    }
}
