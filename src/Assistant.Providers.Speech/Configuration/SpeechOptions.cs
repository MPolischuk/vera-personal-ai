using System.ComponentModel.DataAnnotations;

namespace Assistant.Providers.Speech.Configuration;

public sealed class SpeechOptions
{
    public const string SectionName = "Assistant:Speech";

    public SttOptions Stt { get; set; } = new();

    public TtsOptions Tts { get; set; } = new();
}

public sealed class SttOptions
{
    /// <summary>
    /// Empty disables STT registration. Supported values: <c>deepgram</c>,
    /// <c>openrouter</c> (ElevenLabs Scribe v2, one final transcript), and
    /// <c>elevenlabs-realtime</c> (Scribe v2 Realtime, manual commit).
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    public string Language { get; set; } = "es";

    public string Model { get; set; } = "nova-3";

    /// <summary>
    /// Nova-3 keyterm prompting. Boosts proper nouns the user actually says
    /// (assistant name, people). Repeated as <c>keyterm=</c> on the listen query.
    /// </summary>
    public string[] Keyterms { get; set; } = [];

    [Url]
    public string Endpoint { get; set; } = "https://api.deepgram.com";

    /// <summary>Never in appsettings. User Secrets in development, environment variable in production.</summary>
    public string ApiKey { get; set; } = string.Empty;

    [Range(0.0, 1_000.0)]
    public decimal PricePerMinuteUsd { get; set; }
}

public sealed class TtsOptions
{
    /// <summary>
    /// Empty disables TTS registration. Supported value: <c>elevenlabs</c>.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Fallback when <c>SynthesisOptions.VoiceId</c> is empty.</summary>
    public string VoiceId { get; set; } = string.Empty;

    [Range(0.1, 4.0)]
    public double Speed { get; set; } = 1.0;

    public string Model { get; set; } = "eleven_flash_v2_5";

    /// <summary>ElevenLabs streaming latency hint (0–4). Higher is lower TTFB.</summary>
    [Range(0, 4)]
    public int OptimizeStreamingLatency { get; set; } = 4;

    [Url]
    public string Endpoint { get; set; } = "https://api.elevenlabs.io";

    /// <summary>Never in appsettings. User Secrets in development, environment variable in production.</summary>
    public string ApiKey { get; set; } = string.Empty;

    [Range(0.0, 1_000.0)]
    public decimal PricePerMillionCharactersUsd { get; set; }
}
