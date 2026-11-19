using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class SuggestionAuditStore(AssistantDbContext db) : ISuggestionAuditStore
{
    public const int ReasonMaxLength = 200;

    public const int SourceRefMaxLength = 80;

    public async ValueTask<SuggestionAudit> InsertAsync(
        NewSuggestionAudit draft, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Outcome is not (SuggestionOutcomes.Delivered or SuggestionOutcomes.Discarded))
        {
            throw new ArgumentException("The suggestion outcome is not supported.", nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.Reason) || draft.Reason.Trim().Length > ReasonMaxLength)
        {
            throw new ArgumentException("The suggestion reason must be 1 to 200 characters.", nameof(draft));
        }

        var sourceRef = string.IsNullOrWhiteSpace(draft.SourceRef) ? null : draft.SourceRef.Trim();
        if (sourceRef is { Length: > SourceRefMaxLength })
        {
            throw new ArgumentException("The source ref must be at most 80 characters.", nameof(draft));
        }

        var entity = new SuggestionAuditEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = draft.UserId,
            LocalDate = draft.LocalDate,
            Outcome = draft.Outcome,
            Reason = draft.Reason.Trim(),
            SourceRef = sourceRef,
            CreatedAt = now,
        };
        db.Set<SuggestionAuditEntity>().Add(entity);
        await db.SaveChangesAsync(ct);
        return ToModel(entity);
    }

    public async ValueTask<int> CountDeliveredAsync(Guid userId, DateOnly localDate, CancellationToken ct)
        => await db.Set<SuggestionAuditEntity>().CountAsync(
            audit => audit.UserId == userId
                && audit.LocalDate == localDate
                && audit.Outcome == SuggestionOutcomes.Delivered,
            ct);

    private static SuggestionAudit ToModel(SuggestionAuditEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            entity.LocalDate,
            entity.Outcome,
            entity.Reason,
            entity.SourceRef,
            entity.CreatedAt);
}
