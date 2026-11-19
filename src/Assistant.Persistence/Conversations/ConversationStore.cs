using System.Text.Json;
using Assistant.Core.Conversations;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Assistant.Persistence.Conversations;

internal sealed class ConversationStore(AssistantDbContext db, TimeProvider clock) : IConversationStore
{
    public async ValueTask<Conversation> CreateAsync(Guid userId, string? title, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var entity = new ConversationEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Title = title,
            StartedAt = now,
            LastMessageAt = now,
        };

        db.Conversations.Add(entity);
        await db.SaveChangesAsync(ct);

        return ToDomain(entity);
    }

    public async ValueTask<Conversation?> FindAsync(Guid conversationId, Guid userId, CancellationToken ct)
        => await db.Conversations
            .Where(c => c.Id == conversationId && c.UserId == userId)
            .Select(c => new Conversation(c.Id, c.UserId, c.Title, c.StartedAt, c.LastMessageAt))
            .FirstOrDefaultAsync(ct);

    public async ValueTask<IReadOnlyList<Conversation>> ListAsync(
        Guid userId, int skip, int take, CancellationToken ct)
        => await db.Conversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.LastMessageAt)
            .Skip(skip)
            .Take(take)
            .Select(c => new Conversation(c.Id, c.UserId, c.Title, c.StartedAt, c.LastMessageAt))
            .ToListAsync(ct);

    public async ValueTask<IReadOnlyList<ConversationMessage>> GetHistoryAsync(
        Guid conversationId, Guid userId, int maxMessages, CancellationToken ct)
    {
        var messages = await db.Messages
            .Where(m => m.ConversationId == conversationId && m.Conversation.UserId == userId)
            .OrderByDescending(m => m.Sequence)
            .Take(maxMessages)
            .Select(m => new ConversationMessage(
                m.Id, m.ConversationId, m.Role, m.Content,
                m.Sequence, m.WasInterrupted, m.Metadata, m.CreatedAt))
            .ToListAsync(ct);

        // Fetch newest first, return in reading order.
        messages.Reverse();
        return messages;
    }

    public async ValueTask<bool> SoftDeleteAsync(Guid conversationId, Guid userId, CancellationToken ct)
    {
        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);

        if (conversation is null)
        {
            return false;
        }

        // D8: messages are not stamped. The conversation filter is enough.
        conversation.DeletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return true;
    }

    public async ValueTask<bool> TryUpdateMessageContentAsync(
        Guid conversationId,
        Guid userId,
        Guid messageId,
        string content,
        bool wasInterrupted,
        CancellationToken ct)
    {
        var message = await db.Messages
            .FirstOrDefaultAsync(
                m => m.Id == messageId
                    && m.ConversationId == conversationId
                    && m.Conversation.UserId == userId,
                ct);

        if (message is null)
        {
            return false;
        }

        message.Content = content;
        message.WasInterrupted = wasInterrupted;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async ValueTask<ConversationMessage> AppendMessageAsync(
        AppendMessageRequest request, CancellationToken ct)
    {
        var conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.Id == request.ConversationId && c.UserId == request.UserId, ct)
            ?? throw new ConversationNotFoundException(request.ConversationId);

        var entity = new MessageEntity
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Role = request.Role,
            Content = request.Content,
            Metadata = request.Metadata,
            WasInterrupted = request.WasInterrupted,
        };

        foreach (var invocation in request.Invocations)
        {
            entity.ToolInvocations.Add(new ToolInvocationEntity
            {
                Id = Guid.CreateVersion7(),
                ToolName = invocation.ToolName,
                ExtensionName = invocation.ExtensionName,
                Arguments = invocation.ArgumentsJson,
                PermissionLevel = invocation.Level.ToString(),
                PermissionOutcome = invocation.Outcome.ToString(),
                PermissionReason = invocation.Reason,
                // Column is jsonb; tool content is text that is only sometimes JSON.
                Result = invocation.ResultJson is null
                    ? null
                    : $$"""{"content": {{JsonSerializer.Serialize(invocation.ResultJson)}}}""",
                Error = invocation.Error,
                DurationMs = (int)invocation.Duration.TotalMilliseconds,
            });
        }

        db.Messages.Add(entity);
        conversation.LastMessageAt = clock.GetUtcNow();

        for (var attempt = 0; ; attempt++)
        {
            entity.Sequence = await NextSequenceAsync(conversation.Id, ct);

            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException e) when (IsSequenceCollision(e) && attempt == 0)
            {
                // Another turn inserted first. Entity is still Added, so recalculating is enough.
                // A second collision is not a race: it is a defect.
            }
        }

        return ToDomain(entity);
    }

    // D7/D8: soft-deleted messages still occupy their number for the unique index,
    // and the global filter hides them. Without IgnoreQueryFilters, the first soft
    // delete makes the next insert collide.
    private async Task<int> NextSequenceAsync(Guid conversationId, CancellationToken ct)
        => 1 + (await db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversationId)
            .MaxAsync(m => (int?)m.Sequence, ct) ?? 0);

    private static bool IsSequenceCollision(DbUpdateException e)
        => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static Conversation ToDomain(ConversationEntity e)
        => new(e.Id, e.UserId, e.Title, e.StartedAt, e.LastMessageAt);

    private static ConversationMessage ToDomain(MessageEntity e)
        => new(e.Id, e.ConversationId, e.Role, e.Content, e.Sequence, e.WasInterrupted, e.Metadata, e.CreatedAt);
}
