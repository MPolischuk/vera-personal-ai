using Assistant.Abstractions.Context;

namespace Assistant.Core.Context.Contributors;

/// <summary>
/// Kept registered so the contributor order stays stable. The clock now travels
/// on the outgoing user message, not in an appended fragment.
/// </summary>
internal sealed class TemporalContributor : IContextContributor
{
    public string Name => "temporal";

    public int Priority => 10;

    public bool IsEnabled => true;

    public ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        _ = request;
        _ = ct;
        return ValueTask.FromResult<ContextFragment?>(null);
    }
}
