using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Assistant.Core.Speech;
using Assistant.Providers.Speech.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Speech.Deepgram;

/// <summary>
/// Deepgram Nova listen: WebSocket streaming for live partials, then a pre-recorded
/// listen of the same PCM as the authoritative final (PTT has the full utterance).
/// Port audio is PCM s16le 16 kHz mono.
/// </summary>
internal sealed class DeepgramSpeechToTextProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SpeechOptions> options,
    ILogger<DeepgramSpeechToTextProvider> logger) : ISpeechToTextProvider
{
    public const string HttpClientName = "Deepgram";

    private readonly SttOptions _stt = options.Value.Stt;

    public async IAsyncEnumerable<TranscriptionEvent> TranscribeAsync(
        IAsyncEnumerable<AudioChunk> audio,
        TranscriptionOptions transcriptionOptions,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var language = string.IsNullOrWhiteSpace(transcriptionOptions.Language)
            ? _stt.Language
            : transcriptionOptions.Language;
        var keyterms = _stt.Keyterms;

        using var socket = new ClientWebSocket();
        var uri = BuildListenUri(_stt.Endpoint, _stt.Model, language, keyterms, streaming: true);

        try
        {
            socket.Options.SetRequestHeader("Authorization", $"Token {_stt.ApiKey}");
            await socket.ConnectAsync(uri, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Deepgram WebSocket connect failed against endpoint '{_stt.Endpoint}' model '{_stt.Model}'.", e);
        }

        var channel = Channel.CreateUnbounded<TranscriptionEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var pcm = new MemoryStream();

        var finalized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiveTask = PumpReceiveAsync(socket, channel.Writer, finalized, ct);
        var sendTask = SendAudioAsync(socket, audio, pcm, finalized, ct);
        _ = sendTask.ContinueWith(
            static (task, state) =>
            {
                if (task.IsFaulted)
                {
                    ((ChannelWriter<TranscriptionEvent>)state!).TryComplete(task.Exception);
                }
            },
            channel.Writer,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var streamingFinals = new List<string>();
        string? lastInterim = null;
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (evt.IsFinal)
                {
                    streamingFinals.Add(evt.Text);
                    continue;
                }

                lastInterim = evt.Text;
                yield return evt;
            }

            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Observed; REST still runs on whatever PCM arrived.
            }

            var authoritative = await TryPrerecordedAsync(pcm, language, keyterms, ct)
                .ConfigureAwait(false);
            var text = !string.IsNullOrWhiteSpace(authoritative)
                ? authoritative.Trim()
                : JoinStreaming(streamingFinals, lastInterim);
            if (text.Length > 0)
            {
                yield return new TranscriptionEvent(text, IsFinal: true, Confidence: null);
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
                // Observed; the receive channel already surfaced the failure.
            }

            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Observed; the channel already completed.
            }

            await pcm.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static Uri BuildListenUri(
        string endpoint,
        string model,
        string language,
        IEnumerable<string>? keyterms = null,
        bool streaming = true)
    {
        var http = new Uri(endpoint);
        var scheme = streaming
            ? (http.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss")
            : http.Scheme;
        var builder = new UriBuilder(http)
        {
            Scheme = scheme,
            Path = "/v1/listen",
            Port = http.IsDefaultPort ? -1 : http.Port,
            Query = BuildListenQuery(model, language, keyterms, streaming),
        };

        return builder.Uri;
    }

    internal static string BuildListenQuery(
        string model,
        string language,
        IEnumerable<string>? keyterms,
        bool streaming)
    {
        var deepgramLanguage = ToDeepgramLanguage(language);
        var query =
            $"model={Uri.EscapeDataString(model)}" +
            $"&language={Uri.EscapeDataString(deepgramLanguage)}" +
            "&encoding=linear16&sample_rate=16000&channels=1&smart_format=true";
        if (streaming)
        {
            query +=
                "&interim_results=true" +
                // Client owns end-of-speech (Grabar/Enviar). Vendor endpointing
                // would finalize on a mid-sentence pause and drop the tail.
                "&endpointing=false";
        }

        foreach (var term in NormalizeKeyterms(keyterms))
        {
            query += $"&keyterm={Uri.EscapeDataString(term)}";
        }

        return query;
    }

    internal static IReadOnlyList<string> NormalizeKeyterms(IEnumerable<string>? keyterms)
    {
        if (keyterms is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var raw in keyterms)
        {
            var term = raw.Trim();
            if (term.Length == 0 || !seen.Add(term))
            {
                continue;
            }

            list.Add(term);
        }

        return list;
    }

    private static string JoinStreaming(IEnumerable<string> finals, string? lastInterim)
    {
        var parts = new List<string>();
        foreach (var raw in finals)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                parts.Add(trimmed);
            }
        }

        if (parts.Count > 0)
        {
            return string.Join(" ", parts);
        }

        return lastInterim?.Trim() ?? string.Empty;
    }

    internal static byte[] WrapWav(ReadOnlySpan<byte> pcm)
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bits = 16;
        const int byteRate = sampleRate * channels * (bits / 8);
        const short blockAlign = channels * (bits / 8);
        var header = new byte[44 + pcm.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BitConverter.TryWriteBytes(header.AsSpan(4), 36 + pcm.Length);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        BitConverter.TryWriteBytes(header.AsSpan(16), 16);
        BitConverter.TryWriteBytes(header.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(header.AsSpan(22), channels);
        BitConverter.TryWriteBytes(header.AsSpan(24), sampleRate);
        BitConverter.TryWriteBytes(header.AsSpan(28), byteRate);
        BitConverter.TryWriteBytes(header.AsSpan(32), blockAlign);
        BitConverter.TryWriteBytes(header.AsSpan(34), bits);
        Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BitConverter.TryWriteBytes(header.AsSpan(40), pcm.Length);
        pcm.CopyTo(header.AsSpan(44));
        return header;
    }

    /// <summary>
    /// Deepgram listen accepts <c>es</c> / <c>es-419</c>, not BCP-47 tags like <c>es-AR</c>.
    /// Latin-American cultures map to <c>es-419</c> so conversational rioplatense
    /// is not scored against Peninsular Spanish.
    /// </summary>
    internal static string ToDeepgramLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "es";
        }

        var trimmed = language.Trim();
        if (trimmed.Equals("es", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("es-ES", StringComparison.OrdinalIgnoreCase))
        {
            return "es";
        }

        var dash = trimmed.IndexOf('-');
        if (dash > 0
            && trimmed[..dash].Equals("es", StringComparison.OrdinalIgnoreCase))
        {
            return "es-419";
        }

        return trimmed;
    }

    private static async Task SendAudioAsync(
        ClientWebSocket socket,
        IAsyncEnumerable<AudioChunk> audio,
        Stream pcm,
        TaskCompletionSource<bool> finalized,
        CancellationToken ct)
    {
        await foreach (var chunk in audio.WithCancellation(ct).ConfigureAwait(false))
        {
            if (chunk.Data.IsEmpty)
            {
                continue;
            }

            pcm.Write(chunk.Data.Span);
            await socket.SendAsync(chunk.Data, WebSocketMessageType.Binary, endOfMessage: true, ct)
                .ConfigureAwait(false);
        }

        var finalize = Encoding.UTF8.GetBytes("""{"type":"Finalize"}""");
        await socket.SendAsync(finalize, WebSocketMessageType.Text, endOfMessage: true, ct)
            .ConfigureAwait(false);

        try
        {
            await finalized.Task.WaitAsync(TimeSpan.FromMilliseconds(1500), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // CloseStream anyway so the receive loop can finish.
        }

        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var close = Encoding.UTF8.GetBytes("""{"type":"CloseStream"}""");
        await socket.SendAsync(close, WebSocketMessageType.Text, endOfMessage: true, ct)
            .ConfigureAwait(false);
    }

    private async Task PumpReceiveAsync(
        ClientWebSocket socket,
        ChannelWriter<TranscriptionEvent> writer,
        TaskCompletionSource<bool> finalized,
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
                    await CloseQuietlyAsync(socket).ConfigureAwait(false);
                    break;
                }
                catch (WebSocketException e)
                {
                    throw new InvalidOperationException(
                        $"Deepgram WebSocket receive failed against endpoint '{_stt.Endpoint}'.", e);
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    SpeechLog.DeepgramSocketClosed(
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

                if (DeepgramTranscriptMapper.TryMap(json, out var text, out var isFinal, out var confidence))
                {
                    await writer.WriteAsync(new TranscriptionEvent(text, isFinal, confidence), ct)
                        .ConfigureAwait(false);
                }

                if (DeepgramTranscriptMapper.IsFinalizeAck(json))
                {
                    finalized.TrySetResult(true);
                }
                else if (DeepgramTranscriptMapper.TryReadError(json, out var errorMessage))
                {
                    finalized.TrySetResult(true);
                    throw new InvalidOperationException(
                        $"Deepgram listen error against endpoint '{_stt.Endpoint}': {errorMessage}");
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
            writer.TryComplete();
        }
    }

    private async Task<string?> TryPrerecordedAsync(
        MemoryStream pcm,
        string language,
        IEnumerable<string>? keyterms,
        CancellationToken ct)
    {
        if (pcm.Length == 0)
        {
            return null;
        }

        try
        {
            var uri = BuildListenUri(_stt.Endpoint, _stt.Model, language, keyterms, streaming: false);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {_stt.ApiKey}");
            request.Content = new ByteArrayContent(WrapWav(pcm.ToArray()));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Deepgram prerecorded listen returned {(int)response.StatusCode} against '{uri.GetLeftPart(UriPartial.Path)}'.");
            }

            return DeepgramTranscriptMapper.TryMapPrerecorded(body, out var text) ? text : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            SpeechLog.DeepgramPrerecordedFailed(logger, e);
            return null;
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
            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure, "cancelled", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // Already closing or aborted.
        }
    }
}
