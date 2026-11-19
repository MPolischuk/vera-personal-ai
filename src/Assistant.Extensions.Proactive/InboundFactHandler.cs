using Assistant.Abstractions.Events;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Webhooks;

namespace Assistant.Extensions.Proactive;

/// <summary>
/// Publishes the saved text of each active <c>inbound.fact</c> intent.
/// Does not call the model, does not write memory, and does not copy the transcript.
/// </summary>
public sealed class InboundFactHandler(
    IConditionalIntentStore intents,
    INotificationPublisher notifications) : IDomainEventHandler<InboundFactReceived>
{
    public const string Title = "Intención";

    public async ValueTask HandleAsync(InboundFactReceived domainEvent, CancellationToken ct)
    {
        var rows = await intents.ListAsync(domainEvent.UserId, ct);
        foreach (var intent in rows)
        {
            if (intent.Status != ConditionalIntentStatuses.Active
                || !ConditionalIntentEvents.IsAllowed(intent.EventName))
            {
                continue;
            }

            await notifications.PublishAsync(
                new NotificationRequest(
                    domainEvent.UserId,
                    NotificationKind.System,
                    FormattableString.Invariant($"intent:{intent.Id:D}:{domainEvent.DeliveryId}"),
                    Title,
                    intent.Text,
                    SourceType: "conditional_intent",
                    SourceId: intent.Id.ToString("D"),
                    Trust: NotificationContentTrust.Trusted,
                    AllowDuringQuietHours: false),
                ct);
        }
    }
}
