using System.Text;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Api.Protocol;
using Assistant.Api.Realtime;
using Assistant.Channels.Delivery;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Channels;

/// <summary>Hub outbound channel. Sends <c>assistant.notification</c> and waits for an ack in the router.</summary>
internal sealed partial class HubOutboundChannel(
    IHubContext<AssistantHub> hub,
    HubPresence presence,
    INotificationStore notifications,
    IOptions<DeliveryOptions> options,
    TimeProvider clock,
    ILogger<HubOutboundChannel> logger) : IOutboundChannel
{
    public ChannelId Id => ChannelId.Hub;

    public ChannelCapabilities Capabilities { get; } = new()
    {
        SupportsInbound = false,
        SupportsNotificationActions = true,
        SupportsSilentDelivery = true,
        RequiresAck = true,
        MaxTextLength = 4000,
        MaxButtons = 8,
        MaxButtonLabelLength = 40,
        MaxActionPayloadBytes = 64,
        TextFormat = ChannelTextFormat.Plain,
    };

    public ValueTask<ChannelReachability> GetReachabilityAsync(Guid userId, CancellationToken ct)
    {
        if (!presence.IsReachable(userId))
        {
            return ValueTask.FromResult(new ChannelReachability(
                Reachable: false,
                TargetRef: null,
                InsideServiceWindow: true,
                Reason: "not_visible"));
        }

        return ValueTask.FromResult(new ChannelReachability(
            Reachable: true,
            TargetRef: $"hub:{userId:D}",
            InsideServiceWindow: true,
            Reason: null));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Outbound send returns a transient result instead of throwing on a hub failure.")]
    public async ValueTask<OutboundResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        if (InvalidActions(message) is { } invalid)
        {
            LogActionsInvalid(logger);
            return invalid;
        }

        if (message.NotificationId is not { } notificationId)
        {
            return new OutboundResult.PermanentFailure("notification_required", DeactivateTarget: false);
        }

        var stored = await notifications.FindAsync(notificationId, ct);
        if (stored is null)
        {
            return new OutboundResult.PermanentFailure("notification_missing", DeactivateTarget: false);
        }

        var actions = (message.Actions ?? [])
            .Select(action => new NotificationActionPayload(action.ActionId, action.Label))
            .ToArray();
        var payload = new NotificationPayload(
            stored.Id,
            message.MessageId,
            NotificationKindNames.ToWire(stored.Kind),
            message.Title ?? stored.Title,
            message.Text,
            actions,
            stored.ConversationId,
            stored.CreatedAt);
        var envelope = MessageEnvelope.Create(
            ProtocolMessageTypes.Notification,
            message.MessageId.ToString("D"),
            payload,
            clock);

        try
        {
            await hub.Clients
                .Group(UserGroups.For(message.UserId))
                .SendAsync("Receive", envelope, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new OutboundResult.TransientFailure(exception.GetType().Name, RetryAfter: null);
        }

        return new OutboundResult.AwaitingAck(options.Value.HubAckTimeout);
    }

    private OutboundResult.PermanentFailure? InvalidActions(OutboundMessage message)
    {
        var actions = message.Actions;
        if (actions is not { Count: > 0 })
        {
            return null;
        }

        if (actions.Count > Capabilities.MaxButtons)
        {
            return new OutboundResult.PermanentFailure("actions_invalid", DeactivateTarget: false);
        }

        foreach (var action in actions)
        {
            if (string.IsNullOrEmpty(action.ActionId)
                || Encoding.UTF8.GetByteCount(action.ActionId) > Capabilities.MaxActionPayloadBytes)
            {
                return new OutboundResult.PermanentFailure("actions_invalid", DeactivateTarget: false);
            }
        }

        return null;
    }

    [LoggerMessage(
        EventId = 760,
        Level = LogLevel.Error,
        Message = "Hub outbound rejected invalid actions.")]
    private static partial void LogActionsInvalid(ILogger logger);
}
