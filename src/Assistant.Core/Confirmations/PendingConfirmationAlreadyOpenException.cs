namespace Assistant.Core.Confirmations;

public sealed class PendingConfirmationAlreadyOpenException : InvalidOperationException
{
    public PendingConfirmationAlreadyOpenException(Guid conversationId)
        : base($"Conversation {conversationId} already has an open pending confirmation.")
    {
        ConversationId = conversationId;
    }

    public PendingConfirmationAlreadyOpenException(Guid conversationId, Exception innerException)
        : base($"Conversation {conversationId} already has an open pending confirmation.", innerException)
    {
        ConversationId = conversationId;
    }

    public Guid ConversationId { get; }
}
