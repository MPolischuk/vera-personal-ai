using System.Data;
using Assistant.Core.Confirmations;
using Assistant.Core.Conversations;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Assistant.Persistence.Confirmations;

internal sealed class PendingConfirmationStore(AssistantDbContext db) : IPendingConfirmationStore
{
    private const int MaxCreateAttempts = 3;

    public async ValueTask<PendingConfirmation> CreateAsync(
        CreatePendingConfirmationRequest request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            try
            {
                var conversation = await db.Conversations
                        .FirstOrDefaultAsync(
                            c => c.Id == request.ConversationId && c.UserId == request.UserId, ct)
                    ?? throw new ConversationNotFoundException(request.ConversationId);

                var hasOpen = await db.PendingConfirmations.AnyAsync(
                    p => p.ConversationId == conversation.Id && p.ResolvedAt == null, ct);

                if (hasOpen)
                {
                    throw new PendingConfirmationAlreadyOpenException(conversation.Id);
                }

                var entity = new PendingConfirmationEntity
                {
                    Id = Guid.CreateVersion7(),
                    ConversationId = conversation.Id,
                    ToolName = request.ToolName,
                    Arguments = request.ArgumentsJson,
                    Prompt = request.Prompt,
                    ExpiresAt = request.ExpiresAt,
                    TurnContaminated = request.TurnContaminated,
                };

                db.PendingConfirmations.Add(entity);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return ToDomain(entity, conversation.UserId);
            }
            catch (Exception e) when (IsSerializationFailure(e) && attempt < MaxCreateAttempts - 1)
            {
                await tx.RollbackAsync(ct);
                DetachPendingAdds();
            }
            catch
            {
                await tx.RollbackAsync(ct);
                DetachPendingAdds();
                throw;
            }
        }
    }

    public async ValueTask<PendingConfirmation?> FindAsync(
        Guid confirmationId, Guid conversationId, Guid userId, CancellationToken ct)
    {
        var row = await db.PendingConfirmations
            .AsNoTracking()
            .Where(p => p.Id == confirmationId
                && p.ConversationId == conversationId
                && p.Conversation.UserId == userId)
            .Select(p => new Row(
                p.Id,
                p.ConversationId,
                p.Conversation.UserId,
                p.ToolName,
                p.Arguments,
                p.Prompt,
                p.ExpiresAt,
                p.CreatedAt,
                p.ResolvedAt,
                p.ResolvedOutcome,
                p.TurnContaminated))
            .FirstOrDefaultAsync(ct);

        return row is null ? null : ToDomain(row);
    }

    public async ValueTask<PendingConfirmation?> FindOwnedAsync(
        Guid confirmationId, Guid userId, CancellationToken ct)
    {
        var row = await db.PendingConfirmations
            .AsNoTracking()
            .Where(p => p.Id == confirmationId && p.Conversation.UserId == userId)
            .Select(p => new Row(
                p.Id,
                p.ConversationId,
                p.Conversation.UserId,
                p.ToolName,
                p.Arguments,
                p.Prompt,
                p.ExpiresAt,
                p.CreatedAt,
                p.ResolvedAt,
                p.ResolvedOutcome,
                p.TurnContaminated))
            .FirstOrDefaultAsync(ct);

        return row is null ? null : ToDomain(row);
    }

    public async ValueTask<PendingConfirmation?> FindOpenAsync(
        Guid conversationId, Guid userId, CancellationToken ct)
    {
        var row = await db.PendingConfirmations
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId
                && p.Conversation.UserId == userId
                && p.ResolvedAt == null)
            .Select(p => new Row(
                p.Id,
                p.ConversationId,
                p.Conversation.UserId,
                p.ToolName,
                p.Arguments,
                p.Prompt,
                p.ExpiresAt,
                p.CreatedAt,
                p.ResolvedAt,
                p.ResolvedOutcome,
                p.TurnContaminated))
            .FirstOrDefaultAsync(ct);

        return row is null ? null : ToDomain(row);
    }

    public async ValueTask<ConfirmationClaimResult> TryClaimAsync(
        Guid confirmationId,
        Guid conversationId,
        Guid userId,
        ConfirmationResolutionOutcome outcome,
        string? expectedToolName,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var query = db.PendingConfirmations.Where(p =>
            p.Id == confirmationId
            && p.ConversationId == conversationId
            && p.Conversation.UserId == userId
            && p.ResolvedAt == null);

        if (expectedToolName is not null)
        {
            query = query.Where(p => p.ToolName == expectedToolName);
        }

        query = outcome switch
        {
            ConfirmationResolutionOutcome.Approved => query.Where(p => p.ExpiresAt > utcNow),
            ConfirmationResolutionOutcome.Expired => query.Where(p => p.ExpiresAt <= utcNow),
            ConfirmationResolutionOutcome.Denied => query,
            _ => query,
        };

        var rows = await query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(p => p.ResolvedAt, utcNow)
                .SetProperty(p => p.ResolvedOutcome, ToStorage(outcome)),
            ct);

        var current = await FindAsync(confirmationId, conversationId, userId, ct);
        return new ConfirmationClaimResult(rows == 1, current);
    }

    public async ValueTask<IReadOnlyList<PendingConfirmation>> ListExpiredAsync(
        DateTimeOffset utcNow, int take, CancellationToken ct)
    {
        var rows = await db.PendingConfirmations
            .AsNoTracking()
            .Where(p => p.ResolvedAt == null && p.ExpiresAt <= utcNow)
            .OrderBy(p => p.ExpiresAt)
            .Take(take)
            .Select(p => new Row(
                p.Id,
                p.ConversationId,
                p.Conversation.UserId,
                p.ToolName,
                p.Arguments,
                p.Prompt,
                p.ExpiresAt,
                p.CreatedAt,
                p.ResolvedAt,
                p.ResolvedOutcome,
                p.TurnContaminated))
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    private void DetachPendingAdds()
    {
        foreach (var entry in db.ChangeTracker.Entries<PendingConfirmationEntity>()
                     .Where(e => e.State is EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsSerializationFailure(Exception e)
    {
        for (var current = e; current is not null; current = current.InnerException!)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
            {
                return true;
            }
        }

        return false;
    }

    private static string ToStorage(ConfirmationResolutionOutcome outcome) => outcome switch
    {
        ConfirmationResolutionOutcome.Approved => "approved",
        ConfirmationResolutionOutcome.Denied => "denied",
        ConfirmationResolutionOutcome.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static ConfirmationResolutionOutcome? FromStorage(string? value) => value switch
    {
        "approved" => ConfirmationResolutionOutcome.Approved,
        "denied" => ConfirmationResolutionOutcome.Denied,
        "expired" => ConfirmationResolutionOutcome.Expired,
        null => null,
        _ => throw new InvalidOperationException($"Unknown confirmation outcome '{value}'."),
    };

    private static PendingConfirmation ToDomain(PendingConfirmationEntity e, Guid userId)
        => new(
            e.Id,
            e.ConversationId,
            userId,
            e.ToolName,
            e.Arguments,
            e.Prompt,
            e.ExpiresAt,
            e.CreatedAt,
            e.ResolvedAt,
            FromStorage(e.ResolvedOutcome),
            e.TurnContaminated);

    private static PendingConfirmation ToDomain(Row row)
        => new(
            row.Id,
            row.ConversationId,
            row.UserId,
            row.ToolName,
            row.Arguments,
            row.Prompt,
            row.ExpiresAt,
            row.CreatedAt,
            row.ResolvedAt,
            FromStorage(row.ResolvedOutcome),
            row.TurnContaminated);

    private sealed record Row(
        Guid Id,
        Guid ConversationId,
        Guid UserId,
        string ToolName,
        string? Arguments,
        string Prompt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ResolvedAt,
        string? ResolvedOutcome,
        bool TurnContaminated);
}
