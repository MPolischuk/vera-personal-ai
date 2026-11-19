using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class ChannelPeerConfiguration : IEntityTypeConfiguration<ChannelPeerEntity>
{
    public void Configure(EntityTypeBuilder<ChannelPeerEntity> builder)
    {
        builder.ToTable("channel_peers", table => table.HasCheckConstraint(
            "ck_channel_peers_status",
            "status IN ('pending', 'approved', 'revoked', 'blocked')"));
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Channel).HasMaxLength(50).IsRequired();
        builder.Property(p => p.ExternalPeerId).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Status).HasMaxLength(20).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(200);
        builder.Property(p => p.Username).HasMaxLength(100);
        builder.Property(p => p.PairingCodeHash).HasMaxLength(64);

        builder.HasIndex(p => new { p.Channel, p.ExternalPeerId }).IsUnique();
        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.ConversationId);

        builder.HasOne<ConversationEntity>()
            .WithMany()
            .HasForeignKey(p => p.ConversationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
