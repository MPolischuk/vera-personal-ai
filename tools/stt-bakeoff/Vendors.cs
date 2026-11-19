using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SttBakeoff;

internal sealed record PassResult(
    string Text,
    int FinalMilliseconds,
    int? FirstPartialMilliseconds,
    string? Note,
    bool? ServerEndpointBeforeEnergy = null);

internal sealed class ContextRejectedException : Exception
{
    public ContextRejectedException(string message)
        : base(message)
    {
    }
}

internal sealed class VendorException : Exception
{
    public VendorException(string message)
        : base(message)
    {
    }
}

internal static class Vendors
{
    private const string DeepgramModel = "nova-3";
    private const string RealtimeModel = "voxtral-mini-transcribe-realtime-2602";
    private const string BatchModel = "voxtral-mini-2602";
    private const string ScribeModel = "elevenlabs/scribe-v2";
    private const string OpenAiModel = "gpt-4o-transcribe";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static Task<PassResult> DeepgramAsync(
        WavClip clip,
        string apiKey,
        bool withKeyterms,
        CancellationToken cancellationToken)
    {
        return DeepgramPassAsync(clip, apiKey, withKeyterms ? GoldPhrases.Keyterms : null, cancellationToken);
    }

    public static Task<PassResult> VoxtralRealtimeAsync(
        WavClip clip,
        string apiKey,
        bool withKeyterms,
        CancellationToken cancellationToken)
    {
        return VoxtralRealtimePassAsync(clip, apiKey, withKeyterms ? GoldPhrases.Keyterms : null, cancellationToken);
    }

    public static Task<PassResult> VoxtralBatchAsync(
        WavClip clip,
        string apiKey,
        bool withKeyterms,
        CancellationToken cancellationToken)
    {
        return VoxtralBatchPassAsync(clip, apiKey, withKeyterms ? GoldPhrases.Keyterms : null, cancellationToken);
    }

    public static Task<PassResult> ScribeAsync(
        WavClip clip,
        string apiKey,
        bool withKeyterms,
        CancellationToken cancellationToken)
    {
        return ScribePassAsync(clip, apiKey, withKeyterms ? GoldPhrases.Keyterms : null, cancellationToken);
    }

    public static Task<PassResult> ScribeRealtimeAsync(
        WavClip clip,
        string apiKey,
        bool withKeyterms,
        CancellationToken cancellationToken)
    {
        return ScribeRealtimePassAsync(clip, apiKey, withKeyterms ? GoldPhrases.Keyterms : null, cancellationToken);
    }

    public static Task<PassResult> FluxAsync(
        WavClip clip,
        string apiKey,
        bool withKeyterms,
        CancellationToken cancellationToken)
    {
        return FluxPassAsync(clip, apiKey, withKeyterms ? GoldPhrases.Keyterms : null, cancellationToken);
    }

    public static Task<PassResult> OpenAiAsync(
        WavClip clip,
        string apiKey,
        bool withKeyterms,
        CancellationToken cancellationToken)
    {
        return OpenAiPassAsync(clip, apiKey, withKeyterms, cancellationToken);
    }

    private static async Task<PassResult> DeepgramPassAsync(
        WavClip clip,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        var streamed = await StreamDeepgramAsync(clip.Pcm, apiKey, keyterms, cancellationToken).ConfigureAwait(false);
        string? prerecorded = null;
        string? note = null;
        try
        {
            prerecorded = await PostDeepgramAsync(clip.WavBytes, apiKey, keyterms, cancellationToken).ConfigureAwait(false);
        }
        catch (VendorException ex)
        {
            note = "prerecorded listen failed; final text is the streaming transcript. " + ex.Message;
        }

        var text = string.IsNullOrWhiteSpace(prerecorded) ? streamed.Text : prerecorded.Trim();
        var finalMs = MillisecondsSince(streamed.AudioEndedAt);
        return new PassResult(text, finalMs, streamed.FirstPartialMilliseconds, note);
    }

