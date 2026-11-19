using Assistant.Abstractions.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Webhooks;

internal sealed class InboundWebhookDeliveryConfiguration : IEntityTypeConfiguration<InboundWebhookDeliveryEntity>
{
    public void Configure(EntityTypeBuilder<InboundWebhookDeliveryEntity> builder)
    {
        builder.ToTable("inbound_webhook_deliveries");
        builder.HasKey(delivery => new { delivery.Source, delivery.DeliveryId });
        builder.Property(delivery => delivery.Source).HasMaxLength(InboundWebhookLimits.MaxSourceLength).IsRequired();
        builder.Property(delivery => delivery.DeliveryId).HasMaxLength(InboundWebhookLimits.MaxDeliveryIdLength).IsRequired();
        builder.Property(delivery => delivery.ReceivedAt).IsRequired();
    }
}
