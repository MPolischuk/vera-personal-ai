namespace Assistant.Abstractions.Agents;

/// <summary>
/// A task this process records for a peer. The client writes the registry and does not open the network.
/// </summary>
public sealed record RemoteTaskRequest(
    Guid UserId,
    Guid ConversationId,
    Guid OriginTurnId,
    string IdempotencyKey,
    string Title,
    string Instruction);

/// <summary>
/// Records a <c>remote</c> task. A repeated idempotency key returns the same id.
/// </summary>
public interface IRemoteAgent
{
    /// <summary>
    /// Inserts a <c>remote</c> task in <c>submitted</c> and returns its id.
    /// </summary>
    /// <param name="request">Task to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task id. The same key returns the existing id.</returns>
    public ValueTask<Guid> SubmitAsync(RemoteTaskRequest request, CancellationToken ct);
}
