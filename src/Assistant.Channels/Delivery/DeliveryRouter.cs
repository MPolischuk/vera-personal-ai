using System.Diagnostics;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Delivery;

/// <summary>
/// Runs inside <c>notification.dispatch</c>. Writes each attempt before <c>SendAsync</c>.
/// </summary>
public sealed class DeliveryRouter(
    INotificationStore notifications,
    INotificationDeliveryStore deliveries,
    IChannelRegistry registry,
    QuietHoursPolicy quietHours,
    IDeliveryAckWait acks,
    IOptions<DeliveryOptions> options,
    TimeProvider clock,
    ILogger<DeliveryRouter> logger)
{
    public async ValueTask<JobOutcome> DispatchAsync(
        StoredNotification notification,
        bool allowDuringQuietHours,
        int attempt,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Status is NotificationStatuses.Cancelled
            or NotificationStatuses.Delivered
            or NotificationStatuses.Acted
            or NotificationStatuses.Failed)
        {
            return new JobOutcome.Succeeded();
        }

        var current = options.Value;
        await UpdateAsync(notification, NotificationStatuses.Dispatching, deliveredChannel: null, ct);

        var decision = await quietHours.EvaluateAsync(
            notification.Kind,
            notification.DueAt,
            allowDuringQuietHours,
            notification.UserId,
            ct);

        switch (decision)
        {
            case QuietHoursDecision.Defer defer:
                await UpdateAsync(notification, NotificationStatuses.Deferred, deliveredChannel: null, ct);
                return new JobOutcome.Reschedule(defer.Until);
            case QuietHoursDecision.Drop:
                DeliveryLog.QuietHoursDropped(logger, notification.Id);
                await UpdateAsync(notification, NotificationStatuses.Cancelled, deliveredChannel: null, ct);
                return new JobOutcome.Succeeded();
            case QuietHoursDecision.Deliver:
            case QuietHoursDecision.DeliverSilently:
                break;
            default:
                throw new InvalidOperationException("Unknown quiet-hours decision.");
        }

        var now = clock.GetUtcNow();
        var sentRecently = await notifications.CountDeliveredSinceAsync(
            notification.UserId,
            now.AddHours(-1),
            ct);
        if (sentRecently >= current.MaxNotificationsPerHour)
        {
            DeliveryLog.RateLimited(logger, notification.Id, sentRecently);
            await UpdateAsync(notification, NotificationStatuses.Deferred, deliveredChannel: null, ct);
            return new JobOutcome.Reschedule(now.AddMinutes(15));
        }

        var silent = decision is QuietHoursDecision.DeliverSilently;
        var fanOut = string.Equals(current.Mode, DeliveryOptions.FanOut, StringComparison.Ordinal);
        var transientAfter = new List<TimeSpan>();
        var sawTransient = false;
        string? deliveredChannel = null;

        foreach (var channel in Candidates(notification.PreferredChannel, current))
        {
            var reach = await channel.GetReachabilityAsync(notification.UserId, ct);
            if (!reach.Reachable)
            {
                continue;
            }

            var draft = BuildMessage(notification, channel.Id, reach.TargetRef, silent);
            var adapted = CapabilityAdapter.Adapt(draft, channel.Capabilities, reach);
            if (adapted is OutboundAdaptation.NotApplicable skipped)
            {
                await RecordAsync(
                    notification.Id,
                    channel.Id.Value,
                    reach.TargetRef,
                    DeliveryResults.NotApplicable,
                    skipped.Reason,
                    externalId: null,
                    ct);
                CountOutbound(channel.Id.Value, DeliveryResults.NotApplicable);
                continue;
            }

            if (adapted is not OutboundAdaptation.Ready ready)
            {
                throw new InvalidOperationException("Unknown outbound adaptation.");
            }

            var deliveryId = Guid.CreateVersion7();
            var message = ready.Message with { MessageId = deliveryId };
            var attemptNumber = await deliveries.CountByNotificationAsync(notification.Id, ct) + 1;
            await deliveries.BeginAsync(
                deliveryId,
                notification.Id,
                channel.Id.Value,
                TargetRefHash.Compute(reach.TargetRef),
                attemptNumber,
                ct);

            if (channel.Capabilities.RequiresAck)
            {
                acks.Arm(deliveryId, notification.Id);
            }

            var started = Stopwatch.GetTimestamp();
            var result = await SendAsync(notification.Id, channel, message, ct);
            ChannelsTelemetry.OutboundDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("channel", channel.Id.Value));

            switch (result)
            {
                case OutboundResult.Delivered delivered:
                    await deliveries.CompleteAsync(
                        deliveryId,
                        DeliveryResults.Delivered,
                        errorCode: null,
                        delivered.ExternalId,
                        ct);
                    CountOutbound(channel.Id.Value, DeliveryResults.Delivered);
                    deliveredChannel ??= channel.Id.Value;
                    if (!fanOut)
                    {
                        await MarkDeliveredAsync(notification, deliveredChannel, ct);
                        return new JobOutcome.Succeeded();
                    }

                    break;

                case OutboundResult.AwaitingAck waiting:
                    await deliveries.CompleteAsync(
                        deliveryId,
                        DeliveryResults.AwaitingAck,
                        errorCode: null,
                        externalId: null,
                        ct);
                    var timeout = waiting.Timeout > current.HubAckTimeout
                        ? current.HubAckTimeout
                        : waiting.Timeout;
                    var acked = await WaitForAckAsync(deliveryId, timeout, ct);
                    if (acked)
                    {
                        await deliveries.CompleteAsync(
                            deliveryId,
                            DeliveryResults.Acked,
                            errorCode: null,
                            externalId: null,
                            ct);
                        CountOutbound(channel.Id.Value, DeliveryResults.Acked);
                        deliveredChannel ??= channel.Id.Value;
                        if (!fanOut)
                        {
                            await MarkDeliveredAsync(notification, deliveredChannel, ct);
                            return new JobOutcome.Succeeded();
                        }
                    }
                    else
                    {
                        await deliveries.CompleteAsync(
                            deliveryId,
                            DeliveryResults.AckTimeout,
                            "ack_timeout",
                            externalId: null,
                            ct);
                        CountOutbound(channel.Id.Value, DeliveryResults.AckTimeout);
                    }

                    break;

                case OutboundResult.TransientFailure transientFailure:
                    await deliveries.CompleteAsync(
                        deliveryId,
                        DeliveryResults.Transient,
                        transientFailure.Code,
                        externalId: null,
                        ct);
                    CountOutbound(channel.Id.Value, DeliveryResults.Transient);
                    sawTransient = true;
                    if (transientFailure.RetryAfter is { } after)
                    {
                        transientAfter.Add(after);
                    }

                    break;

                case OutboundResult.PermanentFailure permanent:
                    await deliveries.CompleteAsync(
                        deliveryId,
                        DeliveryResults.Permanent,
                        permanent.Code,
                        externalId: null,
                        ct);
                    CountOutbound(channel.Id.Value, DeliveryResults.Permanent);
                    if (permanent.DeactivateTarget)
                    {
                        DeliveryLog.DeactivateTarget(logger, notification.Id, channel.Id.Value);
                    }

                    break;

                case OutboundResult.NotApplicable notApplicable:
                    await deliveries.CompleteAsync(
                        deliveryId,
                        DeliveryResults.NotApplicable,
                        notApplicable.Reason,
                        externalId: null,
                        ct);
                    CountOutbound(channel.Id.Value, DeliveryResults.NotApplicable);
                    break;

                default:
                    throw new InvalidOperationException("Unknown outbound result.");
            }
        }

        if (deliveredChannel is not null)
        {
            await MarkDeliveredAsync(notification, deliveredChannel, ct);
            return new JobOutcome.Succeeded();
        }

        if (sawTransient)
        {
            if (attempt >= NotificationDispatchHandler.MaxAttempts)
            {
                await UpdateAsync(notification, NotificationStatuses.Failed, deliveredChannel: null, ct);
                return new JobOutcome.Fail("transient");
            }

            await UpdateAsync(notification, NotificationStatuses.Deferred, deliveredChannel: null, ct);
            TimeSpan? retryAfter = transientAfter.Count == 0 ? null : transientAfter.Min();
            return new JobOutcome.Retry("transient", retryAfter);
        }

        await UpdateAsync(notification, NotificationStatuses.Failed, deliveredChannel: null, ct);
        return new JobOutcome.Fail("no_reachable_channel");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A channel must not throw on provider failure; the router records a transient result.")]
    private async ValueTask<OutboundResult> SendAsync(
        Guid notificationId,
        IOutboundChannel channel,
        OutboundMessage message,
        CancellationToken ct)
    {
        try
        {
            return await channel.SendAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DeliveryLog.SendThrew(logger, notificationId, channel.Id.Value, exception.GetType().Name);
            return new OutboundResult.TransientFailure(exception.GetType().Name, RetryAfter: null);
        }
    }

    private async ValueTask<bool> WaitForAckAsync(Guid deliveryId, TimeSpan timeout, CancellationToken ct)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            return await acks.WaitAsync(deliveryId, timeout, ct);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private List<IOutboundChannel> Candidates(ChannelId? preferred, DeliveryOptions current)
    {
        var result = new List<IOutboundChannel>();
        if (preferred is { } preferredId && registry.FindOutbound(preferredId) is { } preferredChannel)
        {
            result.Add(preferredChannel);
        }

        foreach (var value in current.Order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var id = new ChannelId(value);
            if (result.Any(channel => channel.Id == id))
            {
                continue;
            }

            if (registry.FindOutbound(id) is { } channel)
            {
                result.Add(channel);
            }
        }

        return result;
    }

    private async ValueTask RecordAsync(
        Guid notificationId,
        string channel,
        string? targetRef,
        string result,
        string? errorCode,
        string? externalId,
        CancellationToken ct)
    {
        var deliveryId = Guid.CreateVersion7();
        var attemptNumber = await deliveries.CountByNotificationAsync(notificationId, ct) + 1;
        await deliveries.BeginAsync(
            deliveryId,
            notificationId,
            channel,
            TargetRefHash.Compute(targetRef),
            attemptNumber,
            ct);
        await deliveries.CompleteAsync(deliveryId, result, errorCode, externalId, ct);
    }

    private async ValueTask MarkDeliveredAsync(
        StoredNotification notification,
        string channel,
        CancellationToken ct)
    {
        await UpdateAsync(notification, NotificationStatuses.Delivered, channel, ct);
        if (notification.DueAt is { } due)
        {
            var latency = (clock.GetUtcNow() - due).TotalMilliseconds;
            ChannelsTelemetry.DeliveryLatency.Record(
                latency,
                new KeyValuePair<string, object?>("kind", NotificationKindNames.ToWire(notification.Kind)),
                new KeyValuePair<string, object?>("channel", channel));
        }
    }

    private async ValueTask UpdateAsync(
        StoredNotification notification,
        string status,
        string? deliveredChannel,
        CancellationToken ct)
    {
        await notifications.UpdateStatusAsync(notification.Id, status, deliveredChannel, ct);
        ChannelsTelemetry.Status.Add(
            1,
            new KeyValuePair<string, object?>("kind", NotificationKindNames.ToWire(notification.Kind)),
            new KeyValuePair<string, object?>("status", status));
    }

    private static void CountOutbound(string channel, string result)
        => ChannelsTelemetry.Outbound.Add(
            1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("kind", "notification"),
            new KeyValuePair<string, object?>("result", result));

    private static OutboundMessage BuildMessage(
        StoredNotification notification,
        ChannelId channel,
        string? targetRef,
        bool silent)
        => new(
            MessageId: Guid.Empty,
            Channel: channel,
            UserId: notification.UserId,
            TargetRef: targetRef ?? "",
            Kind: OutboundKind.Notification,
            Text: notification.Body,
            Title: notification.Title,
            Silent: silent,
            Actions: notification.Actions,
            NotificationId: notification.Id,
            CollapseKey: notification.Id.ToString("D"));
}
