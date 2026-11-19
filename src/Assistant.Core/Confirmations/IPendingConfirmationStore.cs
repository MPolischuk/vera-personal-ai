namespace Assistant.Core.Confirmations;

/// <summary>
/// Persistence for confirmation requests. Every read that targets a specific row or
/// conversation requires the owning user: there is no way to load a pending
/// confirmation without stating whose conversation it is.
/// </summary>
public interface IPendingConfirmationStore
{
    /// <summary>
    /// Inserts the only open pending confirmation for the conversation. Fails when the
    /// conversation is missing/unowned or another open row already exists.
    /// </summary>
    public ValueTask<PendingConfirmation> CreateAsync(
        CreatePendingConfirmationRequest request, CancellationToken ct);

    /// <summary>
    /// Loads a row by id only when it belongs to the given conversation and user.
    /// </summary>
    public ValueTask<PendingConfirmation?> FindAsync(
        Guid confirmationId, Guid conversationId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Loads a row by id when the conversation belongs to the user (wire payloads
    /// that carry only <c>confirmationId</c>).
    /// </summary>
    public ValueTask<PendingConfirmation?> FindOwnedAsync(
        Guid confirmationId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Returns the open pending confirmation for the conversation, if any.
    /// </summary>
    public ValueTask<PendingConfirmation?> FindOpenAsync(
        Guid conversationId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Atomically resolves a still-open row (<c>UPDATE … WHERE resolved_at IS NULL</c>).
    /// Ownership and outcome-specific checks (tool name, expiry) belong here.
    /// </summary>
    public ValueTask<ConfirmationClaimResult> TryClaimAsync(
        Guid confirmationId,
        Guid conversationId,
        Guid userId,
        ConfirmationResolutionOutcome outcome,
        string? expectedToolName,
        DateTimeOffset utcNow,
        CancellationToken ct);

    /// <summary>
    /// Open rows whose <c>expires_at</c> is at or before <paramref name="utcNow"/>,
    /// oldest first, capped by <paramref name="take"/>.
    /// </summary>
    public ValueTask<IReadOnlyList<PendingConfirmation>> ListExpiredAsync(
        DateTimeOffset utcNow, int take, CancellationToken ct);
}
