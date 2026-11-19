using Assistant.Abstractions.Context;

namespace Assistant.Core.Context.Contributors;

internal sealed class ConversationHistoryContributor : IContextContributor
{
    public string Name => "history";

    // 100, as in design §2.1. First to trim, last to drop.
    public int Priority => 100;

    public bool IsEnabled => true;

    public ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
        => ValueTask.FromResult<ContextFragment?>(
            request.RecentHistory.Count == 0
                ? null
                : new ContextFragment(
                    Name,
                    ContextPlacement.ConversationHistory,
                    // One part per message, in order: D5 positional contract.
                    [.. request.RecentHistory.Select(m => m.Content)],
                    FragmentTrim.FromStart));
}
