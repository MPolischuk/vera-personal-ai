using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<DeviceEntity>
{
    public void Configure(EntityTypeBuilder<DeviceEntity> builder)
    {
        builder.ToTable("devices");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasMaxLength(80).IsRequired();

        builder.HasIndex(d => d.RefreshTokenHash).IsUnique();
    }
}
