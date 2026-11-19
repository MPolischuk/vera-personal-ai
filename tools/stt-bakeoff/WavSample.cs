using System.Buffers.Binary;

namespace SttBakeoff;

internal sealed record WavClip(string Id, string Phrase, byte[] WavBytes, byte[] Pcm, double DurationSeconds, double Rms);

internal static class WavSample
{
    public static string PrepareSampleDirectory(string sampleDir)
    {
        var root = Path.GetFullPath(sampleDir);
        EnsureOutsideGitRepo(root);
        Directory.CreateDirectory(root);
        WriteExpectedText(root);
        return root;
    }

    public static IReadOnlyList<WavClip> Load(string sampleDir)
    {
        var root = Path.GetFullPath(sampleDir);
        EnsureOutsideGitRepo(root);
        var clips = new List<WavClip>(GoldPhrases.Lines.Length);
        for (var i = 0; i < GoldPhrases.Lines.Length; i++)
        {
            var id = $"{i + 1:00}";
            var path = Path.Combine(root, id + ".wav");
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Missing {path}. Record the gold sample before measuring.");
            }

            var wav = File.ReadAllBytes(path);
            var pcm = ExtractPcm(wav, path);
            var duration = pcm.Length / (16000d * 2d);
            var rms = RootMeanSquare(pcm);
            var words = Scoring.Tokenize(GoldPhrases.Lines[i]).Length;
            if (rms < 40)
            {
                throw new InvalidOperationException($"{path} looks silent (rms {rms:0}). Record it again.");
            }

            if (words > 0 && duration / words > 1.1)
            {
                throw new InvalidOperationException(
                    $"{path} is {duration:0.00}s for {words} words. That pace is syllabified. Record it again at a conversational pace.");
            }

            clips.Add(new WavClip(id, GoldPhrases.Lines[i], wav, pcm, duration, rms));
        }

