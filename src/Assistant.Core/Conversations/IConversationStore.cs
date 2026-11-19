using Assistant.Abstractions;
using Assistant.Abstractions.Tools;
using Assistant.Core.Permissions;

namespace Assistant.Core.Conversations;

/// <summary>
/// Access to conversations and their messages. Every operation requires the owning
/// user: there is no way to read a conversation without stating whose it is.
/// </summary>
public interface IConversationStore
{
    public ValueTask<Conversation> CreateAsync(Guid userId, string? title, CancellationToken ct);

    public ValueTask<Conversation?> FindAsync(Guid conversationId, Guid userId, CancellationToken ct);

    public ValueTask<IReadOnlyList<Conversation>> ListAsync(Guid userId, int skip, int take, CancellationToken ct);

    public ValueTask<ConversationMessage> AppendMessageAsync(AppendMessageRequest request, CancellationToken ct);

    /// <summary>Returns the last <paramref name="maxMessages"/> messages in reading order.</summary>
    public ValueTask<IReadOnlyList<ConversationMessage>> GetHistoryAsync(
        Guid conversationId, Guid userId, int maxMessages, CancellationToken ct);

    /// <summary>
    /// Updates content and interruption flag of an existing message. False when the
    /// conversation is not owned by <paramref name="userId"/> or the message is missing.
    /// </summary>
    public ValueTask<bool> TryUpdateMessageContentAsync(
        Guid conversationId,
        Guid userId,
        Guid messageId,
        string content,
        bool wasInterrupted,
        CancellationToken ct);

    public ValueTask<bool> SoftDeleteAsync(Guid conversationId, Guid userId, CancellationToken ct);
}

public sealed record AppendMessageRequest(
    Guid ConversationId,
    Guid UserId,
    MessageRole Role,
    string Content,
    string? Metadata = null,
    bool WasInterrupted = false)
{
    /// <summary>
    /// Written to tool_invocations with the newly created message id in the same
    /// SaveChanges. If the message is not saved, there are no orphan invocation rows (D9).
    /// </summary>
    public IReadOnlyList<ToolInvocationRecord> Invocations { get; init; } = [];
}

public sealed record ToolInvocationRecord(
    string ToolName,
    string ExtensionName,
    string ArgumentsJson,
    PermissionLevel Level,
    PermissionOutcome Outcome,
    string? Reason,
    string? ResultJson,
    string? Error,
    TimeSpan Duration);
