using Assistant.Abstractions.Events;

namespace Assistant.Core.Usage;

internal sealed class UsageRecordingSubscriber(IUsageStore store) : IDomainEventHandler<UsageRecorded>
{
    public ValueTask HandleAsync(UsageRecorded domainEvent, CancellationToken ct)
        => store.RecordAsync(
            new UsageRecord(
                domainEvent.ConversationId,
                MessageId: null,   // D8: the assistant message does not exist yet.
                domainEvent.Provider,
                domainEvent.Model,
                domainEvent.Kind,
                domainEvent.InputTokens,
                domainEvent.OutputTokens,
                domainEvent.EstimatedCostUsd)
            {
                TtsCharacters = domainEvent.TtsCharacters,
                SttSeconds = domainEvent.SttSeconds,
                EmbeddingTokens = domainEvent.EmbeddingTokens,
                CachedInputTokens = domainEvent.CachedInputTokens,
                CacheWriteTokens = domainEvent.CacheWriteTokens,
            },
            ct);
}
