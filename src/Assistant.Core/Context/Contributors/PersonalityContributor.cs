using Assistant.Abstractions.Context;
using Assistant.Core.Personality;

namespace Assistant.Core.Context.Contributors;

internal sealed class PersonalityContributor(IPersonalityService personality) : IContextContributor
{
    public string Name => "personality";

    // Priority 0: never dropped. If it does not fit the budget, the turn fails,
    // because a reply without identity would be another assistant's (design §2.1).
    public int Priority => ContextPriority.Mandatory;

    public bool IsEnabled => true;

    public async ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        var active = await personality.GetActiveAsync(ct);

        // Tokens already estimated on load: the assembler does not re-estimate
        // the same text every turn (PT-03 D5).
        return ContextFragment.Text(
            Name, ContextPlacement.SystemPrompt, active.SystemPrompt, active.EstimatedTokens);
    }
}
