using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Assistant.Core.Speech;
using Assistant.Providers.Speech.Configuration;
using Assistant.Providers.Speech.Deepgram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Speech.ElevenLabs;

/// <summary>
/// Scribe v2 Realtime over one WebSocket per turn. Vera already owns end of turn,
/// so the socket uses manual commit when the audio enumerable completes.
/// <c>enable_logging=false</c> and <c>Stt:Keyterms</c> are on the handshake.
/// A silent chunk keeps the session alive if the client pauses; the socket still
/// closes with the turn (the server drops a session after 15 s without messages).
/// </summary>
internal sealed class ElevenLabsRealtimeSpeechToTextProvider(
    IOptions<SpeechOptions> options,
    ILogger<ElevenLabsRealtimeSpeechToTextProvider> logger) : ISpeechToTextProvider
{
    internal const string ProviderName = "elevenlabs-realtime";

    /// <summary>List price USD 0.39 per hour, before the keyterm surcharge.</summary>
    public const decimal PublishedPricePerMinuteUsd = 0.0065m;

    internal const int SampleRate = 16_000;

    private const int MaxChunkBytes = 32_000;

    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(10);

    private static readonly byte[] SilenceKeepalive = new byte[3_200];

    private readonly SttOptions _stt = options.Value.Stt;

    public async IAsyncEnumerable<TranscriptionEvent> TranscribeAsync(
        IAsyncEnumerable<AudioChunk> audio,
        TranscriptionOptions transcriptionOptions,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var language = string.IsNullOrWhiteSpace(transcriptionOptions.Language)
            ? _stt.Language
            : transcriptionOptions.Language;
        var uri = BuildRealtimeUri(_stt.Endpoint, _stt.Model, language, _stt.Keyterms);

        using var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("xi-api-key", _stt.ApiKey);
            await socket.ConnectAsync(uri, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"ElevenLabs Realtime WebSocket connect failed against endpoint '{_stt.Endpoint}' model '{_stt.Model}'.",
                e);
        }

        var channel = Channel.CreateUnbounded<TranscriptPiece>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var sessionReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiveTask = PumpReceiveAsync(socket, channel.Writer, sessionReady, committed, ct);
        var sendTask = SendAudioAsync(socket, audio, sessionReady, committed, ct);
        _ = sendTask.ContinueWith(
            static (task, state) =>
            {
                if (task.IsFaulted)
                {
                    ((ChannelWriter<TranscriptPiece>)state!).TryComplete(task.Exception);
                }
            },
            channel.Writer,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        string? lastPartial = null;
        string? committedText = null;
        try
        {
            await foreach (var piece in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (piece.Committed)
                {
                    committedText = piece.Text;
                    break;
                }

                lastPartial = piece.Text;
                yield return new TranscriptionEvent(piece.Text, IsFinal: false, Confidence: null);
            }

            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Observed; the transcript below still uses whatever text arrived.
            }

            var text = !string.IsNullOrWhiteSpace(committedText) ? committedText : lastPartial;
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new TranscriptionEvent(text.Trim(), IsFinal: true, Confidence: null);
            }
        }
        finally
        {
            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Observed.
            }

            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Observed; the channel already completed.
            }

            await CloseQuietlyAsync(socket).ConfigureAwait(false);
        }
    }

    internal static Uri BuildRealtimeUri(
        string endpoint,
        string model,
        string language,
        IEnumerable<string>? keyterms)
    {
        var http = new Uri(endpoint);
        var scheme = http.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss";
        var builder = new UriBuilder(http)
        {
            Scheme = scheme,
            Path = "/v1/speech-to-text/realtime",
            Port = http.IsDefaultPort ? -1 : http.Port,
            Query = BuildRealtimeQuery(model, language, keyterms),
        };
        return builder.Uri;
    }

    internal static string BuildRealtimeQuery(string model, string language, IEnumerable<string>? keyterms)
    {
        var query =
            $"model_id={Uri.EscapeDataString(model)}" +
            "&audio_format=pcm_16000" +
            $"&language_code={Uri.EscapeDataString(ToLanguageCode(language))}" +
            "&commit_strategy=manual" +
            "&enable_logging=false" +
            "&keepalive_interval_ms=10000";
        foreach (var term in DeepgramSpeechToTextProvider.NormalizeKeyterms(keyterms))
        {
            query += $"&keyterms={Uri.EscapeDataString(term)}";
        }

        return query;
    }

    internal static string BuildAudioMessage(ReadOnlySpan<byte> pcm, bool commit)
    {
        return JsonSerializer.Serialize(new AudioChunkPayload(
            "input_audio_chunk",
            pcm.IsEmpty ? string.Empty : Convert.ToBase64String(pcm),
            commit,
            SampleRate));
    }

    internal static string ToLanguageCode(string language)
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

    internal static bool TryRead(string json, out string text, out bool committed, out string? error)
    {
        text = string.Empty;
        committed = false;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = root.TryGetProperty("message_type", out var typeProperty)
                ? typeProperty.GetString()
                : null;
            if (type is "partial_transcript")
            {
                text = ReadText(root);
                return text.Length > 0;
            }

            if (type is "committed_transcript" or "committed_transcript_with_timestamps")
            {
                text = ReadText(root);
                committed = true;
                return true;
            }

            if (root.TryGetProperty("error", out var errorProperty)
                && errorProperty.ValueKind == JsonValueKind.String)
            {
                error = errorProperty.GetString();
                return false;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ReadText(JsonElement root)
    {
        return root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static async Task SendAudioAsync(
        ClientWebSocket socket,
        IAsyncEnumerable<AudioChunk> audio,
        TaskCompletionSource<bool> sessionReady,
        TaskCompletionSource<bool> committed,
        CancellationToken ct)
    {
        try
        {
            var ready = await sessionReady.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            if (!ready || socket.State != WebSocketState.Open)
            {
                return;
            }
        }
        catch (TimeoutException)
        {
            return;
        }

        var sentSpeech = false;
        await using var enumerator = audio.GetAsyncEnumerator(ct);
        var pending = enumerator.MoveNextAsync().AsTask();
        while (socket.State == WebSocketState.Open)
        {
            using var idle = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, idle.Token);
            var delay = Task.Delay(KeepaliveInterval, linked.Token);
            var winner = await Task.WhenAny(pending, delay).ConfigureAwait(false);
            if (!ReferenceEquals(winner, pending))
            {
                ct.ThrowIfCancellationRequested();
                await SendAsync(socket, SilenceKeepalive, commit: false, ct).ConfigureAwait(false);
                continue;
            }

            idle.Cancel();
            try
            {
                await delay.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The next chunk arrived before the keepalive interval.
            }

            if (!await pending.ConfigureAwait(false))
            {
                break;
            }

            var data = enumerator.Current.Data;
            if (!data.IsEmpty)
            {
                sentSpeech = true;
                var offset = 0;
                while (offset < data.Length)
                {
                    var count = Math.Min(MaxChunkBytes, data.Length - offset);
                    await SendAsync(socket, data.Slice(offset, count), commit: false, ct).ConfigureAwait(false);
                    offset += count;
                }
            }

            pending = enumerator.MoveNextAsync().AsTask();
        }

        if (!sentSpeech || socket.State != WebSocketState.Open)
        {
            return;
        }

        await SendAsync(socket, ReadOnlyMemory<byte>.Empty, commit: true, ct).ConfigureAwait(false);
        try
        {
            await committed.Task.WaitAsync(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The caller falls back to the last partial.
        }
    }

    private static async Task SendAsync(
        ClientWebSocket socket,
        ReadOnlyMemory<byte> pcm,
        bool commit,
        CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(BuildAudioMessage(pcm.Span, commit));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
    }

    private async Task PumpReceiveAsync(
        ClientWebSocket socket,
        ChannelWriter<TranscriptPiece> writer,
        TaskCompletionSource<bool> sessionReady,
        TaskCompletionSource<bool> committed,
        CancellationToken ct)
    {
        try
        {
            var buffer = new byte[64 * 1024];
            var message = new StringBuilder();
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException e)
                {
                    throw new InvalidOperationException(
                        $"ElevenLabs Realtime WebSocket receive failed against endpoint '{_stt.Endpoint}'.", e);
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    SpeechLog.RealtimeSocketClosed(
                        logger,
                        socket.CloseStatus?.ToString() ?? "none",
                        socket.CloseStatusDescription ?? string.Empty);
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
                if (IsSessionStarted(json))
                {
                    sessionReady.TrySetResult(true);
                    continue;
                }

                if (!TryRead(json, out var text, out var isCommitted, out var error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        committed.TrySetResult(true);
                        throw new InvalidOperationException(
                            $"ElevenLabs Realtime error against endpoint '{_stt.Endpoint}': {error}");
                    }

                    continue;
                }

                await writer.WriteAsync(new TranscriptPiece(text, isCommitted), ct).ConfigureAwait(false);
                if (isCommitted)
                {
                    committed.TrySetResult(true);
                    break;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            writer.TryComplete(e);
            return;
        }
        finally
        {
            sessionReady.TrySetResult(false);
            committed.TrySetResult(false);
            writer.TryComplete();
        }
    }

    private static bool IsSessionStarted(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("message_type", out var type)
                && string.Equals(type.GetString(), "session_started", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task CloseQuietlyAsync(ClientWebSocket socket)
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

    private sealed record AudioChunkPayload(
        [property: JsonPropertyName("message_type")] string MessageType,
        [property: JsonPropertyName("audio_base_64")] string AudioBase64,
        [property: JsonPropertyName("commit")] bool Commit,
        [property: JsonPropertyName("sample_rate")] int SampleRate);

    private readonly record struct TranscriptPiece(string Text, bool Committed);
}
