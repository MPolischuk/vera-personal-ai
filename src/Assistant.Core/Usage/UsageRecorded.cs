using Assistant.Abstractions.Events;
using Assistant.Core.Models;

namespace Assistant.Core.Usage;

/// <summary>Usage for a model or speech call. <see cref="TokensReported"/> is false if tokens came from the estimator (D10).</summary>
public sealed record UsageRecorded(
    Guid ConversationId,
    Guid TurnId,
    TaskKind Kind,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd,
    bool TokensReported,
    TimeSpan Duration) : IDomainEvent
{
    /// <summary>Characters synthesized in a TTS call. Default 0 keeps LLM rows unchanged.</summary>
    public int TtsCharacters { get; init; }

    /// <summary>Seconds of PCM audio transcribed in an STT call. Default 0 keeps LLM rows unchanged.</summary>
    public double SttSeconds { get; init; }

    /// <summary>Tokens consumed by an embedding call. Default 0 keeps LLM rows unchanged.</summary>
    public int EmbeddingTokens { get; init; }

    /// <summary>Input tokens served from the provider cache. Default 0 keeps rows without cache unchanged.</summary>
    public int CachedInputTokens { get; init; }

    /// <summary>Tokens written to the provider cache. Default 0 keeps rows without a cache write unchanged.</summary>
    public int CacheWriteTokens { get; init; }
}
