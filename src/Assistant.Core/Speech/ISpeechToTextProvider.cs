namespace Assistant.Core.Speech;

public interface ISpeechToTextProvider
{
    public IAsyncEnumerable<TranscriptionEvent> TranscribeAsync(
        IAsyncEnumerable<AudioChunk> audio,
        TranscriptionOptions options,
        CancellationToken ct);
}
