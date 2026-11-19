using System.Globalization;
using Assistant.Abstractions.Proactive;
using Assistant.Core.Memory;

namespace Assistant.Extensions.Memory.Conditions;

/// <summary>
/// Current conditions for the briefing prompt. A missing residence or a failed lookup adds nothing.
/// This is not the turn contributor: it does not tell the model to skip tools.
/// </summary>
internal sealed class BriefingWeatherSource(
    IMemoryStore memories,
    ICurrentConditionsSource conditions) : IAmbientSource
{
    public string Name => "weather";

    public async ValueTask<string?> ReadAsync(Guid userId, CancellationToken ct)
    {
        var rows = await memories.ListAsync(userId, skip: 0, take: 40, ct);
        var residence = ResidencePhrase.FromMemories(rows);
        if (residence is null)
        {
            return null;
        }

        var current = await conditions.GetAsync(residence.Value, ct);
        if (current is null)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"En {residence.Value.Display}: {current.Value.TemperatureC:0.0} °C, {SkyLabel.Describe(current.Value.WeatherCode)}, precipitación {current.Value.PrecipitationMm:0.0} mm.");
    }
}
