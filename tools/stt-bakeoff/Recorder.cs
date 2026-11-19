using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SttBakeoff;

internal static class Recorder
{
    public static void Run(string sampleDir, string? deviceFilter)
    {
        var root = WavSample.PrepareSampleDirectory(sampleDir);
        var deviceName = CaptureDeviceName(deviceFilter);
        Console.WriteLine("Gold sample directory: " + root);
        Console.WriteLine("Recording from: " + deviceName);
        Console.WriteLine("Speak at a conversational Rioplatense pace. Do not syllable the words.");
        Console.WriteLine("Enter starts a take. Enter again stops it. The meter should move while you speak.");

        for (var i = 0; i < GoldPhrases.Lines.Length; i++)
        {
            var id = $"{i + 1:00}";
            var phrase = GoldPhrases.Lines[i];
            var path = Path.Combine(root, id + ".wav");
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine(id + "  " + phrase);
                Console.Write("Press Enter to record... ");
                Console.ReadLine();
                var take = RecordTake(deviceFilter);
                var gold = WavSample.FromMonoSamples(take.Samples, take.SampleRate);
                File.WriteAllBytes(path, gold);
                var pcm = WavSample.ExtractPcm(gold, path);
                var duration = pcm.Length / (16000d * 2d);
                var rms = WavSample.RootMeanSquare(pcm);
                var peak = WavSample.Peak(pcm);
                var words = Scoring.Tokenize(phrase).Length;
                Console.WriteLine();
                Console.WriteLine($"Wrote {path} ({duration:0.00}s, rms {rms:0}, peak {peak}) from {take.DeviceName}");

                if (peak < 400)
                {
                    Console.WriteLine($"No input signal from '{take.DeviceName}'.");
                    Console.WriteLine("Unmute that microphone in Windows Sound settings, or pass --device with another name from the list above.");
                    DeleteAndRetry(path);
                    continue;
                }

                if (duration < 0.4)
                {
                    Console.WriteLine("The take is shorter than 0.4s.");
                    DeleteAndRetry(path);
                    continue;
                }

                if (words > 0 && duration / words > 1.1)
                {
                    Console.WriteLine("The take is slow enough to be syllabified.");
                    DeleteAndRetry(path);
                    continue;
                }

                if (rms < 500)
                {
                    Console.WriteLine($"The signal from '{take.DeviceName}' is quiet (rms {rms:0}).");
                    Console.WriteLine("Raise that microphone's volume in Windows Sound settings if the words were hard to hear.");
                    Console.Write("Press k then Enter to keep this take, or Enter to record it again: ");
                    var answer = Console.ReadLine();
                    if (!string.Equals(answer?.Trim(), "k", StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteAndRetry(path);
                        continue;
                    }
                }

                break;
            }
        }

        Console.WriteLine();
        Console.WriteLine("Eight takes are in " + root);
    }

    public static void Probe()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var chosen = OpenDevice(enumerator, null, out var devices);
        Console.WriteLine("Default input: " + chosen.FriendlyName);
        foreach (var device in devices)
        {
            var mark = device.ID == chosen.ID ? "*" : " ";
            Console.WriteLine($"{mark} {device.FriendlyName}");
            if (!ReferenceEquals(device, chosen))
            {
                device.Dispose();
            }
        }

