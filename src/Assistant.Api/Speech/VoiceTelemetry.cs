using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Assistant.Api.Speech;

internal static class VoiceTelemetry
{
    public const string ActivitySourceName = "Assistant.Api";
    public const string MeterName = "Assistant.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    /// <summary>F1: user.audio_end → final transcription.</summary>
    public static readonly Histogram<double> SttFinalMs =
        Meter.CreateHistogram<double>("speech.stt_final_ms", unit: "ms");

    /// <summary>F2: final transcription → first TurnToken.</summary>
    public static readonly Histogram<double> SttToFirstTokenMs =
        Meter.CreateHistogram<double>("speech.stt_to_first_token_ms", unit: "ms");

    /// <summary>F3: sentence ready → first byte of that sentence sent.</summary>
    public static readonly Histogram<double> TtsFirstByteMs =
        Meter.CreateHistogram<double>("speech.tts_first_byte_ms", unit: "ms");

    /// <summary>F4: audio_end (STT) or TurnAccepted → first ReceiveAudio of the turn.</summary>
    public static readonly Histogram<double> TurnToFirstAudioMs =
        Meter.CreateHistogram<double>("speech.turn_to_first_audio_ms", unit: "ms");

    /// <summary>
    /// Diagnostic: first TurnToken → first sentence Enqueue (segmenter wait; not an ADR Fn).
    /// </summary>
    public static readonly Histogram<double> FirstSentenceWaitMs =
        Meter.CreateHistogram<double>("speech.first_sentence_wait_ms", unit: "ms");
}
