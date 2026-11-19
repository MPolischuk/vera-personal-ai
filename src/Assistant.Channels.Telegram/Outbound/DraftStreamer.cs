using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>
/// Streams one turn into a rich-message draft. Updates are coalesced to the
/// configured interval, and the same body is resent every <see cref="Heartbeat"/>
/// so Telegram does not expire the preview.
/// </summary>
internal sealed class DraftStreamer
{
    public const string ThinkingPlaceholder = "<tg-thinking>Pensando…</tg-thinking>";

    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    private readonly ITelegramBotClient _client;
    private readonly long _chatId;
    private readonly ActiveDraftRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, ValueTask> _onFailed;
    private readonly ILogger _logger;
    private readonly string _peerRef;
    private readonly CancellationTokenSource _cts;
    private readonly object _gate = new();
    private readonly Task _loop;
    private string _pending = "";
    private string _lastSent = "";
    private DateTimeOffset _lastSentAt;
    private int _stopped;

    private DraftStreamer(
        ITelegramBotClient client,
        long chatId,
        int draftId,
        ActiveDraftRegistry registry,
        TimeProvider clock,
        TimeSpan interval,
        Func<CancellationToken, ValueTask> onFailed,
        ILogger logger,
        string peerRef,
        CancellationToken turnToken)
    {
        _client = client;
        _chatId = chatId;
        DraftId = draftId;
        _registry = registry;
        _clock = clock;
        _interval = interval > TimeSpan.Zero ? interval : TimeSpan.FromSeconds(1);
        _onFailed = onFailed;
        _logger = logger;
        _peerRef = peerRef;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(turnToken);
        _lastSent = ThinkingPlaceholder;
        _lastSentAt = clock.GetUtcNow();
        IsActive = true;
        _loop = RunAsync(_cts.Token);
    }

    public int DraftId { get; }

    public bool IsActive { get; private set; }

    public static async Task<DraftStreamer?> TryStartAsync(
        ITelegramBotClient client,
        long chatId,
        ActiveDraftRegistry registry,
        Guid peerId,
        Guid userId,
        TimeProvider clock,
        TimeSpan interval,
        Func<CancellationToken, ValueTask> onFailed,
        ILogger logger,
        string peerRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(onFailed);

        var draftId = registry.NextDraftId();
        registry.Add(chatId, draftId, peerId, userId);
        if (!await SendAsync(client, chatId, draftId, ThinkingPlaceholder, logger, peerRef, cancellationToken).ConfigureAwait(false))
        {
            registry.Remove(chatId, draftId);
            return null;
        }

        return new DraftStreamer(
            client, chatId, draftId, registry, clock, interval, onFailed, logger, peerRef, cancellationToken);
    }

    public void Update(string text)
    {
        lock (_gate)
        {
            _pending = text ?? "";
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        IsActive = false;
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The loop ends when the turn ends.
        }

        _registry.Remove(_chatId, DraftId);
        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _stopped) == 0)
            {
                await Task.Delay(_interval, _clock, cancellationToken).ConfigureAwait(false);
                if (!await FlushAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop or the turn token ended the preview.
        }
    }

    private async Task<bool> FlushAsync(CancellationToken cancellationToken)
    {
        string pending;
        lock (_gate)
        {
            pending = _pending;
        }

        var body = string.IsNullOrWhiteSpace(pending)
            ? ThinkingPlaceholder
            : TelegramRichMarkdownSanitizer.Sanitize(pending);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = ThinkingPlaceholder;
        }

        var changed = !string.Equals(body, _lastSent, StringComparison.Ordinal);
        var heartbeatDue = _clock.GetUtcNow() - _lastSentAt >= Heartbeat;
        if (!changed && !heartbeatDue)
        {
            return true;
        }

        if (Volatile.Read(ref _stopped) == 1)
        {
            return false;
        }

        if (!await SendAsync(_client, _chatId, DraftId, body, _logger, _peerRef, cancellationToken).ConfigureAwait(false))
        {
            IsActive = false;
            _registry.Remove(_chatId, DraftId);
            await _onFailed(CancellationToken.None).ConfigureAwait(false);
            return false;
        }

        _lastSent = body;
        _lastSentAt = _clock.GetUtcNow();
        return true;
    }

    private static async Task<bool> SendAsync(
        ITelegramBotClient client,
        long chatId,
        int draftId,
        string markdown,
        ILogger logger,
        string peerRef,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.SendRichMessageDraftAsync(
                new SendRichMessageDraftRequest
                {
                    ChatId = chatId,
                    DraftId = draftId,
                    RichMessage = new InputRichMessage { Markdown = markdown },
                    CanStop = true,
                    KeepOnStop = false,
                },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TelegramApiException exception)
        {
            TelegramTurnLog.DraftFailed(logger, exception.ErrorCode ?? 0, peerRef);
            return false;
        }
    }
}
