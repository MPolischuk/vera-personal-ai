using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Assistant.Core.Memory;

namespace Assistant.Extensions.Memory.Conditions;

internal readonly record struct Residence(string SearchName, string Display, string? CountryCode);

internal readonly record struct CurrentConditions(double TemperatureC, double PrecipitationMm, int WeatherCode);

internal interface ICurrentConditionsSource
{
    public ValueTask<CurrentConditions?> GetAsync(Residence residence, CancellationToken ct);
}

internal static partial class LocalConditionsIntent
{
    private static readonly HashSet<string> PlaceStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "casa", "este", "esta", "ese", "esa", "eso", "mi", "mis", "el", "la", "los", "las",
        "un", "una", "serio", "general", "realidad", "teoria", "teoría", "absoluto",
        "principio", "todo", "cualquier", "vivo",
    };

    public static bool NeedsHomeConditions(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || !Cue().IsMatch(message))
        {
            return false;
        }

        return !NamesAnotherPlace(message);
    }

    private static bool NamesAnotherPlace(string message)
    {
        var match = Place().Match(message);
        if (!match.Success)
        {
            return false;
        }

        var first = match.Groups[1].Value;
        return !PlaceStopwords.Contains(first);
    }

    [GeneratedRegex(
        @"paraguas|temperatura|clima|\blluv|\bprecipita|pron[oó]stico|\bfr[ií]o\b|\bcalor\b|\babrig|chaparr|tormenta|\bhumedad\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Cue();

    [GeneratedRegex(
        @"\ben\s+([\p{L}]+)(?:\s+[\p{L}]+){0,2}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Place();
}

internal static partial class ResidencePhrase
{
    private static readonly Dictionary<string, string> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["argentina"] = "AR",
        ["brasil"] = "BR",
        ["brazil"] = "BR",
        ["uruguay"] = "UY",
        ["chile"] = "CL",
        ["paraguay"] = "PY",
        ["españa"] = "ES",
        ["espana"] = "ES",
        ["spain"] = "ES",
        ["méxico"] = "MX",
        ["mexico"] = "MX",
        ["francia"] = "FR",
        ["france"] = "FR",
        ["estados unidos"] = "US",
    };

    public static Residence? FromMemories(IEnumerable<MemoryItem> items)
    {
        var chosen = items
            .Select(item => (Item: item, Residence: Parse(item.Content)))
            .Where(pair => pair.Residence is not null)
            .OrderByDescending(pair => pair.Item.Pinned)
            .ThenByDescending(pair => pair.Item.UpdatedAt)
            .Select(pair => pair.Residence)
            .FirstOrDefault();

        return chosen;
    }

    public static Residence? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var match = LivesIn().Match(content);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
        var comma = raw.IndexOf(',');
        var locality = (comma < 0 ? raw : raw[..comma]).Trim();
        var country = comma < 0 ? null : raw[(comma + 1)..].Trim().TrimEnd('.');
        if (locality.Length == 0)
        {
            return null;
        }

        var search = Capital().Replace(locality, " ").Trim();
        search = Spaces().Replace(search, " ");
        if (search.Length == 0)
        {
            search = locality;
        }

        string? countryCode = null;
        if (!string.IsNullOrWhiteSpace(country))
        {
            Countries.TryGetValue(country, out countryCode);
        }

        return new Residence(search, locality, countryCode);
    }

    [GeneratedRegex(@"\bvive en\s+(.+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LivesIn();

    [GeneratedRegex(@"\bCapital\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Capital();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}

internal static class SkyLabel
{
    public static string Describe(int weatherCode) => weatherCode switch
    {
        0 => "despejado",
        1 => "mayormente despejado",
        2 => "parcialmente nublado",
        3 => "nublado",
        45 or 48 => "niebla",
        >= 51 and <= 57 => "llovizna",
        >= 61 and <= 67 => "lluvia",
        >= 71 and <= 77 => "nieve",
        >= 80 and <= 82 => "chaparrones",
        85 or 86 => "nieve",
        >= 95 and <= 99 => "tormenta",
        _ => "variable",
    };
}

internal sealed class OpenMeteoCurrentConditions(HttpClient http) : ICurrentConditionsSource
{
    public async ValueTask<CurrentConditions?> GetAsync(Residence residence, CancellationToken ct)
    {
        var name = Uri.EscapeDataString(residence.SearchName);
        var geoUrl = "https://geocoding-api.open-meteo.com/v1/search?name="
            + name + "&count=5&language=es";
        if (!string.IsNullOrEmpty(residence.CountryCode))
        {
            geoUrl += "&countryCode=" + residence.CountryCode;
        }

        var geo = await http.GetFromJsonAsync<GeoResponse>(geoUrl, ct);
        var hit = Pick(geo);
        if (hit is null)
        {
            return null;
        }

        var forecastUrl =
            "https://api.open-meteo.com/v1/forecast?latitude="
            + hit.Latitude.ToString(CultureInfo.InvariantCulture)
            + "&longitude="
            + hit.Longitude.ToString(CultureInfo.InvariantCulture)
            + "&current=temperature_2m,precipitation,weather_code&timezone=auto";

        var forecast = await http.GetFromJsonAsync<ForecastResponse>(forecastUrl, ct);
        var current = forecast?.Current;
        if (current?.TemperatureC is null || current.PrecipitationMm is null || current.WeatherCode is null)
        {
            return null;
        }

        return new CurrentConditions(
            current.TemperatureC.Value,
            current.PrecipitationMm.Value,
            current.WeatherCode.Value);
    }

    private static GeoHit? Pick(GeoResponse? response)
    {
        var results = response?.Results;
        if (results is null || results.Count == 0)
        {
            return null;
        }

        return results.OrderByDescending(hit => hit.Population ?? 0).First();
    }

    private sealed class GeoResponse
    {
        [JsonPropertyName("results")]
        public List<GeoHit>? Results { get; set; }
    }

    private sealed class GeoHit
    {
        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("population")]
        public int? Population { get; set; }
    }

    private sealed class ForecastResponse
    {
        [JsonPropertyName("current")]
        public CurrentPayload? Current { get; set; }
    }

    private sealed class CurrentPayload
    {
        [JsonPropertyName("temperature_2m")]
        public double? TemperatureC { get; set; }

        [JsonPropertyName("precipitation")]
        public double? PrecipitationMm { get; set; }

        [JsonPropertyName("weather_code")]
        public int? WeatherCode { get; set; }
    }
}