        WriteExpectedText(root);
        return clips;
    }

    public static void WriteExpectedText(string sampleDir)
    {
        var path = Path.Combine(sampleDir, "expected.txt");
        var text = GoldPhrases.ExpectedFileText();
        if (File.Exists(path))
        {
            var current = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!string.Equals(current, text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} does not match the eight reference phrases.");
            }

            return;
        }

        File.WriteAllText(path, text);
    }

    public static byte[] ToGoldWav(byte[] wav, string path)
    {
        var parsed = Parse(wav, path);
        if (parsed.AudioFormat != 1 || parsed.Bits != 16)
        {
            throw new InvalidOperationException($"{path} must be 16-bit PCM. Found format {parsed.AudioFormat}, {parsed.Bits} bit.");
        }

        var mono = parsed.Channels == 1 ? parsed.Pcm : MixToMono(parsed.Pcm, parsed.Channels);
        var samples = BytesToSamples(mono);
        if (parsed.SampleRate != 16000)
        {
            samples = Resample(samples, parsed.SampleRate, 16000);
        }

        return WrapPcm(SamplesToBytes(samples));
    }

    public static byte[] ExtractPcm(byte[] wav, string path)
    {
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
        {
            throw new InvalidOperationException($"{path} is not a WAV file.");
        }

        var offset = 12;
        short audioFormat = 0;
        short channels = 0;
        var sampleRate = 0;
        short bits = 0;
        byte[]? data = null;
        while (offset + 8 <= wav.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(wav, offset, 4);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(offset + 4, 4));
            if (chunkSize < 0 || offset + 8 + chunkSize > wav.Length)
            {
                throw new InvalidOperationException($"{path} has a truncated WAV chunk.");
            }

            var chunk = wav.AsSpan(offset + 8, chunkSize);
            if (chunkId == "fmt ")
            {
                if (chunk.Length < 16)
                {
                    throw new InvalidOperationException($"{path} has a short fmt chunk.");
                }

                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadInt16LittleEndian(chunk[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                bits = BinaryPrimitives.ReadInt16LittleEndian(chunk[14..]);
            }
            else if (chunkId == "data")
            {
                data = chunk.ToArray();
            }

            offset += 8 + chunkSize + (chunkSize % 2);
        }

        if (audioFormat != 1 || channels != 1 || sampleRate != 16000 || bits != 16 || data is null || data.Length == 0)
        {
            throw new InvalidOperationException(
                $"{path} must be mono 16 kHz s16le PCM. Found format {audioFormat}, {channels} ch, {sampleRate} Hz, {bits} bit.");
        }

        return data;
    }

    public static byte[] WrapPcm(ReadOnlySpan<byte> pcm)
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bits = 16;
        var header = new byte[44 + pcm.Length];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 36 + pcm.Length);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22), channels);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), sampleRate * channels * (bits / 8));
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32), channels * (bits / 8));
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34), bits);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), pcm.Length);
        pcm.CopyTo(header.AsSpan(44));
        return header;
    }

    public static byte[] FromMonoSamples(short[] samples, int sampleRate)
    {
        if (sampleRate <= 0 || samples.Length == 0)
        {
            throw new InvalidOperationException("The capture produced no samples.");
        }

        if (sampleRate != 16000)
        {
            samples = Resample(samples, sampleRate, 16000);
        }

        return WrapPcm(SamplesToBytes(samples));
    }

    public static int Peak(ReadOnlySpan<byte> pcm)
    {
        var peak = 0;
        var count = pcm.Length / 2;
        for (var i = 0; i < count; i++)
        {
            var sample = Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2)));
            if (sample > peak)
            {
                peak = sample;
            }
        }

        return peak;
    }

    public static double RootMeanSquare(ReadOnlySpan<byte> pcm)
    {
        long sum = 0;
        var count = pcm.Length / 2;
        for (var i = 0; i < count; i++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2));
            sum += (long)sample * sample;
        }

        return count == 0 ? 0 : Math.Sqrt(sum / (double)count);
    }

    private readonly record struct ParsedWav(short AudioFormat, short Channels, int SampleRate, short Bits, byte[] Pcm);

    private static ParsedWav Parse(byte[] wav, string path)
    {
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
        {
            throw new InvalidOperationException($"{path} is not a WAV file.");
        }

        var offset = 12;
        short audioFormat = 0;
        short channels = 0;
        var sampleRate = 0;
        short bits = 0;
        byte[]? data = null;
        while (offset + 8 <= wav.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(wav, offset, 4);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(offset + 4, 4));
            if (chunkSize < 0 || offset + 8 + chunkSize > wav.Length)
            {
                throw new InvalidOperationException($"{path} has a truncated WAV chunk.");
            }

            var chunk = wav.AsSpan(offset + 8, chunkSize);
            if (chunkId == "fmt ")
            {
                if (chunk.Length < 16)
                {
                    throw new InvalidOperationException($"{path} has a short fmt chunk.");
                }

                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadInt16LittleEndian(chunk[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                bits = BinaryPrimitives.ReadInt16LittleEndian(chunk[14..]);
            }
            else if (chunkId == "data")
            {
                data = chunk.ToArray();
            }

            offset += 8 + chunkSize + (chunkSize % 2);
        }

        if (channels <= 0 || sampleRate <= 0 || data is null || data.Length == 0)
        {
            throw new InvalidOperationException($"{path} has no PCM data.");
        }

        return new ParsedWav(audioFormat, channels, sampleRate, bits, data);
    }

    private static byte[] MixToMono(byte[] interleaved, int channels)
    {
        var frameBytes = channels * 2;
        var frames = interleaved.Length / frameBytes;
        var mono = new byte[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += BinaryPrimitives.ReadInt16LittleEndian(interleaved.AsSpan((frame * channels + channel) * 2, 2));
            }

            BinaryPrimitives.WriteInt16LittleEndian(mono.AsSpan(frame * 2, 2), (short)(sum / channels));
        }

        return mono;
    }

    private static short[] BytesToSamples(byte[] pcm)
    {
        var samples = new short[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2, 2));
        }

        return samples;
    }

    private static byte[] SamplesToBytes(short[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), samples[i]);
        }

        return pcm;
    }

    private static short[] Resample(short[] input, int fromRate, int toRate)
    {
        if (input.Length == 0 || fromRate == toRate)
        {
            return input;
        }

        var length = (int)((long)input.Length * toRate / fromRate);
        var output = new short[length];
        for (var i = 0; i < length; i++)
        {
            var position = (double)i * fromRate / toRate;
            var index = (int)position;
            var fraction = position - index;
            var left = input[Math.Min(index, input.Length - 1)];
            var right = input[Math.Min(index + 1, input.Length - 1)];
            output[i] = (short)Math.Clamp(left + ((right - left) * fraction), short.MinValue, short.MaxValue);
        }

        return output;
    }

    private static void EnsureOutsideGitRepo(string sampleDir)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    var root = dir.FullName.TrimEnd(Path.DirectorySeparatorChar);
                    var full = sampleDir.TrimEnd(Path.DirectorySeparatorChar);
                    if (full.Equals(root, StringComparison.OrdinalIgnoreCase)
                        || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "The gold sample must live outside the git repo. Refusing to read WAV files from " + full);
                    }

                    break;
                }

                dir = dir.Parent;
            }
        }
    }
}
