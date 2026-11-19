using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Assistant.Core.Speech;
using Assistant.Providers.Speech.Configuration;
using Assistant.Providers.Speech.Deepgram;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Speech.OpenRouter;

/// <summary>
/// ElevenLabs Scribe v2 through OpenRouter. The listen call is one batch request:
/// the hub already holds the whole utterance when the user releases Send.
/// One final <see cref="TranscriptionEvent"/> is emitted. Partials are not invented.
/// Keyterms are not sent: the V2-04.1 bake-off rejected that field.
/// </summary>
internal sealed class OpenRouterSpeechToTextProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SpeechOptions> options) : ISpeechToTextProvider
{
    public const string HttpClientName = "OpenRouterStt";

    /// <summary>USD per minute published for <c>elevenlabs/scribe-v2</c> (0.000061 USD/s).</summary>
    public const decimal PublishedPricePerMinuteUsd = 0.00366m;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private readonly SttOptions _stt = options.Value.Stt;

    public async IAsyncEnumerable<TranscriptionEvent> TranscribeAsync(
        IAsyncEnumerable<AudioChunk> audio,
        TranscriptionOptions transcriptionOptions,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var pcm = new MemoryStream();
        await foreach (var chunk in audio.WithCancellation(ct).ConfigureAwait(false))
        {
            if (chunk.Data.IsEmpty)
            {
                continue;
            }

            pcm.Write(chunk.Data.Span);
        }

        if (pcm.Length == 0)
        {
            yield break;
        }

        var language = string.IsNullOrWhiteSpace(transcriptionOptions.Language)
            ? _stt.Language
            : transcriptionOptions.Language;
        var text = await TranscribeWavAsync(
            DeepgramSpeechToTextProvider.WrapWav(pcm.ToArray()),
            language,
            ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(text))
        {
            yield return new TranscriptionEvent(text.Trim(), IsFinal: true, Confidence: null);
        }
    }

    internal static string ToIso639(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "es";
        }

        var trimmed = language.Trim();
        var dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        var primary = dash > 0 ? trimmed[..dash] : trimmed;
        return primary.ToLowerInvariant();
    }

    internal static string BuildRequestJson(string model, string language, ReadOnlySpan<byte> wav)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["language"] = ToIso639(language),
            ["input_audio"] = new Dictionary<string, string>
            {
                ["data"] = Convert.ToBase64String(wav),
                ["format"] = "wav",
            },
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private async Task<string?> TranscribeWavAsync(byte[] wav, string language, CancellationToken ct)
    {
        var uri = BuildUri(_stt.Endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _stt.ApiKey);
        request.Content = new StringContent(
            BuildRequestJson(_stt.Model, language, wav),
            Encoding.UTF8,
            "application/json");

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenRouter transcription returned {(int)response.StatusCode} against '{uri.GetLeftPart(UriPartial.Path)}'.");
        }

        return OpenRouterTranscriptMapper.TryMap(body, out var text) ? text : null;
    }

    private static Uri BuildUri(string endpoint)
    {
        var root = endpoint.TrimEnd('/');
        return new Uri(root + "/audio/transcriptions");
    }
}
