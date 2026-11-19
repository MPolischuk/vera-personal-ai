namespace Assistant.Core.Confirmations;

/// <summary>
/// Creates the single open pending confirmation for a conversation.
/// </summary>
public sealed record CreatePendingConfirmationRequest(
    Guid ConversationId,
    Guid UserId,
    string ToolName,
    string? ArgumentsJson,
    string Prompt,
    DateTimeOffset ExpiresAt,
    bool TurnContaminated = false);
