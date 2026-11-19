using System.Text.Json;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Delivery;

/// <summary>JSON body of a <c>notification.dispatch</c> job.</summary>
/// <param name="NotificationId">Notification to deliver.</param>
/// <param name="AllowDuringQuietHours">Explicit override copied from the publish request.</param>
public sealed record NotificationDispatchPayload(Guid NotificationId, bool AllowDuringQuietHours);

/// <summary>Job handler <c>notification.dispatch</c>.</summary>
public sealed class NotificationDispatchHandler(
    INotificationStore notifications,
    DeliveryRouter router,
    ILogger<NotificationDispatchHandler> logger) : IJobHandler
{
    public const string KindName = "notification.dispatch";

    public const int MaxAttempts = 5;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => KindName;

    public static string IdempotencyKey(Guid notificationId)
        => $"notification:{notificationId:D}:dispatch";

    public static string SerializePayload(Guid notificationId, bool allowDuringQuietHours)
        => JsonSerializer.Serialize(
            new NotificationDispatchPayload(notificationId, allowDuringQuietHours),
            Json);

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        NotificationDispatchPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<NotificationDispatchPayload>(context.PayloadJson, Json);
        }
        catch (JsonException)
        {
            DeliveryLog.PayloadInvalid(logger, context.JobId);
            return new JobOutcome.Fail("payload_invalid");
        }

        if (payload is null || payload.NotificationId == Guid.Empty)
        {
            DeliveryLog.PayloadInvalid(logger, context.JobId);
            return new JobOutcome.Fail("payload_invalid");
        }

        var notification = await notifications.FindAsync(payload.NotificationId, ct);
        if (notification is null)
        {
            return new JobOutcome.Fail("notification_missing");
        }

        return await router.DispatchAsync(
            notification,
            payload.AllowDuringQuietHours,
            context.Attempt,
            ct);
    }
}
