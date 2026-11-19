using Microsoft.Extensions.Options;

namespace Assistant.Providers.Speech.Configuration;

/// <summary>
/// D7: empty Provider disables that side. Named vendor requires ApiKey; unknown name fails.
/// </summary>
internal sealed class SpeechOptionsValidation : IValidateOptions<SpeechOptions>
{
    public const string DeepgramProvider = "deepgram";
    public const string OpenRouterProvider = "openrouter";
    public const string ElevenLabsRealtimeProvider = "elevenlabs-realtime";
    public const string ElevenLabsProvider = "elevenlabs";

    public ValidateOptionsResult Validate(string? name, SpeechOptions options)
    {
        var failures = new List<string>();

        ValidateStt(options.Stt, failures);
        ValidateTts(options.Tts, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateStt(SttOptions stt, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(stt.Provider))
        {
            return;
        }

        if (!string.Equals(stt.Provider, DeepgramProvider, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(stt.Provider, OpenRouterProvider, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(stt.Provider, ElevenLabsRealtimeProvider, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"Assistant:Speech:Stt:Provider '{stt.Provider}' is not supported. " +
                $"Use '{DeepgramProvider}', '{OpenRouterProvider}', '{ElevenLabsRealtimeProvider}', or leave empty to disable STT.");
        }

        if (string.IsNullOrWhiteSpace(stt.ApiKey))
        {
            failures.Add("Assistant:Speech:Stt:ApiKey is required when Stt:Provider is set.");
        }

        if (string.IsNullOrWhiteSpace(stt.Model))
        {
            failures.Add("Assistant:Speech:Stt:Model is required when Stt:Provider is set.");
        }

        if (string.IsNullOrWhiteSpace(stt.Endpoint))
        {
            failures.Add("Assistant:Speech:Stt:Endpoint is required when Stt:Provider is set.");
        }
    }

    private static void ValidateTts(TtsOptions tts, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(tts.Provider))
        {
            return;
        }

        if (!string.Equals(tts.Provider, ElevenLabsProvider, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"Assistant:Speech:Tts:Provider '{tts.Provider}' is not supported. " +
                $"Use '{ElevenLabsProvider}' or leave empty to disable TTS.");
        }

        if (string.IsNullOrWhiteSpace(tts.ApiKey))
        {
            failures.Add("Assistant:Speech:Tts:ApiKey is required when Tts:Provider is set.");
        }

        if (string.IsNullOrWhiteSpace(tts.Model))
        {
            failures.Add("Assistant:Speech:Tts:Model is required when Tts:Provider is set.");
        }

        if (string.IsNullOrWhiteSpace(tts.Endpoint))
        {
            failures.Add("Assistant:Speech:Tts:Endpoint is required when Tts:Provider is set.");
        }
    }
}
