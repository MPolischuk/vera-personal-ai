namespace Assistant.Api.Channels;

/// <summary>
/// Transcribes an inbound voice note to text. The OGG/Opus implementation arrives in PT-82.
/// </summary>
internal interface IInboundAudioTranscriber
{
    public ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<byte> data,
        string mimeType,
        TimeSpan duration,
        Guid userId,
        Guid conversationId,
        CancellationToken ct);
}

/// <summary>
/// Placeholder until PT-82 registers the real transcriber. Audio turns are rejected
/// with <c>stt_failed</c> and the bytes are not logged.
/// </summary>
internal sealed class UnavailableInboundAudioTranscriber : IInboundAudioTranscriber
{
    public ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<byte> data,
        string mimeType,
        TimeSpan duration,
        Guid userId,
        Guid conversationId,
        CancellationToken ct)
        => throw new InvalidOperationException("Inbound audio transcription is not configured.");
}
