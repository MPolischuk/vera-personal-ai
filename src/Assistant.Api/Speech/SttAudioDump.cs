using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Assistant.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Assistant.Api.Speech;

/// <summary>
/// Diagnostic tap (PT-33 D3): captures the exact PCM handed to the STT provider and
/// writes it as a 16 kHz mono s16le WAV beside the transcript it produced, so the same
/// signal can be replayed against other vendors instead of debating capture vs model.
/// Enabled only when ASSISTANT_STT_DUMP_DIR names a writable directory; otherwise the
/// pump runs untouched. Temporary: remove or gate behind config when PT-33 closes.
/// </summary>
internal sealed class SttAudioDump : IDisposable
{
    private const int SampleRateHz = 16_000;
    private const short BitsPerSample = 16;
    private const short Channels = 1;
    private const string DirectoryVariable = "ASSISTANT_STT_DUMP_DIR";

    private readonly string _directory;
    private readonly string _baseName;
    private readonly MemoryStream _pcm = new();

    private SttAudioDump(string directory, string baseName)
    {
        _directory = directory;
        _baseName = baseName;
    }

    public static SttAudioDump? CreateIfEnabled(Guid conversationId, DateTimeOffset now)
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return new SttAudioDump(directory, $"{stamp}-{conversationId:N}");
    }

    /// <summary>Passes chunks through untouched while accumulating a copy.</summary>
    public async IAsyncEnumerable<AudioChunk> TeeAsync(
        IAsyncEnumerable<AudioChunk> source,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in source.WithCancellation(ct).ConfigureAwait(false))
        {
            _pcm.Write(chunk.Data.Span);
            yield return chunk;
        }
    }

    public async Task SaveAsync(string transcript, ILogger log)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var wavPath = Path.Combine(_directory, $"{_baseName}.wav");

            var data = _pcm.GetBuffer().AsMemory(0, (int)_pcm.Length);
            await using (var file = File.Create(wavPath))
            {
                await file.WriteAsync(BuildWavHeader(data.Length), CancellationToken.None)
                    .ConfigureAwait(false);
                await file.WriteAsync(data, CancellationToken.None).ConfigureAwait(false);
            }

            await File.WriteAllTextAsync(
                    Path.Combine(_directory, $"{_baseName}.txt"),
                    transcript,
                    Encoding.UTF8)
                .ConfigureAwait(false);

            var seconds = data.Length / (double)(SampleRateHz * (BitsPerSample / 8) * Channels);
            ApiLog.SttAudioDumped(log, wavPath, seconds, transcript);
        }
        catch (Exception e)
        {
            ApiLog.SttAudioDumpFailed(log, e, _directory);
        }
    }

    public void Dispose() => _pcm.Dispose();

    private static byte[] BuildWavHeader(int dataLength)
    {
        const int byteRate = SampleRateHz * Channels * (BitsPerSample / 8);
        const short blockAlign = Channels * (BitsPerSample / 8);

        var header = new MemoryStream(44);
        using var w = new BinaryWriter(header, Encoding.ASCII, leaveOpen: true);
        w.Write("RIFF"u8);
        w.Write(36 + dataLength);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                 // PCM chunk size
        w.Write((short)1);           // PCM format
        w.Write(Channels);
        w.Write(SampleRateHz);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(BitsPerSample);
        w.Write("data"u8);
        w.Write(dataLength);
        w.Flush();
        return header.ToArray();
    }
}
