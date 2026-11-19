namespace Assistant.Abstractions.Agents;

/// <summary>
/// A proposal from a peer. It is not an order: the user still has to accept it.
/// </summary>
public sealed record InboundTaskRequest(
    Guid UserId,
    Guid ConversationId,
    Guid OriginTurnId,
    string IdempotencyKey,
    string Title,
    string Instruction);

/// <summary>
/// Accepts a peer proposal into the task registry. It does not claim a worker.
/// </summary>
public interface IAgentTaskIngress
{
    /// <summary>
    /// Inserts an <c>inbound</c> task in <c>input-required</c> and publishes one confirmation.
    /// </summary>
    /// <param name="request">Proposal to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task id.</returns>
    public ValueTask<Guid> AcceptAsync(InboundTaskRequest request, CancellationToken ct);
}
