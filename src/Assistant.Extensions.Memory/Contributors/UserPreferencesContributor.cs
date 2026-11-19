using System.Diagnostics;
using System.Text;
using Assistant.Abstractions;
using Assistant.Abstractions.Context;
using Assistant.Core.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory.Contributors;

internal sealed class UserPreferencesContributor(
    IPreferenceStore store,
    ITokenEstimator estimator,
    ILogger<UserPreferencesContributor> logger) : IContextContributor
{
    public string Name => "user.preferences";

    public int Priority => 30;

    public bool IsEnabled => true;

    public async ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        using var activity = MemoryTelemetry.ActivitySource.StartActivity(Name);

        var items = await store.ListAsync(request.UserId, ct);
        if (items.Count == 0)
        {
            return null;
        }

        var content = BuildFragmentText(items);
        var estimated = estimator.Estimate(content);
        if (estimated > MemoryContextDefaults.MaxPreferenceTokens)
        {
            MemoryTelemetry.PreferenceDropped.Add(1);
            return null;
        }

        MemoryTelemetry.PreferenceHits.Add(items.Count);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var keys = string.Join(", ", items.Select(item => item.Key));
            MemoryLog.PreferenceKeysIncluded(logger, keys);
        }

        activity?.SetTag("preference.keys", string.Join(",", items.Select(item => item.Key)));

        return ContextFragment.Text(Name, ContextPlacement.AppendedMessage, content, estimated);
    }

    private static string BuildFragmentText(IReadOnlyList<PreferenceItem> items)
    {
        var sb = new StringBuilder();
        sb.Append("Preferencias del usuario (hechos, no tu carácter):\n");
        foreach (var item in items)
        {
            sb.Append("- ");
            sb.Append(item.Key);
            sb.Append(": ");
            sb.Append(item.Value);
            sb.Append('\n');
        }

        sb.Append("No las recites. Usalas. Si hay que cambiarlas, update_preference. ");
        sb.Append("Si pregunta qué preferencias tiene, get_preferences.");
        return sb.ToString();
    }
}
