using System.Net.Http;
using System.Text.Json;
using Radha.Core.Config;
using Radha.Core.Services;
using Radha.Core.Weather;

namespace Radha.App.Services.Win;

/// <summary>
/// Open-Meteo weather (no API key). Coordinates come from shell.json; if absent the
/// service derives an approximate city once from ipapi.co (disable with provider = "manual").
/// </summary>
public sealed class WeatherService : IWeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly ShellConfig _config;
    private double? _lat;
    private double? _lon;
    private string _city = "";

    public WeatherNow? Current { get; private set; }
    public event EventHandler<WeatherNow>? Updated;

    public WeatherService(ShellConfig config)
    {
        _config = config;
        _lat = config.Services.Weather.Latitude;
        _lon = config.Services.Weather.Longitude;
        _city = config.Services.Weather.Location;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            if (_lat is null || _lon is null)
                await ResolveLocationAsync(ct).ConfigureAwait(false);
            if (_lat is null || _lon is null) return;

            var units = _config.Services.Weather.Units == "imperial" ? "fahrenheit" : "celsius";
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={_lat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                      $"&longitude={_lon.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                      $"&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m" +
                      $"&hourly=temperature_2m,weather_code,precipitation_probability" +
                      $"&daily=temperature_2m_max,temperature_2m_min&forecast_days=2" +
                      $"&temperature_unit={units}&timezone=auto";

            await using var stream = await Http.GetStreamAsync(url, ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = document.RootElement;

            var current = root.GetProperty("current");
            var temp = current.GetProperty("temperature_2m").GetDouble();
            var code = current.GetProperty("weather_code").GetInt32();
            var daily = root.GetProperty("daily");
            var high = daily.GetProperty("temperature_2m_max")[0].GetDouble();
            var low = daily.GetProperty("temperature_2m_min")[0].GetDouble();

            var hours = new List<WeatherHour>();
            var hourly = root.GetProperty("hourly");
            var times = hourly.GetProperty("time");
            var temps = hourly.GetProperty("temperature_2m");
            var codes = hourly.GetProperty("weather_code");
            var precip = hourly.GetProperty("precipitation_probability");

            var nowIso = DateTime.Now.ToString("yyyy-MM-ddTHH") + ":00";
            var start = 0;
            for (var i = 0; i < times.GetArrayLength(); i++)
            {
                if (string.CompareOrdinal(times[i].GetString(), nowIso) < 0) continue;
                start = i;
                break;
            }

            for (var i = start; i < Math.Min(start + 5, times.GetArrayLength()); i++)
            {
                var label = DateTime.TryParse(times[i].GetString(), out var time) ? time.ToString("HH:mm") : "--";
                hours.Add(new WeatherHour(label, WeatherCodes.For(codes[i].GetInt32()).Key,
                    temps[i].GetDouble(), precip[i].GetInt32()));
            }

            var mapped = WeatherCodes.For(code);
            Current = new WeatherNow(
                string.IsNullOrWhiteSpace(_city) ? "Nearby" : _city,
                temp,
                mapped.Label,
                mapped.Key,
                current.GetProperty("apparent_temperature").GetDouble(),
                current.GetProperty("relative_humidity_2m").GetInt32(),
                (int)Math.Round(current.GetProperty("wind_speed_10m").GetDouble()),
                high,
                low)
            {
                Hours = hours
            };

            Ui.Post(() => Updated?.Invoke(this, Current));
        }
        catch
        {
            // Offline / rate-limited: keep the last reading, fail silently.
        }
    }

    private async Task ResolveLocationAsync(CancellationToken ct)
    {
        if (_config.Services.Weather.Provider != "open-meteo") return;
        try
        {
            await using var stream = await Http.GetStreamAsync("https://ipapi.co/json/", ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number)
                _lat = lat.GetDouble();
            if (root.TryGetProperty("longitude", out var lon) && lon.ValueKind == JsonValueKind.Number)
                _lon = lon.GetDouble();
            if (root.TryGetProperty("city", out var city) && city.ValueKind == JsonValueKind.String)
                _city = city.GetString() ?? "";
        }
        catch { }
    }


}
