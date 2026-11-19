using Assistant.Abstractions.Context;
using Assistant.Core.Personality;

namespace Assistant.Core.Context.Contributors;

/// <summary>
/// Repeats the cue instruction as the last appended system text (priority 25),
/// immediately before the user message. Live Gemini/Llama ignore a cue buried
/// in the identity prefix when temporal/situational come after it.
/// </summary>
internal sealed class CharacterCueContributor : IContextContributor
{
    public string Name => "character.cue";

    public int Priority => 25;

    public bool IsEnabled => true;

    public ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
        => ValueTask.FromResult<ContextFragment?>(
            ContextFragment.Text(
                Name,
                ContextPlacement.AppendedMessage,
                PersonalityPromptComposer.CharacterCueInstruction));
}
