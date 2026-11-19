using System.Text.Json;

namespace Assistant.Api.Protocol;

internal sealed record MessageEnvelope(
    int V,
    string Type,
    string? CorrelationId,
    DateTimeOffset Timestamp,
    JsonElement Payload)
{
    public const int CurrentVersion = 1;

    public static MessageEnvelope Create<TPayload>(
        string type,
        string? correlationId,
        TPayload payload,
        TimeProvider clock)
        => new(
            CurrentVersion,
            type,
            correlationId,
            clock.GetUtcNow(),
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options));
}