    private static async Task<StreamCapture> StreamDeepgramAsync(
        byte[] pcm,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Token " + apiKey);
        await socket.ConnectAsync(DeepgramUri(streaming: true, keyterms), cancellationToken).ConfigureAwait(false);
        var inbox = Channel.CreateUnbounded<Incoming>();
        var pump = PumpTextAsync(socket, inbox.Writer, cancellationToken);
        try
        {
            for (var offset = 0; offset < pcm.Length; offset += 3200)
            {
                var count = Math.Min(3200, pcm.Length - offset);
                await socket.SendAsync(pcm.AsMemory(offset, count), WebSocketMessageType.Binary, true, cancellationToken)
                    .ConfigureAwait(false);
            }

            var audioEndedAt = Stopwatch.GetTimestamp();
            await SendTextAsync(socket, """{"type":"Finalize"}""", cancellationToken).ConfigureAwait(false);

            var finals = new List<string>();
            string? interim = null;
            long? firstPartialAt = null;
            var sawFinalize = false;
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && !sawFinalize)
            {
                var incoming = await ReadAsync(inbox.Reader, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                if (incoming is null)
                {
                    break;
                }

                ThrowIfDeepgramError(incoming.Json, apiKey);
                if (IsDeepgramFinalize(incoming.Json))
                {
                    sawFinalize = true;
                }

                if (!TryDeepgramResults(incoming.Json, out var text, out var isFinal))
                {
                    continue;
                }

                firstPartialAt ??= incoming.At;
                if (isFinal)
                {
                    finals.Add(text);
                }
                else
                {
                    interim = text;
                }
            }

            if (socket.State == WebSocketState.Open)
            {
                await SendTextAsync(socket, """{"type":"CloseStream"}""", cancellationToken).ConfigureAwait(false);
                await CloseSocketAsync(socket).ConfigureAwait(false);
            }

            var transcript = finals.Count > 0 ? string.Join(' ', finals) : interim ?? string.Empty;
            int? partialMs = firstPartialAt is null
                ? null
                : Math.Max(0, MillisecondsBetween(audioEndedAt, firstPartialAt.Value));
            return new StreamCapture(transcript, audioEndedAt, partialMs);
        }
        finally
        {
            await CloseSocketAsync(socket).ConfigureAwait(false);
            inbox.Writer.TryComplete();
            await pump.ConfigureAwait(false);
        }
    }

