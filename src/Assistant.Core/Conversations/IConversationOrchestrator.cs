namespace Assistant.Core.Conversations;

public interface IConversationOrchestrator
{
    public IAsyncEnumerable<TurnUpdate> RunTurnAsync(TurnRequest request, CancellationToken ct);
}

/// <summary>
/// A normal turn carries <see cref="UserMessage"/>. A resume carries
/// <see cref="ConfirmationId"/> and must not append another user row (D8).
/// </summary>
public sealed record TurnRequest(
    Guid ConversationId,
    Guid UserId,
    string UserMessage,
    Guid? ConfirmationId = null,
    bool Approved = false,
    bool Expired = false)
{
    public bool IsResume => ConfirmationId is not null;

    public static TurnRequest Resume(
        Guid conversationId, Guid userId, Guid confirmationId, bool approved)
        => new(conversationId, userId, string.Empty, confirmationId, approved);

    public static TurnRequest Expire(
        Guid conversationId, Guid userId, Guid confirmationId)
        => new(conversationId, userId, string.Empty, confirmationId, Approved: false, Expired: true);
}
