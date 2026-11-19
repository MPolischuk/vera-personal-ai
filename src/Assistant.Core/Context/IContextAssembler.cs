using Assistant.Abstractions.Context;

namespace Assistant.Core.Context;

public interface IContextAssembler
{
    public ValueTask<AssembledContext> AssembleAsync(ContextRequest request, CancellationToken ct);
}

public sealed record AssembledContext(
    IReadOnlyList<ContextFragment> Fragments,
    int EstimatedTokens,
    bool ContainsUntrustedContent = false);
