using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    public void Configure(EntityTypeBuilder<NotificationEntity> builder)
    {
        builder.ToTable("notifications", table =>
        {
            table.HasCheckConstraint(
                "ck_notifications_status",
                "status IN ('pending', 'dispatching', 'deferred', 'delivered', 'acted', 'failed', 'cancelled')");
            table.HasCheckConstraint(
                "ck_notifications_trust",
                "trust IN ('trusted', 'untrusted')");
        });
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Kind).HasMaxLength(50).IsRequired();
        builder.Property(n => n.DedupKey).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(5000);
        builder.Property(n => n.Trust).HasMaxLength(20).IsRequired();
        builder.Property(n => n.PreferredChannel).HasMaxLength(50);
        builder.Property(n => n.Actions).HasColumnType("jsonb");
        builder.Property(n => n.SourceType).HasMaxLength(100);
        builder.Property(n => n.SourceId).HasMaxLength(200);
        builder.Property(n => n.Status).HasMaxLength(20).IsRequired();
        builder.Property(n => n.DeliveredChannel).HasMaxLength(50);
        builder.Property(n => n.ActionTaken).HasMaxLength(100);

        builder.HasIndex(n => new { n.UserId, n.DedupKey }).IsUnique();
        builder.HasIndex(n => new { n.UserId, n.Status, n.CreatedAt });
        builder.HasIndex(n => new { n.Status, n.CreatedAt });
    }
}
