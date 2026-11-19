using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class ChannelInboxConfiguration : IEntityTypeConfiguration<ChannelInboxEntity>
{
    public void Configure(EntityTypeBuilder<ChannelInboxEntity> builder)
    {
        builder.ToTable("channel_inbox", table => table.HasCheckConstraint(
            "ck_channel_inbox_status",
            "status IN ('received', 'processing', 'done', 'ignored', 'failed')"));
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).UseIdentityColumn();
        builder.Property(i => i.Channel).HasMaxLength(50).IsRequired();
        builder.Property(i => i.ExternalId).HasMaxLength(200).IsRequired();
        builder.Property(i => i.ExternalPeerId).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Payload).HasColumnType("jsonb");
        builder.Property(i => i.Status).HasMaxLength(20).IsRequired();
        builder.Property(i => i.LastError).HasMaxLength(500);

        builder.HasIndex(i => new { i.Channel, i.ExternalId }).IsUnique();
        builder.HasIndex(i => new { i.Status, i.ReceivedAt });
        builder.HasIndex(i => new { i.Channel, i.ExternalPeerId, i.ReceivedAt });
    }
}
