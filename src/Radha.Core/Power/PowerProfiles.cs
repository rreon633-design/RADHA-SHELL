namespace Radha.Core.Power;

/// <summary>The three power overlays Windows exposes to an application.</summary>
public enum PowerProfile
{
    /// <summary>Balanced — the OS default; also the "recommended" overlay on Surface hardware.</summary>
    Balanced,

    /// <summary>Best power efficiency.</summary>
    Saver,

    /// <summary>Best performance. On battery this drains noticeably faster and is worth one click.</summary>
    Performance
}

/// <summary>
/// Power overlay schemes.
///
/// Windows has two layers of power configuration: the *scheme* (a plan you can edit, which we do not
/// touch) and the *overlay*, the three-way switch shown in Quick Settings. The overlay is the thing
/// users actually mean by "power mode", and it is addressable without administrator rights through
/// GUIDs fixed by the OS, so we never have to parse localized <c>powercfg</c> output to find a plan.
/// </summary>
public static class PowerProfiles
{
    // {00000000-0000-0000-0000-000000000000} is deliberately the balanced overlay GUID.
    public const string BalancedGuid = "00000000-0000-0000-0000-000000000000";
    public const string SaverGuid = "961cc777-2547-4f9d-8174-7d86181b8a7a";
    public const string PerformanceGuid = "ded574b5-45a0-4f42-8737-46345c09c238";

    public static string ToGuid(PowerProfile profile) => profile switch
    {
        PowerProfile.Saver => SaverGuid,
        PowerProfile.Performance => PerformanceGuid,
        _ => BalancedGuid
    };

    public static string ToConfigString(PowerProfile profile) => profile switch
    {
        PowerProfile.Saver => "saver",
        PowerProfile.Performance => "performance",
        _ => "balanced"
    };

    public static bool TryParse(string? text, out PowerProfile profile)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "balanced":
            case "normal":
            case "default":
                profile = PowerProfile.Balanced;
                return true;
            case "saver":
            case "power-saver":
            case "powersaver":
            case "efficiency":
                profile = PowerProfile.Saver;
                return true;
            case "performance":
            case "turbo":
            case "best-performance":
                profile = PowerProfile.Performance;
                return true;
            default:
                profile = PowerProfile.Balanced;
                return false;
        }
    }

    /// <summary>The argument list for <c>powercfg</c>; kept here so the App layer only spawns.</summary>
    public static IReadOnlyList<string> PowercfgArguments(string guid) => new[] { "/overlaysetactive", guid };

    /// <summary>
    /// A machine without a battery has no meaningful power-mode choice — the overlay is a no-op
    /// there, and offering the switch would be a lie.
    /// </summary>
    public static bool IsRelevant(bool hasBattery) => hasBattery;
}
