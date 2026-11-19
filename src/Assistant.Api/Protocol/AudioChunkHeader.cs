namespace Assistant.Api.Protocol;

/// <summary>
/// Binary hub audio header for <c>ReceiveAudio</c>. PCM body travels as a separate argument.
/// </summary>
internal sealed record AudioChunkHeader(
    int V,
    string Type,
    string? CorrelationId,
    DateTimeOffset Timestamp,
    int Sequence);
