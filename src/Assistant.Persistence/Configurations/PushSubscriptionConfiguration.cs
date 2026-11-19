using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscriptionEntity>
{
    public void Configure(EntityTypeBuilder<PushSubscriptionEntity> builder)
    {
        builder.ToTable("push_subscriptions", table => table.HasCheckConstraint(
            "ck_push_subscriptions_status",
            "status IN ('active', 'disabled', 'revoked')"));
        builder.HasKey(s => s.Id);

        builder.Property(s => s.EndpointHash).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Endpoint).HasMaxLength(1000).IsRequired();
        builder.Property(s => s.P256dh).HasMaxLength(200);
        builder.Property(s => s.Auth).HasMaxLength(200);
        builder.Property(s => s.VapidPublicKeyHash).HasMaxLength(64);
        builder.Property(s => s.UserAgentFamily).HasMaxLength(50);
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();

        builder.HasIndex(s => s.EndpointHash).IsUnique();
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.DeviceId);

        builder.HasOne<DeviceEntity>()
            .WithMany()
            .HasForeignKey(s => s.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
