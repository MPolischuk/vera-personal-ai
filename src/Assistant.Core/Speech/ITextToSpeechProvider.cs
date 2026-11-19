namespace Assistant.Core.Speech;

public interface ITextToSpeechProvider
{
    public IAsyncEnumerable<AudioChunk> SynthesizeAsync(
        string text,
        SynthesisOptions options,
        CancellationToken ct);
}
