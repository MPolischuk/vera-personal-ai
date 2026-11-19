namespace Assistant.Core.Confirmations;

/// <summary>
/// Domain view of a <c>pending_confirmations</c> row. No EF types.
/// <see cref="UserId"/> comes from the owning conversation (ownership seam).
/// </summary>
public sealed record PendingConfirmation(
    Guid Id,
    Guid ConversationId,
    Guid UserId,
    string ToolName,
    string? ArgumentsJson,
    string Prompt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt = null,
    ConfirmationResolutionOutcome? ResolvedOutcome = null,
    bool TurnContaminated = false);
