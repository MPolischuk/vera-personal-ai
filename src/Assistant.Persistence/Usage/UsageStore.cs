using Assistant.Core.Usage;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Usage;

internal sealed class UsageStore(AssistantDbContext db) : IUsageStore
{
    public async ValueTask RecordAsync(UsageRecord record, CancellationToken ct)
    {
        db.UsageRecords.Add(new UsageRecordEntity
        {
            Id = Guid.CreateVersion7(),
            ConversationId = record.ConversationId,
            MessageId = record.MessageId,
            Provider = record.Provider,
            Model = record.Model,
            TaskKind = record.Kind.ToString(),
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            EstimatedCostUsd = record.EstimatedCostUsd,
            TtsCharacters = record.TtsCharacters,
            SttSeconds = record.SttSeconds,
            EmbeddingTokens = record.EmbeddingTokens,
            CachedInputTokens = record.CachedInputTokens,
            CacheWriteTokens = record.CacheWriteTokens,
        });

        await db.SaveChangesAsync(ct);
    }

    public async ValueTask<UsageSummary> GetSummaryAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct)
    {
        // Anonymous projection: EF cannot translate constructors of external records in GroupBy.
        var rows = await db.UsageRecords
            .AsNoTracking()
            .Where(u => u.CreatedAt >= since && u.Conversation.UserId == userId)
            .GroupBy(u => new { u.Provider, u.Model })
            .Select(g => new
            {
                g.Key.Provider,
                g.Key.Model,
                InputTokens = g.Sum(u => u.InputTokens),
                OutputTokens = g.Sum(u => u.OutputTokens),
                EstimatedCostUsd = g.Sum(u => u.EstimatedCostUsd),
                Turns = g.Count(),
            })
            .OrderBy(r => r.Provider)
            .ThenBy(r => r.Model)
            .ToListAsync(ct);

        var byModel = rows
            .Select(r => new UsageByModel(
                r.Provider, r.Model, r.InputTokens, r.OutputTokens, r.EstimatedCostUsd))
            .ToList();

        return new UsageSummary(
            since,
            rows.Sum(r => r.Turns),
            byModel.Sum(r => r.InputTokens),
            byModel.Sum(r => r.OutputTokens),
            byModel.Sum(r => r.EstimatedCostUsd),
            byModel);
    }
}
