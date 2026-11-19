using System.Globalization;
using System.Text;
using Assistant.Abstractions.Context;
using Assistant.Abstractions.Tools;
using Assistant.Core.Memory;
using Assistant.Extensions.Memory.Conditions;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory.Contributors;

/// <summary>
/// Questions about the person's own weather (umbrella, temperature, rain) do not
/// name a city. Resolve where they live and put the current conditions in the
/// turn so the model does not spend extra rounds guessing a place.
/// </summary>
internal static partial class LocalConditionsLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Local conditions lookup failed for turn {TurnId}.")]
    public static partial void LookupFailed(ILogger logger, Exception exception, Guid turnId);
}

internal sealed class LocalConditionsContributor(
    IMemoryStore store,
    ICurrentConditionsSource conditions,
    ToolDeclarationFilter tools,
    ILogger<LocalConditionsContributor> logger) : IContextContributor
{
    public const string FragmentName = "conditions.local";

    public string Name => FragmentName;

    public int Priority => 45;

    public bool IsEnabled => true;

    public async ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        if (!LocalConditionsIntent.NeedsHomeConditions(request.UserMessage))
        {
            return null;
        }

        try
        {
            var memories = await store.ListAsync(request.UserId, skip: 0, take: 40, ct);
            var residence = ResidencePhrase.FromMemories(memories);
            if (residence is null)
            {
                return null;
            }

            var current = await conditions.GetAsync(residence.Value, ct);
            if (current is null)
            {
                return null;
            }

            tools.ExcludePrefix("weather_");
            tools.ExcludeName("get_current_datetime");
            return ContextFragment.Text(
                Name,
                ContextPlacement.AppendedMessage,
                Describe(residence.Value, current.Value));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LocalConditionsLog.LookupFailed(logger, ex, request.TurnId);
            return null;
        }
    }

    internal static string Describe(Residence residence, CurrentConditions current)
    {
        var sb = new StringBuilder();
        sb.Append("Condiciones actuales en ");
        sb.Append(residence.Display);
        sb.Append(" (donde vive la persona):\n");
        sb.Append("- Temperatura: ");
        sb.Append(current.TemperatureC.ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append(" °C\n");
        sb.Append("- Precipitación: ");
        sb.Append(current.PrecipitationMm.ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append(" mm\n");
        sb.Append("- Cielo: ");
        sb.Append(SkyLabel.Describe(current.WeatherCode));
        sb.Append('\n');
        sb.Append("Respondé con esto. No llames tools de clima, recall ni la hora. ");
        sb.Append("Hablá de la persona en segunda persona.");
        return sb.ToString();
    }
}
