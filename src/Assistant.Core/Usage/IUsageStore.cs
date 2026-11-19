using Assistant.Core.Models;

namespace Assistant.Core.Usage;

public interface IUsageStore
{
    public ValueTask RecordAsync(UsageRecord record, CancellationToken ct);

    public ValueTask<UsageSummary> GetSummaryAsync(Guid userId, DateTimeOffset since, CancellationToken ct);
}

/// <summary>
/// A <c>usage_records</c> row. Voice fields are declared as zero and filled in by
/// stage 1b: the table already has them and a second method would be worse.
/// </summary>
public sealed record UsageRecord(
    Guid ConversationId,
    Guid? MessageId,
    string Provider,
    string Model,
    TaskKind Kind,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd)
{
    public int TtsCharacters { get; init; }

    public double SttSeconds { get; init; }

    public int EmbeddingTokens { get; init; }

    public int CachedInputTokens { get; init; }

    public int CacheWriteTokens { get; init; }
}

public sealed record UsageSummary(
    DateTimeOffset Since,
    int Turns,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd,
    IReadOnlyList<UsageByModel> ByModel);

public sealed record UsageByModel(
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd);