    private static async Task<string?> PostDeepgramAsync(
        byte[] wav,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DeepgramUri(streaming: false, keyterms));
        request.Headers.TryAddWithoutValidation("Authorization", "Token " + apiKey);
        request.Content = new ByteArrayContent(wav);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new VendorException(
                "Deepgram prerecorded HTTP " + (int)response.StatusCode + ": " + SecretRedaction.Snippet(body, 180, apiKey));
        }

        return ReadDeepgramPrerecorded(body);
    }

    private static async Task<PassResult> VoxtralRealtimePassAsync(
        WavClip clip,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
        var uri = new Uri(
            "wss://api.mistral.ai/v1/audio/transcriptions/realtime?model=" + Uri.EscapeDataString(RealtimeModel));
        await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        var inbox = Channel.CreateUnbounded<Incoming>();
        var pump = PumpTextAsync(socket, inbox.Writer, cancellationToken);
        try
        {
            await WaitForTypeAsync(inbox.Reader, "session.created", apiKey, keyterms is not null, TimeSpan.FromSeconds(15), cancellationToken)
                .ConfigureAwait(false);
            await SendTextAsync(socket, SessionUpdateJson(keyterms), cancellationToken).ConfigureAwait(false);
            await WaitForTypeAsync(inbox.Reader, "session.updated", apiKey, keyterms is not null, TimeSpan.FromSeconds(15), cancellationToken)
                .ConfigureAwait(false);

            const int chunkBytes = 15360;
            for (var offset = 0; offset < clip.Pcm.Length; offset += chunkBytes)
            {
                var count = Math.Min(chunkBytes, clip.Pcm.Length - offset);
                var audio = Convert.ToBase64String(clip.Pcm, offset, count);
                await SendTextAsync(socket, "{\"type\":\"input_audio.append\",\"audio\":\"" + audio + "\"}", cancellationToken)
                    .ConfigureAwait(false);
            }

            var audioEndedAt = Stopwatch.GetTimestamp();
            await SendTextAsync(socket, """{"type":"input_audio.end"}""", cancellationToken).ConfigureAwait(false);

            string? done = null;
            long? firstDeltaAt = null;
            var assembled = new StringBuilder();
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline && done is null)
            {
                var incoming = await ReadAsync(inbox.Reader, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
                if (incoming is null)
                {
                    break;
                }

                var type = ReadEventType(incoming.Json);
                if (type == "error")
                {
                    ThrowMistralError(incoming.Json, apiKey, keyterms is not null);
                }

                if (type == "transcription.text.delta")
                {
                    firstDeltaAt ??= incoming.At;
                    assembled.Append(ReadStringProperty(incoming.Json, "text"));
                }
                else if (type == "transcription.done")
                {
                    done = ReadStringProperty(incoming.Json, "text");
                }
            }

            await CloseSocketAsync(socket).ConfigureAwait(false);
            var text = string.IsNullOrWhiteSpace(done) ? assembled.ToString() : done;
            int? partialMs = firstDeltaAt is null
                ? null
                : Math.Max(0, MillisecondsBetween(audioEndedAt, firstDeltaAt.Value));
            return new PassResult(text.Trim(), MillisecondsSince(audioEndedAt), partialMs, null);
        }
        finally
        {
            await CloseSocketAsync(socket).ConfigureAwait(false);
            inbox.Writer.TryComplete();
            await pump.ConfigureAwait(false);
        }
    }

    private static async Task<PassResult> VoxtralBatchPassAsync(
        WavClip clip,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(BatchModel), "model");
        form.Add(new StringContent("es"), "language");
        var audio = new ByteArrayContent(clip.WavBytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", clip.Id + ".wav");
        if (keyterms is not null)
        {
            foreach (var term in keyterms)
            {
                form.Add(new StringContent(term), "context_bias");
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/audio/transcriptions");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        request.Content = form;
        var started = Stopwatch.GetTimestamp();
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            ThrowHttp("Voxtral batch", (int)response.StatusCode, body, apiKey, keyterms is not null);
        }

        return new PassResult(ReadTranscript(body), MillisecondsSince(started), null, null);
    }

    private static async Task<PassResult> ScribePassAsync(
        WavClip clip,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = ScribeModel,
            ["language"] = "es",
            ["input_audio"] = new Dictionary<string, string>
            {
                ["data"] = Convert.ToBase64String(clip.WavBytes),
                ["format"] = "wav",
            },
        };
        if (keyterms is not null)
        {
            payload["keyterms"] = keyterms;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/audio/transcriptions");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var started = Stopwatch.GetTimestamp();
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            ThrowHttp("Scribe v2", (int)response.StatusCode, body, apiKey, keyterms is not null);
        }

        return new PassResult(ReadTranscript(body), MillisecondsSince(started), null, null);
    }

    private static async Task<PassResult> ScribeRealtimePassAsync(
        WavClip clip,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("xi-api-key", apiKey);
        await socket.ConnectAsync(ScribeRealtimeUri(keyterms), cancellationToken).ConfigureAwait(false);
        var inbox = Channel.CreateUnbounded<Incoming>();
        var pump = PumpTextAsync(socket, inbox.Writer, cancellationToken);
        try
        {
            await WaitForScribeSessionAsync(socket, inbox.Reader, apiKey, keyterms, cancellationToken)
                .ConfigureAwait(false);
            for (var offset = 0; offset < clip.Pcm.Length; offset += 32000)
            {
                var count = Math.Min(32000, clip.Pcm.Length - offset);
                await SendTextAsync(
                    socket,
                    ScribeAudioJson(clip.Pcm.AsSpan(offset, count), commit: false),
                    cancellationToken).ConfigureAwait(false);
            }

            var audioEndedAt = Stopwatch.GetTimestamp();
            await SendTextAsync(socket, ScribeAudioJson(ReadOnlySpan<byte>.Empty, commit: true), cancellationToken)
                .ConfigureAwait(false);

            string? committed = null;
            string? partial = null;
            long? firstPartialAt = null;
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline && committed is null)
            {
                var incoming = await ReadAsync(inbox.Reader, TimeSpan.FromSeconds(4), cancellationToken)
                    .ConfigureAwait(false);
                if (incoming is null)
                {
                    break;
                }

                if (TryScribeRealtimeError(incoming.Json, out var error))
                {
                    var snippet = SecretRedaction.Snippet(error, 180, apiKey);
                    if (keyterms is not null)
                    {
                        throw new ContextRejectedException(snippet);
                    }

                    throw new VendorException("Scribe realtime error: " + snippet);
                }

                if (!TryScribeRealtimeTranscript(incoming.Json, out var text, out var isCommitted))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    firstPartialAt ??= incoming.At;
                    partial = text;
                }

                if (isCommitted)
                {
                    committed = text.Length > 0 ? text : partial;
                }
            }

            await CloseSocketAsync(socket).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(committed) && string.IsNullOrWhiteSpace(partial))
            {
                throw new VendorException("Scribe realtime closed without a transcript. " + ScribeCloseNote(socket, apiKey));
            }

            int? partialMs = firstPartialAt is null
                ? null
                : Math.Max(0, MillisecondsBetween(audioEndedAt, firstPartialAt.Value));
            return new PassResult(
                (committed ?? partial ?? string.Empty).Trim(),
                MillisecondsSince(audioEndedAt),
                partialMs,
                null);
        }
        finally
        {
            await CloseSocketAsync(socket).ConfigureAwait(false);
            inbox.Writer.TryComplete();
            await pump.ConfigureAwait(false);
        }
    }

    private static async Task WaitForScribeSessionAsync(
        ClientWebSocket socket,
        ChannelReader<Incoming> reader,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var incoming = await ReadAsync(reader, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (incoming is null)
            {
                break;
            }

            if (TryScribeRealtimeError(incoming.Json, out var error))
            {
                var snippet = SecretRedaction.Snippet(error, 180, apiKey);
                if (keyterms is not null)
                {
                    throw new ContextRejectedException(snippet);
                }

                throw new VendorException("Scribe realtime error: " + snippet);
            }

            if (IsScribeSessionStarted(incoming.Json))
            {
                return;
            }
        }

        throw new VendorException("Scribe realtime closed before session_started. " + ScribeCloseNote(socket, apiKey));
    }

    private static bool IsScribeSessionStarted(string json)
    {
        if (!TryParse(json, out var root))
        {
            return false;
        }

        using (root)
        {
            return root.RootElement.TryGetProperty("message_type", out var type)
                && string.Equals(type.GetString(), "session_started", StringComparison.Ordinal);
        }
    }

    private static string ScribeCloseNote(ClientWebSocket socket, string apiKey)
    {
        var description = string.IsNullOrWhiteSpace(socket.CloseStatusDescription)
            ? "no description"
            : SecretRedaction.Snippet(socket.CloseStatusDescription, 180, apiKey);
        return "close=" + (socket.CloseStatus?.ToString() ?? "none") + " " + description;
    }

    private static Uri ScribeRealtimeUri(IReadOnlyList<string>? keyterms)
    {
        var query =
            "model_id=scribe_v2_realtime&audio_format=pcm_16000&language_code=es" +
            "&commit_strategy=manual&enable_logging=false";
        if (keyterms is not null)
        {
            foreach (var term in keyterms)
            {
                var trimmed = term.Trim();
                if (trimmed.Length > 0)
                {
                    query += "&keyterms=" + Uri.EscapeDataString(trimmed);
                }
            }
        }

        return new UriBuilder("https://api.elevenlabs.io")
        {
            Scheme = "wss",
            Path = "/v1/speech-to-text/realtime",
            Query = query,
            Port = -1,
        }.Uri;
    }

    private static string ScribeAudioJson(ReadOnlySpan<byte> pcm, bool commit)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["message_type"] = "input_audio_chunk",
            ["audio_base_64"] = pcm.IsEmpty ? string.Empty : Convert.ToBase64String(pcm),
            ["commit"] = commit,
            ["sample_rate"] = 16000,
        });
    }

    private static bool TryScribeRealtimeTranscript(string json, out string text, out bool committed)
    {
        text = string.Empty;
        committed = false;
        if (!TryParse(json, out var root))
        {
            return false;
        }

        using (root)
        {
            var element = root.RootElement;
            var type = element.TryGetProperty("message_type", out var typeProperty)
                ? typeProperty.GetString()
                : null;
            if (type is not ("partial_transcript" or "committed_transcript" or "committed_transcript_with_timestamps"))
            {
                return false;
            }

            text = element.TryGetProperty("text", out var textProperty)
                ? textProperty.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            committed = type is not "partial_transcript";
            return text.Length > 0 || committed;
        }
    }

    private static bool TryScribeRealtimeError(string json, out string error)
    {
        error = string.Empty;
        if (!TryParse(json, out var root))
        {
            return false;
        }

        using (root)
        {
            var element = root.RootElement;
            var type = element.TryGetProperty("message_type", out var typeProperty)
                ? typeProperty.GetString()
                : null;
            if (type is "partial_transcript" or "committed_transcript" or "committed_transcript_with_timestamps" or "session_started" or "warning")
            {
                return false;
            }

            if (!element.TryGetProperty("error", out var errorProperty))
            {
                return false;
            }

            error = errorProperty.ValueKind == JsonValueKind.String
                ? errorProperty.GetString() ?? type ?? "unknown error"
                : type ?? "unknown error";
            return true;
        }
    }

    private static async Task<PassResult> FluxPassAsync(
        WavClip clip,
        string apiKey,
        IReadOnlyList<string>? keyterms,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Token " + apiKey);
        await socket.ConnectAsync(FluxUri(), cancellationToken).ConfigureAwait(false);
        var inbox = Channel.CreateUnbounded<Incoming>();
        var pump = PumpTextAsync(socket, inbox.Writer, cancellationToken);
        try
        {
            if (keyterms is not null)
            {
                await SendTextAsync(socket, FluxConfigureJson(keyterms), cancellationToken).ConfigureAwait(false);
            }

            for (var offset = 0; offset < clip.Pcm.Length; offset += 3200)
            {
                var count = Math.Min(3200, clip.Pcm.Length - offset);
                await socket.SendAsync(
                    clip.Pcm.AsMemory(offset, count),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken).ConfigureAwait(false);
            }

            var audioEndedAt = Stopwatch.GetTimestamp();
            var silence = new byte[16_000 * 2];
            await socket.SendAsync(silence, WebSocketMessageType.Binary, true, cancellationToken)
                .ConfigureAwait(false);

            string? transcript = null;
            string? latest = null;
            double? endOfTurnSeconds = null;
            var sawEndOfTurn = false;
            long? firstPartialAt = null;
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline && !sawEndOfTurn)
            {
                var incoming = await ReadAsync(inbox.Reader, TimeSpan.FromSeconds(4), cancellationToken)
                    .ConfigureAwait(false);
                if (incoming is null)
                {
                    break;
                }

                if (TryFluxError(incoming.Json, out var error))
                {
                    if (keyterms is not null)
                    {
                        throw new ContextRejectedException(SecretRedaction.Snippet(error, 180, apiKey));
                    }

                    throw new VendorException("Flux listen error: " + SecretRedaction.Snippet(error, 180, apiKey));
                }

                if (!TryFluxTurn(incoming.Json, out var text, out var endOfTurn, out var audioEnd))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    firstPartialAt ??= incoming.At;
                    latest = text;
                }

                if (endOfTurn)
                {
                    transcript = text.Length > 0 ? text : latest;
                    sawEndOfTurn = true;
                    endOfTurnSeconds = audioEnd;
                }
            }

            await CloseSocketAsync(socket).ConfigureAwait(false);
            int? partialMs = firstPartialAt is null
                ? null
                : Math.Max(0, MillisecondsBetween(audioEndedAt, firstPartialAt.Value));
            bool? early = endOfTurnSeconds is double serverEnd
                ? serverEnd + 0.0001 < EnergyEndpoint.EndpointSeconds(clip.Pcm)
                : null;
            var note = !sawEndOfTurn
                ? "sin fin de turno del servidor"
                : endOfTurnSeconds is null ? "fin de turno sin marca de tiempo" : null;
            return new PassResult(
                (transcript ?? latest ?? string.Empty).Trim(),
                MillisecondsSince(audioEndedAt),
                partialMs,
                note,
                early);
        }
        finally
        {
            await CloseSocketAsync(socket).ConfigureAwait(false);
            inbox.Writer.TryComplete();
            await pump.ConfigureAwait(false);
        }
    }

    private static Uri FluxUri()
    {
        return new UriBuilder("https://api.deepgram.com")
        {
            Scheme = "wss",
            Path = "/v2/listen",
            Query = "model=" + Uri.EscapeDataString("flux-general-multi")
                + "&language_hint=es&encoding=linear16&sample_rate=16000",
            Port = -1,
        }.Uri;
    }

    private static string FluxConfigureJson(IReadOnlyList<string> keyterms)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "Configure",
            ["keyterms"] = keyterms,
        });
    }

    private static bool TryFluxTurn(string json, out string text, out bool endOfTurn, out double? audioEnd)
    {
        text = string.Empty;
        endOfTurn = false;
        audioEnd = null;
        if (!TryParse(json, out var root))
        {
            return false;
        }

        using (root)
        {
            var element = root.RootElement;
            if (!element.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "TurnInfo", StringComparison.Ordinal))
            {
                return false;
            }

            text = element.TryGetProperty("transcript", out var transcript)
                ? transcript.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            endOfTurn = element.TryGetProperty("event", out var ev)
                && string.Equals(ev.GetString(), "EndOfTurn", StringComparison.Ordinal);
            if (element.TryGetProperty("audio_window_end", out var windowEnd)
                && windowEnd.TryGetDouble(out var seconds))
            {
                audioEnd = seconds;
            }

            return true;
        }
    }

    private static bool TryFluxError(string json, out string error)
    {
        error = string.Empty;
        if (!TryParse(json, out var root))
        {
            return false;
        }

        using (root)
        {
            var element = root.RootElement;
            if (!element.TryGetProperty("type", out var type))
            {
                return false;
            }

            var name = type.GetString();
            if (!string.Equals(name, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            error = element.TryGetProperty("message", out var message)
                ? message.GetString() ?? "unknown error"
                : element.TryGetProperty("description", out var description)
                    ? description.GetString() ?? "unknown error"
                    : "unknown error";
            return true;
        }
    }

    private static async Task<PassResult> OpenAiPassAsync(
        WavClip clip,
        string apiKey,
        bool withKeyterms,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(OpenAiModel), "model");
        form.Add(new StringContent("es"), "language");
        if (withKeyterms)
        {
            form.Add(new StringContent(string.Join(", ", GoldPhrases.Keyterms)), "prompt");
        }

        var audio = new ByteArrayContent(clip.WavBytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", clip.Id + ".wav");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        request.Content = form;
        var started = Stopwatch.GetTimestamp();
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            ThrowHttp("gpt-4o-transcribe", (int)response.StatusCode, body, apiKey, withKeyterms);
        }

        return new PassResult(ReadTranscript(body), MillisecondsSince(started), null, null);
    }

    private static string SessionUpdateJson(IReadOnlyList<string>? keyterms)
    {
        var session = new Dictionary<string, object?>
        {
            ["audio_format"] = new Dictionary<string, object?>
            {
                ["encoding"] = "pcm_s16le",
                ["sample_rate"] = 16000,
            },
            ["target_streaming_delay_ms"] = 480,
        };
        if (keyterms is not null)
        {
            session["context_bias"] = keyterms;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "session.update",
            ["session"] = session,
        });
    }

    private static Uri DeepgramUri(bool streaming, IReadOnlyList<string>? keyterms)
    {
        var query = "model=" + Uri.EscapeDataString(DeepgramModel)
            + "&language=es-419&encoding=linear16&sample_rate=16000&channels=1&smart_format=true";
        if (streaming)
        {
            query += "&interim_results=true&endpointing=false";
        }

        if (keyterms is not null)
        {
            foreach (var term in keyterms)
            {
                var trimmed = term.Trim();
                if (trimmed.Length > 0)
                {
                    query += "&keyterm=" + Uri.EscapeDataString(trimmed);
                }
            }
        }

        var builder = new UriBuilder("https://api.deepgram.com")
        {
            Scheme = streaming ? "wss" : "https",
            Path = "/v1/listen",
            Query = query,
            Port = -1,
        };
        return builder.Uri;
    }

    private static async Task PumpTextAsync(
        ClientWebSocket socket,
        ChannelWriter<Incoming> writer,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = message.ToString();
                message.Clear();
                writer.TryWrite(new Incoming(json, Stopwatch.GetTimestamp()));
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async Task WaitForTypeAsync(
        ChannelReader<Incoming> reader,
        string type,
        string apiKey,
        bool contextPass,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var incoming = await ReadAsync(reader, deadline - DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            if (incoming is null)
            {
                break;
            }

            var actual = ReadEventType(incoming.Json);
            if (actual == "error")
            {
                ThrowMistralError(incoming.Json, apiKey, contextPass);
            }

            if (actual == type)
            {
                return;
            }
        }

        throw new VendorException("Timed out waiting for " + type + ".");
    }

    private static async Task<Incoming?> ReadAsync(
        ChannelReader<Incoming> reader,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);
        try
        {
            return await reader.ReadAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    private static async Task SendTextAsync(ClientWebSocket socket, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CloseSocketAsync(ClientWebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // The server often aborts the socket instead of completing the close handshake.
        }
    }

    private static void ThrowIfDeepgramError(string json, string apiKey)
    {
        if (!TryParse(json, out var root))
        {
            return;
        }

        using (root)
        {
            if (!root.RootElement.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "Error", StringComparison.Ordinal))
            {
                return;
            }

            var message = root.RootElement.TryGetProperty("message", out var messageProperty)
                ? messageProperty.GetString()
                : "unknown error";
            throw new VendorException("Deepgram listen error: " + SecretRedaction.Snippet(message, 180, apiKey));
        }
    }

    private static bool IsDeepgramFinalize(string json)
    {
        if (!TryParse(json, out var root))
        {
            return false;
        }

        using (root)
        {
            var element = root.RootElement;
            return element.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), "Results", StringComparison.Ordinal)
                && element.TryGetProperty("from_finalize", out var fromFinalize)
                && fromFinalize.ValueKind == JsonValueKind.True;
        }
    }

    private static bool TryDeepgramResults(string json, out string text, out bool isFinal)
    {
        text = string.Empty;
        isFinal = false;
        if (!TryParse(json, out var root))
        {
            return false;
        }

        using (root)
        {
            var element = root.RootElement;
            if (!element.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "Results", StringComparison.Ordinal)
                || !element.TryGetProperty("channel", out var channel)
                || !channel.TryGetProperty("alternatives", out var alternatives)
                || alternatives.GetArrayLength() == 0)
            {
                return false;
            }

            var first = alternatives[0];
            text = first.TryGetProperty("transcript", out var transcript)
                ? transcript.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            isFinal = element.TryGetProperty("is_final", out var isFinalProperty)
                && isFinalProperty.ValueKind == JsonValueKind.True;
            return true;
        }
    }

    private static string? ReadDeepgramPrerecorded(string json)
    {
        if (!TryParse(json, out var root))
        {
            return null;
        }

        using (root)
        {
            var element = root.RootElement;
            if (!element.TryGetProperty("results", out var results)
                || !results.TryGetProperty("channels", out var channels)
                || channels.GetArrayLength() == 0)
            {
                return null;
            }

            var channel = channels[0];
            if (!channel.TryGetProperty("alternatives", out var alternatives) || alternatives.GetArrayLength() == 0)
            {
                return null;
            }

            var first = alternatives[0];
            var text = first.TryGetProperty("transcript", out var transcript)
                ? transcript.GetString()
                : null;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }

    private static void ThrowMistralError(string json, string apiKey, bool contextPass)
    {
        var message = ReadNestedMessage(json);
        var snippet = SecretRedaction.Snippet(message, 180, apiKey);
        if (contextPass)
        {
            throw new ContextRejectedException(snippet);
        }

        throw new VendorException("Voxtral realtime error: " + snippet);
    }

    private static void ThrowHttp(string vendor, int status, string body, string apiKey, bool contextPass)
    {
        var snippet = vendor + " HTTP " + status + ": " + SecretRedaction.Snippet(body, 180, apiKey);
        if (contextPass && status is 400 or 404 or 422)
        {
            throw new ContextRejectedException(snippet);
        }

        throw new VendorException(snippet);
    }

    private static string ReadTranscript(string json)
    {
        if (!TryParse(json, out var root))
        {
            return string.Empty;
        }

        using (root)
        {
            if (root.RootElement.TryGetProperty("text", out var text))
            {
                return text.GetString()?.Trim() ?? string.Empty;
            }

            if (root.RootElement.TryGetProperty("transcript", out var transcript))
            {
                return transcript.GetString()?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string? ReadEventType(string json)
    {
        if (!TryParse(json, out var root))
        {
            return null;
        }

        using (root)
        {
            return root.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
        }
    }

    private static string ReadStringProperty(string json, string name)
    {
        if (!TryParse(json, out var root))
        {
            return string.Empty;
        }

        using (root)
        {
            return root.RootElement.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
        }
    }

    private static string ReadNestedMessage(string json)
    {
        if (!TryParse(json, out var root))
        {
            return json;
        }

        using (root)
        {
            var element = root.RootElement;
            if (element.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? string.Empty;
                }

                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? string.Empty;
                }
            }

            if (element.TryGetProperty("message", out var top))
            {
                return top.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool TryParse(string json, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static int MillisecondsSince(long started)
    {
        return (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static int MillisecondsBetween(long started, long ended)
    {
        var ticks = ended - started;
        return (int)(ticks * 1000d / Stopwatch.Frequency);
    }

    private sealed record Incoming(string Json, long At);

    private sealed record StreamCapture(string Text, long AudioEndedAt, int? FirstPartialMilliseconds);
}
