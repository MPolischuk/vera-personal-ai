using System.Runtime.CompilerServices;
using Assistant.Core.Personality;
using Assistant.Core.Speech;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;

namespace Assistant.Api.Channels;

/// <summary>
/// Decodes an OGG/Opus voice note to PCM s16le 16 kHz mono and streams it to the
/// speech provider already registered in DI (usage accounting included).
/// </summary>
internal sealed class InboundAudioTranscriber(ISpeechToTextProvider? speech, IPersonalityService personality)
    : IInboundAudioTranscriber
{
    public const int MaxVoiceSeconds = 120;

    public const int OutputRate = 16_000;

    private const int EncodeRate = 48_000;

    private const int ChunkBytes = OutputRate * 2 / 10;

    public async ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<byte> data,
        string mimeType,
        TimeSpan duration,
        Guid userId,
        Guid conversationId,
        CancellationToken ct)
    {
        _ = userId;
        if (mimeType.Length > 0
            && !mimeType.Contains("ogg", StringComparison.OrdinalIgnoreCase)
            && !mimeType.Contains("opus", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The voice note is not OGG/Opus.");
        }

        if (duration > TimeSpan.FromSeconds(MaxVoiceSeconds))
        {
            throw new InboundAudioTooLongException();
        }

        if (speech is null)
        {
            throw new InboundAudioUnavailableException();
        }

        var pcm = DecodePcm16(data.Span);
        var language = await ResolveLanguageAsync(ct).ConfigureAwait(false);
        var finals = new List<string>();
        await foreach (var evt in speech.TranscribeAsync(
                           Chunk(pcm, ct),
                           new TranscriptionOptions(new SpeechCallContext(conversationId, Guid.Empty), language),
                           ct).ConfigureAwait(false))
        {
            if (evt.IsFinal && !string.IsNullOrWhiteSpace(evt.Text))
            {
                finals.Add(evt.Text.Trim());
            }
        }

        return string.Join(' ', finals);
    }

    internal static byte[] DecodePcm16(ReadOnlySpan<byte> ogg)
    {
        using var input = new MemoryStream(ogg.ToArray());
        var decoder = OpusCodecFactory.CreateDecoder(OutputRate, 1, messageLogger: null);
        var reader = new OpusOggReadStream(decoder, input);
        var samples = new List<short>();
        var cap = MaxVoiceSeconds * OutputRate;
        while (reader.HasNextPacket)
        {
            var packet = reader.DecodeNextPacket();
            if (packet is not { Length: > 0 })
            {
                continue;
            }

            if ((long)samples.Count + packet.Length > cap)
            {
                throw new InboundAudioTooLongException();
            }

            samples.AddRange(packet);
        }

        var pcm = samples.ToArray();
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    internal static byte[] EncodeTone(double seconds, int frequencyHz, int? complexity = null)
    {
        var encoder = OpusCodecFactory.CreateEncoder(
            EncodeRate, 1, OpusApplication.OPUS_APPLICATION_AUDIO, messageLogger: null);
        if (complexity is int value)
        {
            encoder.Complexity = value;
        }
        using var output = new MemoryStream();
        var writer = new OpusOggWriteStream(encoder, output);
        var frame = new short[960];
        var total = (int)Math.Round(seconds * EncodeRate / frame.Length) * frame.Length;
        for (var produced = 0; produced < total; produced += frame.Length)
        {
            for (var index = 0; index < frame.Length; index++)
            {
                var time = (produced + index) / (double)EncodeRate;
                frame[index] = (short)(Math.Sin(2 * Math.PI * frequencyHz * time) * 8_000);
            }

            writer.WriteSamples(frame, 0, frame.Length);
        }

        writer.Finish();
        return output.ToArray();
    }

    private async Task<string> ResolveLanguageAsync(CancellationToken ct)
    {
        try
        {
            var active = await personality.GetActiveAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(active.Profile.Language))
            {
                return active.Profile.Language;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The hub uses the same default when the profile cannot be read.
        }

        return "es-AR";
    }

    private static async IAsyncEnumerable<AudioChunk> Chunk(
        ReadOnlyMemory<byte> pcm,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sequence = 0;
        for (var offset = 0; offset < pcm.Length; offset += ChunkBytes)
        {
            ct.ThrowIfCancellationRequested();
            var length = Math.Min(ChunkBytes, pcm.Length - offset);
            yield return new AudioChunk(sequence++, pcm.Slice(offset, length));
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}

/// <summary>The note is longer than <see cref="InboundAudioTranscriber.MaxVoiceSeconds"/>.</summary>
internal sealed class InboundAudioTooLongException : InvalidOperationException
{
    public InboundAudioTooLongException()
        : base("The voice note is longer than the limit.")
    {
    }
}

/// <summary>No <c>ISpeechToTextProvider</c> is registered.</summary>
internal sealed class InboundAudioUnavailableException : InvalidOperationException
{
    public InboundAudioUnavailableException()
        : base("Speech-to-text is not configured.")
    {
    }
}
