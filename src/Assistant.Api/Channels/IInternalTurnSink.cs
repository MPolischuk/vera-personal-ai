using Assistant.Core.Conversations;

namespace Assistant.Api.Channels;

/// <summary>
/// In-process sink for one turn. The hub and channel adapters both implement it.
/// The turn runner does not know which one it is driving.
/// </summary>
internal interface IInternalTurnSink
{
    /// <summary>One call per orchestrator update, in order.</summary>
    public ValueTask HandleAsync(TurnUpdate update, TurnRequest request, CancellationToken ct);

    /// <summary>Runs after the stream ends, including when the stream threw.</summary>
    public ValueTask CompleteAsync(CancellationToken ct);
}
