using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Mcp;

/// <summary>
/// Weather tools accept coordinates, and the model invents them (a memorized city)
/// instead of the place already in the turn. A forecast is allowed only when its
/// pairs came from <c>openmeteo_search_locations</c> in the same turn.
/// </summary>
internal sealed partial class OpenMeteoCoordinateGate
{
    internal const string SearchTool = "openmeteo_search_locations";

    internal const double ToleranceDegrees = 0.05;

    private const int MaxTurns = 128;

    private static readonly HashSet<string> CoordinateTools = new(StringComparer.Ordinal)
    {
        "openmeteo_get_forecast",
        "openmeteo_get_historical",
        "openmeteo_get_marine",
        "openmeteo_get_air_quality",
        "openmeteo_get_elevation",
        "openmeteo_get_ensemble",
        "openmeteo_get_flood",
        "openmeteo_get_climate",
    };

    private readonly ConcurrentDictionary<Guid, TurnPairs> _turns = new();

    internal static string Describe(string remoteName, string description)
    {
        var note = Note(remoteName);
        if (note is null || description.Contains(note, StringComparison.Ordinal))
        {
            return description;
        }

        return string.IsNullOrWhiteSpace(description)
            ? note
            : description.TrimEnd() + "\n" + note;
    }

    internal string? Reject(string remoteName, ToolArguments args, Guid turnId, string searchToolName)
    {
        if (!CoordinateTools.Contains(remoteName))
        {
            return null;
        }

        var requested = ReadRequested(args.RawJson);
        if (requested.Count == 0)
        {
            return null;
        }

        if (_turns.TryGetValue(turnId, out var known) && known.MatchesAll(requested))
        {
            return null;
        }

        return
            "Esas coordenadas no salieron de " + searchToolName + " en este turno. "
            + "No inventes latitude ni longitude. Si la pregunta depende de un lugar de la persona, "
            + "usá el nombre que ya está en los hechos fijados o recuerdos y buscalo con " + searchToolName
            + " (la ciudad sola, sin país). Después pasá latitude y longitude de ese resultado.";
    }

    internal void Remember(Guid turnId, string searchText)
    {
        var found = Parse(searchText);
        if (found.Count == 0)
        {
            return;
        }

        var turn = _turns.GetOrAdd(turnId, static _ => new TurnPairs());
        turn.Add(found);
        if (_turns.Count > MaxTurns)
        {
            DropOldest(turnId);
        }
    }

    internal static IReadOnlyList<Coordinate> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var json = TryParseJson(text);
        if (json.Count > 0)
        {
            return json;
        }

        var pairs = new List<Coordinate>();
        foreach (Match match in PairPattern().Matches(text))
        {
            if (TryCoordinate(match.Groups[1].Value, match.Groups[2].Value, out var coordinate))
            {
                pairs.Add(coordinate);
            }
        }

        return pairs;
    }

    private static string? Note(string remoteName)
    {
        if (string.Equals(remoteName, SearchTool, StringComparison.Ordinal))
        {
            return "Si el lugar es de la persona, buscá el nombre pelado que ya está en los hechos fijados o recuerdos: la ciudad sola, sin país ni coma.";
        }

        return CoordinateTools.Contains(remoteName)
            ? "No inventes latitude ni longitude. Tienen que ser un par devuelto por openmeteo_search_locations en este mismo turno."
            : null;
    }

    private static List<Coordinate> ReadRequested(string json)
    {
        var pairs = new List<Coordinate>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        }
        catch (JsonException)
        {
            return pairs;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                return pairs;
            }

            if (TryNumber(root, "latitude", out var latitude) && TryNumber(root, "longitude", out var longitude))
            {
                pairs.Add(new Coordinate(latitude, longitude));
            }

            if (root.TryGetProperty("latitudes", out var latitudes)
                && root.TryGetProperty("longitudes", out var longitudes)
                && latitudes.ValueKind is JsonValueKind.Array
                && longitudes.ValueKind is JsonValueKind.Array)
            {
                var count = Math.Min(latitudes.GetArrayLength(), longitudes.GetArrayLength());
                for (var i = 0; i < count; i++)
                {
                    if (TryElementNumber(latitudes[i], out var lat) && TryElementNumber(longitudes[i], out var lon))
                    {
                        pairs.Add(new Coordinate(lat, lon));
                    }
                }
            }
        }

        return pairs;
    }

    private static List<Coordinate> TryParseJson(string text)
    {
        var pairs = new List<Coordinate>();
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return pairs;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text[start..]);
        }
        catch (JsonException)
        {
            return pairs;
        }

        using (document)
        {
            Walk(document.RootElement, pairs);
        }

        return pairs;
    }

    private static void Walk(JsonElement element, List<Coordinate> pairs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryNumber(element, "latitude", out var latitude)
                    && TryNumber(element, "longitude", out var longitude))
                {
                    pairs.Add(new Coordinate(latitude, longitude));
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, pairs);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, pairs);
                }

                break;
        }
    }

    private static bool TryNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var property) && TryElementNumber(property, out value);
    }

    private static bool TryElementNumber(JsonElement element, out double value)
    {
        if (element.ValueKind is JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }

        if (element.ValueKind is JsonValueKind.String
            && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryCoordinate(string latitudeText, string longitudeText, out Coordinate coordinate)
    {
        coordinate = default;
        if (!double.TryParse(latitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(longitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return false;
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return false;
        }

        coordinate = new Coordinate(latitude, longitude);
        return true;
    }

    private void DropOldest(Guid keep)
    {
        var oldest = _turns
            .Where(pair => pair.Key != keep)
            .OrderBy(pair => pair.Value.Ticks)
            .Take(_turns.Count / 2)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var id in oldest)
        {
            _turns.TryRemove(id, out _);
        }
    }

    [GeneratedRegex(@"(-?\d+\.\d+)\s*,\s*(-?\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    internal readonly record struct Coordinate(double Latitude, double Longitude)
    {
        public bool Near(Coordinate other)
            => Math.Abs(Latitude - other.Latitude) <= ToleranceDegrees
               && Math.Abs(Longitude - other.Longitude) <= ToleranceDegrees;
    }

    private sealed class TurnPairs
    {
        private readonly Lock _gate = new();
        private readonly List<Coordinate> _pairs = [];

        public long Ticks { get; private set; } = Environment.TickCount64;

        public void Add(IReadOnlyList<Coordinate> found)
        {
            lock (_gate)
            {
                _pairs.AddRange(found);
                Ticks = Environment.TickCount64;
            }
        }

        public bool MatchesAll(IReadOnlyList<Coordinate> requested)
        {
            lock (_gate)
            {
                return requested.All(pair => _pairs.Exists(known => known.Near(pair)));
            }
        }
    }
}