        using var capture = new WasapiCapture(chosen);
        capture.StartRecording();
        try
        {
            var format = capture.WaveFormat;
            Console.WriteLine($"Mix format: {format.SampleRate} Hz, {format.BitsPerSample}-bit {format.Encoding}, {format.Channels} ch");
        }
        finally
        {
            capture.StopRecording();
        }
    }

    private static string CaptureDeviceName(string? deviceFilter)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var chosen = OpenDevice(enumerator, deviceFilter, out var devices);
        var name = chosen.FriendlyName;
        Console.WriteLine("Active inputs:");
        foreach (var device in devices)
        {
            var mark = device.ID == chosen.ID ? "*" : " ";
            Console.WriteLine($"{mark} {device.FriendlyName}");
            device.Dispose();
        }

        return name;
    }

    private static Take RecordTake(string? deviceFilter)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var chosen = OpenDevice(enumerator, deviceFilter, out var devices);
        foreach (var device in devices)
        {
            if (!ReferenceEquals(device, chosen))
            {
                device.Dispose();
            }
        }

        var gate = new object();
        var buffer = new MemoryStream();
        var recentPeak = 0;
        var lastPrint = 0L;
        using var capture = new WasapiCapture(chosen);
        capture.DataAvailable += (_, args) =>
        {
            var peak = BufferPeak(args.Buffer, args.BytesRecorded, capture.WaveFormat);
            lock (gate)
            {
                buffer.Write(args.Buffer, 0, args.BytesRecorded);
                recentPeak = peak;
            }

            var now = Environment.TickCount64;
            if (now - lastPrint < 100)
            {
                return;
            }

            lastPrint = now;
            var bars = Math.Clamp(peak / 1000, 0, 32);
            Console.Write("\r  [" + new string('#', bars).PadRight(32) + "]");
        };

        capture.StartRecording();
        Console.WriteLine("Recording from " + chosen.FriendlyName + ". Press Enter to stop.");
        Console.ReadLine();
        capture.StopRecording();
        Thread.Sleep(200);
        Console.WriteLine();

        byte[] raw;
        lock (gate)
        {
            raw = buffer.ToArray();
        }

        var format = capture.WaveFormat;
        return new Take(chosen.FriendlyName, format.SampleRate, ToMono(raw, format));
    }

    private static MMDevice OpenDevice(MMDeviceEnumerator enumerator, string? deviceFilter, out List<MMDevice> devices)
    {
        devices = [.. enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)];
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No capture device is active. Enable a microphone in Windows sound settings.");
        }

        if (!string.IsNullOrWhiteSpace(deviceFilter))
        {
            var matches = devices
                .Where(device => device.FriendlyName.Contains(deviceFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
            {
                var names = string.Join(", ", devices.Select(device => device.FriendlyName));
                foreach (var device in devices)
                {
                    device.Dispose();
                }

                throw new InvalidOperationException(
                    matches.Count == 0
                        ? "No input matches '" + deviceFilter + "'. Active inputs: " + names
                        : "More than one input matches '" + deviceFilter + "'. Active inputs: " + names);
            }

            return matches[0];
        }

        try
        {
            using var fallback = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var chosen = devices.FirstOrDefault(device => device.ID == fallback.ID);
            if (chosen is not null)
            {
                return chosen;
            }
        }
        catch (COMException)
        {
            // No default console endpoint. Fall through to the first active input.
        }

        return devices[0];
    }

    private static short[] ToMono(byte[] raw, WaveFormat format)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        var channels = Math.Max(1, format.Channels);
        if (IsFloat(format))
        {
            var frames = raw.Length / (4 * channels);
            var mono = new short[frames];
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += BitConverter.ToSingle(raw, (frame * channels + channel) * 4);
                }

                mono[frame] = (short)Math.Clamp(sum / channels * 32767f, short.MinValue, short.MaxValue);
            }

            return mono;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var frames = raw.Length / (2 * channels);
            var mono = new short[frames];
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += BitConverter.ToInt16(raw, (frame * channels + channel) * 2);
                }

                mono[frame] = (short)(sum / channels);
            }

            return mono;
        }

        throw new InvalidOperationException(
            $"Unsupported capture format: {format.SampleRate} Hz, {format.BitsPerSample}-bit {format.Encoding}, {format.Channels} ch.");
    }

    private static int BufferPeak(byte[] buffer, int length, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        var peak = 0;
        if (IsFloat(format))
        {
            var frames = length / (4 * channels);
            for (var frame = 0; frame < frames; frame++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = (int)Math.Abs(BitConverter.ToSingle(buffer, (frame * channels + channel) * 4) * 32767f);
                    if (sample > peak)
                    {
                        peak = sample;
                    }
                }
            }

            return peak;
        }

        if (format.BitsPerSample == 16)
        {
            var count = length / 2;
            for (var i = 0; i < count; i++)
            {
                var sample = Math.Abs(BitConverter.ToInt16(buffer, i * 2));
                if (sample > peak)
                {
                    peak = sample;
                }
            }
        }

        return peak;
    }

    private static bool IsFloat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return true;
        }

        if (format is WaveFormatExtensible extensible)
        {
            return extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        }

        return false;
    }

    private static void DeleteAndRetry(string path)
    {
        Console.WriteLine("Record it again.");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record Take(string DeviceName, int SampleRate, short[] Samples);
}
