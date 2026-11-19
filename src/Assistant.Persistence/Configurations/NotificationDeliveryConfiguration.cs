using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDeliveryEntity>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryEntity> builder)
    {
        builder.ToTable("notification_deliveries", table => table.HasCheckConstraint(
            "ck_notification_deliveries_result",
            "result IN ('pending', 'delivered', 'awaiting_ack', 'acked', 'ack_timeout', 'transient', 'permanent', 'not_applicable')"));
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Channel).HasMaxLength(50).IsRequired();
        builder.Property(d => d.TargetRefHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Result).HasMaxLength(50).IsRequired();
        builder.Property(d => d.ErrorCode).HasMaxLength(100);
        builder.Property(d => d.ExternalId).HasMaxLength(200);

        builder.HasIndex(d => new { d.NotificationId, d.Attempt });
        builder.HasIndex(d => d.CreatedAt);
    }
}
