using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Assistant.Core.Speech;
using Assistant.Providers.Speech.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Speech.ElevenLabs;

/// <summary>
/// ElevenLabs Flash HTTP streaming TTS. Emits PCM s16le 16 kHz without buffering the full file.
/// </summary>
internal sealed class ElevenLabsTextToSpeechProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SpeechOptions> options,
    ILogger<ElevenLabsTextToSpeechProvider> logger) : ITextToSpeechProvider
{
    public const string HttpClientName = "ElevenLabs";

    /// <summary>ElevenLabs documented speed range for voice_settings.speed.</summary>
    public const double MinSpeed = 0.7;

    public const double MaxSpeed = 1.2;

    private readonly TtsOptions _tts = options.Value.Tts;

    public async IAsyncEnumerable<AudioChunk> SynthesizeAsync(
        string text,
        SynthesisOptions synthesisOptions,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var voiceId = ResolveVoiceId(synthesisOptions.VoiceId, _tts.VoiceId);
        var speed = ClampSpeed(synthesisOptions.Speed);
        var client = httpClientFactory.CreateClient(HttpClientName);
        var request = BuildRequest(_tts, voiceId, text, speed);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"ElevenLabs TTS request failed against endpoint '{_tts.Endpoint}' model '{_tts.Model}'.", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                SpeechLog.ElevenLabsRequestFailed(
                    logger, (int)response.StatusCode, _tts.Endpoint);
                throw new InvalidOperationException(
                    $"ElevenLabs TTS returned {(int)response.StatusCode} against endpoint '{_tts.Endpoint}' " +
                    $"model '{_tts.Model}' voice '{voiceId}'.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = new byte[4096];
            var sequence = 0;
            var aligner = new PcmS16leFrameAligner();

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    // Odd trailing byte cannot form an s16le sample — drop it.
                    yield break;
                }

                if (aligner.TryPush(buffer.AsSpan(0, read), out var payload))
                {
                    yield return new AudioChunk(sequence++, payload);
                }
            }
        }
    }

    /// <summary>
    /// Carries at most one leftover byte so every yielded PCM payload has even length (s16le).
    /// </summary>
    internal sealed class PcmS16leFrameAligner
    {
        private byte _carry;
        private bool _hasCarry;

        public bool TryPush(ReadOnlySpan<byte> incoming, out byte[] payload)
        {
            var total = incoming.Length + (_hasCarry ? 1 : 0);
            var evenLength = total & ~1;
            if (evenLength == 0)
            {
                if (incoming.Length == 1)
                {
                    _carry = incoming[0];
                    _hasCarry = true;
                }

                payload = [];
                return false;
            }

            payload = new byte[evenLength];
            var write = 0;
            if (_hasCarry)
            {
                payload[0] = _carry;
                write = 1;
                _hasCarry = false;
            }

            var fromIncoming = evenLength - write;
            incoming[..fromIncoming].CopyTo(payload.AsSpan(write));

            if (fromIncoming < incoming.Length)
            {
                _carry = incoming[fromIncoming];
                _hasCarry = true;
            }

            return true;
        }
    }

    internal static string ResolveVoiceId(string optionsVoiceId, string fallbackVoiceId)
    {
        if (!string.IsNullOrWhiteSpace(optionsVoiceId))
        {
            return optionsVoiceId;
        }

        if (!string.IsNullOrWhiteSpace(fallbackVoiceId))
        {
            return fallbackVoiceId;
        }

        throw new InvalidOperationException(
            "TTS voice_id is required. Set SynthesisOptions.VoiceId or Assistant:Speech:Tts:VoiceId.");
    }

    internal static double ClampSpeed(double speed)
        => Math.Clamp(speed, MinSpeed, MaxSpeed);

    internal static HttpRequestMessage BuildRequest(
        TtsOptions tts, string voiceId, string text, double speed)
    {
        var uri = new Uri(
            new Uri(EnsureTrailingSlash(tts.Endpoint)),
            $"v1/text-to-speech/{Uri.EscapeDataString(voiceId)}/stream" +
            $"?output_format=pcm_16000&optimize_streaming_latency={tts.OptimizeStreamingLatency}");

        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.TryAddWithoutValidation("xi-api-key", tts.ApiKey);
        request.Content = JsonContent.Create(
            new ElevenLabsTtsRequest(
                text,
                tts.Model,
                new ElevenLabsVoiceSettings(speed)));

        return request;
    }

    private static string EnsureTrailingSlash(string endpoint)
        => endpoint.EndsWith('/') ? endpoint : endpoint + "/";

    private sealed record ElevenLabsTtsRequest(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("model_id")] string ModelId,
        [property: JsonPropertyName("voice_settings")] ElevenLabsVoiceSettings VoiceSettings);

    private sealed record ElevenLabsVoiceSettings(
        [property: JsonPropertyName("speed")] double Speed);
}
