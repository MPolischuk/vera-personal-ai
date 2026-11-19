using System.Text;
using Assistant.Abstractions;
using Assistant.Abstractions.Context;
using Assistant.Core.Conversations;
using Assistant.Core.Personality;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Context.Contributors;

internal sealed class SituationalCharacterContributor(
    IPersonalityService personality,
    ITokenEstimator estimator,
    ILogger<SituationalCharacterContributor> logger) : IContextContributor
{
    public string Name => "character.situational";

    public int Priority => 20;

    public bool IsEnabled => true;

    public async ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        using var activity = TurnTelemetry.ActivitySource.StartActivity("character.situational");

        var catalog = (await personality.GetActiveAsync(ct)).Profile.Character;
        var texts = CollectUserTexts(request);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            tokens.UnionWith(CharacterTagMatcher.Tokenize(text));
        }

        var selection = CharacterTagMatcher.Select(catalog, tokens);
        if (selection.IsEmpty)
        {
            return null;
        }

        var content = BuildFragmentText(selection);
        var estimated = estimator.Estimate(content);
        if (estimated > CharacterTagMatcher.MaxSituationalTokens)
        {
            CharacterTelemetry.SituationalDropped.Add(1);
            return null;
        }

        foreach (var id in selection.Ids)
        {
            CharacterTelemetry.SituationalHits.Add(1, new KeyValuePair<string, object?>("id", id));
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            var ids = string.Join(", ", selection.Ids);
            PersonalityLog.SituationalIds(logger, ids);
        }

        activity?.SetTag("character.ids", string.Join(",", selection.Ids));

        return ContextFragment.Text(
            Name, ContextPlacement.AppendedMessage, content, estimated);
    }

    private static List<string> CollectUserTexts(ContextRequest request)
    {
        var texts = new List<string> { request.UserMessage };
        texts.AddRange(
            request.RecentHistory
                .Where(m => m.Role is MessageRole.User)
                .TakeLast(CharacterTagMatcher.RecentUserMessages)
                .Select(m => m.Content));
        return texts;
    }

    private static string BuildFragmentText(CharacterTagMatcher.Selection selection)
    {
        var sb = new StringBuilder();
        sb.Append("Para este turno, tené presente:\n");

        foreach (var line in selection.Likes.Select(e => e.Line)
                     .Concat(selection.Dislikes.Select(e => e.Line))
                     .Concat(selection.Quirks.Select(e => e.Line)))
        {
            sb.Append("- ");
            sb.Append(line);
            sb.Append('\n');
        }

        sb.Append("No recites la lista. Que se note si pega; si no pega, callate.");
        return sb.ToString();
    }
}
