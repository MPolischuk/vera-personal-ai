using System.Diagnostics.Metrics;

namespace Assistant.Providers.Speech;

internal static class SpeechTelemetry
{
    public const string MeterName = "Assistant.Providers.Speech";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("assistant.speech.duration", "ms");

    public static readonly Histogram<double> TimeToFirstByte =
        Meter.CreateHistogram<double>("assistant.speech.time_to_first_byte", "ms");

    public static readonly Counter<double> Cost =
        Meter.CreateCounter<double>("assistant.speech.cost_usd");

    public static readonly Counter<double> SttSeconds =
        Meter.CreateCounter<double>("assistant.speech.stt_seconds");

    public static readonly Counter<long> TtsCharacters =
        Meter.CreateCounter<long>("assistant.speech.tts_characters");
}
