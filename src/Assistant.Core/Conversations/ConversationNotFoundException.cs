namespace Assistant.Core.Conversations;

public sealed class ConversationNotFoundException : InvalidOperationException
{
    public ConversationNotFoundException(Guid conversationId)
        : base($"Conversation {conversationId} does not exist or does not belong to the user.")
    {
        ConversationId = conversationId;
    }

    public ConversationNotFoundException(Guid conversationId, Exception innerException)
        : base($"Conversation {conversationId} does not exist or does not belong to the user.", innerException)
    {
        ConversationId = conversationId;
    }

    public Guid ConversationId { get; }
}
