using System.Buffers.Binary;

namespace SttBakeoff;

/// <summary>
/// Client energy end-of-turn: 800 ms below the same RMS the gold loader treats as silence.
/// The returned time can fall past the end of the clip when the trailing silence is shorter.
/// </summary>
internal static class EnergyEndpoint
{
    public const double SilenceSeconds = 0.8;

    public const double SilenceRms = 40;

    public static double EndpointSeconds(ReadOnlySpan<byte> pcm)
    {
        const int frameSamples = 320;
        const double frameSeconds = 0.020;
        var frameBytes = frameSamples * 2;
        var silenceFramesNeeded = (int)Math.Round(SilenceSeconds / frameSeconds);
        var speechSeen = false;
        var silenceRun = 0;
        var frames = pcm.Length / frameBytes;
        for (var i = 0; i < frames; i++)
        {
            var rms = FrameRms(pcm.Slice(i * frameBytes, frameBytes));
            if (rms < SilenceRms)
            {
                if (!speechSeen)
                {
                    continue;
                }

                silenceRun++;
                if (silenceRun >= silenceFramesNeeded)
                {
                    return (i + 1) * frameSeconds;
                }
            }
            else
            {
                speechSeen = true;
                silenceRun = 0;
            }
        }

        if (!speechSeen)
        {
            return 0;
        }

        var trailingSilence = silenceRun * frameSeconds;
        return (frames * frameSeconds) + Math.Max(0, SilenceSeconds - trailingSilence);
    }

    private static double FrameRms(ReadOnlySpan<byte> pcm)
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
}
