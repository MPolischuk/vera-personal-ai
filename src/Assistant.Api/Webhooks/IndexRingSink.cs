using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Channels.Notifications;

namespace Assistant.Api.Webhooks;

/// <summary>
/// Turn sink for one Index delivery. A finished turn notifies; it does not keep audio.
/// </summary>
internal sealed class IndexRingSink(
    INotificationPublisher notifications,
    ILogger logger,
    Guid userId,
    string deliveryId) : ITurnSink
{
    public const string DoneTitle = "Listo";

    public const int DoneBodyChars = 240;

    private ChannelConfirmationPrompt? _prompt;

    public ValueTask OnTurnStartedAsync(Guid turnId, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask OnTranscriptionAsync(string text, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask OnTextDeltaAsync(string text, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask OnToolActivityAsync(string toolName, bool completed, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask OnConfirmationRequestedAsync(ChannelConfirmationPrompt prompt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        _prompt = prompt;
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnTurnFinishedAsync(ChannelTurnResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        switch (result.Outcome)
        {
            case ChannelTurnOutcome.Completed:
                await notifications.PublishAsync(
                    new NotificationRequest(
                        userId,
                        NotificationKind.Proactive,
                        Key(deliveryId),
                        DoneTitle,
                        Clip(result.FullText, DoneBodyChars),
                        SourceType: IndexRingEndpoint.SourceName,
                        SourceId: Source(deliveryId),
                        Trust: NotificationContentTrust.Trusted,
                        AllowDuringQuietHours: false),
                    ct);
                break;

            case ChannelTurnOutcome.AwaitingConfirmation:
                var prompt = _prompt;
                var title = Clip(prompt?.ToolName, NotificationPublisher.MaxTitleLength);
                if (title.Length == 0)
                {
                    title = "Confirmación";
                }

                await notifications.PublishAsync(
                    new NotificationRequest(
                        userId,
                        NotificationKind.ConfirmationRequest,
                        Key(deliveryId, "confirm"),
                        title,
                        Clip(prompt?.Prompt, NotificationPublisher.MaxBodyLength),
                        SourceType: IndexRingEndpoint.SourceName,
                        SourceId: Source(deliveryId),
                        Trust: NotificationContentTrust.Trusted,
                        AllowDuringQuietHours: false),
                    ct);
                break;

            case ChannelTurnOutcome.Failed:
            case ChannelTurnOutcome.Rejected:
            case ChannelTurnOutcome.Interrupted:
                IndexRingLog.TurnClosed(logger, result.ErrorCode ?? result.Outcome.ToString());
                break;
        }
    }

    private static string Key(string id, string? suffix = null)
    {
        var key = suffix is null ? $"index:{id}" : $"index:{id}:{suffix}";
        return key.Length <= NotificationPublisher.MaxDedupKeyLength
            ? key
            : key[..NotificationPublisher.MaxDedupKeyLength];
    }

    private static string Source(string id)
        => id.Length <= 200 ? id : id[..200];

    private static string Clip(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || max <= 0)
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }
}

internal static partial class IndexRingLog
{
    [LoggerMessage(
        EventId = 912,
        Level = LogLevel.Error,
        Message = "Index peer is not approved for the default user")]
    public static partial void PeerNotApproved(ILogger logger);

    [LoggerMessage(
        EventId = 913,
        Level = LogLevel.Warning,
        Message = "Index ring turn closed {Code}")]
    public static partial void TurnClosed(ILogger logger, string code);
}
