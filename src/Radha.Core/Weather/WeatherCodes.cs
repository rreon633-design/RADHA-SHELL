namespace Radha.Core.Weather;

/// <summary>WMO 4677 weather-code mapping shared by the weather service and the dashboard.</summary>
public static class WeatherCodes
{
    public readonly record struct Description(string Key, string Label);

    public static Description For(int code) => code switch
    {
        0 => new("clear", "Clear sky"),
        1 => new("partly", "Mostly clear"),
        2 => new("partly", "Partly cloudy"),
        3 => new("cloudy", "Overcast"),
        45 or 48 => new("fog", "Fog"),
        51 or 53 or 55 => new("rain", "Drizzle"),
        56 or 57 => new("rain", "Freezing drizzle"),
        61 or 63 or 65 => new("rain", "Rain"),
        66 or 67 => new("rain", "Freezing rain"),
        71 or 73 or 75 => new("snow", "Snow"),
        77 => new("snow", "Snow grains"),
        80 or 81 or 82 => new("rain", "Rain showers"),
        85 or 86 => new("snow", "Snow showers"),
        95 => new("storm", "Thunderstorm"),
        96 or 99 => new("storm", "Thunderstorm with hail"),
        _ => new("partly", "Cloudy")
    };

    public static bool IsPrecipitation(int code) => code is >= 51 and <= 99;

    public static bool IsSevere(int code) => code is >= 95;
}
