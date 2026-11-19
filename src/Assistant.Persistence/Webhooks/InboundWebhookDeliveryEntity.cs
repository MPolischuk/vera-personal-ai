namespace Assistant.Persistence.Webhooks;

internal sealed class InboundWebhookDeliveryEntity
{
    public string Source { get; set; } = string.Empty;

    public string DeliveryId { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }
}
