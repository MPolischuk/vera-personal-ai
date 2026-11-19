using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class ChannelLinkTokenConfiguration : IEntityTypeConfiguration<ChannelLinkTokenEntity>
{
    public void Configure(EntityTypeBuilder<ChannelLinkTokenEntity> builder)
    {
        builder.ToTable("channel_link_tokens");
        builder.HasKey(t => t.TokenHash);

        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Channel).HasMaxLength(50).IsRequired();

        builder.HasIndex(t => t.UserId);
        builder.HasIndex(t => t.ExpiresAt);
    }
}
