using System.Text.Json;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Reminders.Firing;

/// <summary>
/// Job handler <c>reminder.snooze</c>. Sends the same reminder again without moving the series
/// and without calling a language model.
/// </summary>
public sealed class ReminderSnoozeHandler(
    IReminderFiringStore store,
    INotificationPublisher publisher,
    TimeProvider clock,
    ILogger<ReminderSnoozeHandler> logger) : IJobHandler
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<OutboundAction> Actions =
    [
        new("done", "Listo"),
        new(ReminderSnooze.ActionId, "+10 min"),
    ];

    public string Kind => ReminderSnooze.JobKind;

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryRead(context.PayloadJson, out var reminderId, out var snoozeNumber))
        {
            ReminderFireLog.PayloadInvalid(logger, context.JobId);
            return new JobOutcome.Fail("payload_invalid");
        }

        var snapshot = await store.LoadAsync(reminderId, ct);
        if (snapshot is null || snapshot.Status == ReminderStatus.Cancelled)
        {
            ReminderFireLog.Stale(logger, reminderId);
            return new JobOutcome.Succeeded();
        }

        await publisher.PublishAsync(
            new NotificationRequest(
                snapshot.UserId,
                NotificationKind.Reminder,
                ReminderSnooze.IdempotencyKey(reminderId, snoozeNumber),
                ReminderFireHandler.Title,
                snapshot.Text,
                clock.GetUtcNow(),
                ChannelOf(snapshot.OriginChannel),
                snapshot.OriginConversationId,
                Actions,
                "reminder",
                reminderId.ToString("D"),
                NotificationContentTrust.Trusted,
                AllowDuringQuietHours: true),
            ct);
        ReminderFireLog.Fired(logger, reminderId);
        return new JobOutcome.Succeeded();
    }

    private static bool TryRead(string json, out Guid reminderId, out int snoozeNumber)
    {
        reminderId = Guid.Empty;
        snoozeNumber = 0;
        try
        {
            var payload = JsonSerializer.Deserialize<SnoozePayload>(json, PayloadJson);
            if (payload is null || payload.ReminderId == Guid.Empty || payload.SnoozeNumber < 1)
            {
                return false;
            }

            reminderId = payload.ReminderId;
            snoozeNumber = payload.SnoozeNumber;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ChannelId ChannelOf(string origin)
        => origin switch
        {
            "hub" => ChannelId.Hub,
            "webpush" => ChannelId.WebPush,
            "telegram" => ChannelId.Telegram,
            "whatsapp" => ChannelId.WhatsApp,
            _ => new ChannelId(string.IsNullOrWhiteSpace(origin) ? "hub" : origin),
        };

    private sealed record SnoozePayload(Guid ReminderId, int SnoozeNumber);
}
