using Assistant.Core;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();

        // D11: constant data. DateTimeOffset.UtcNow here generates a spurious migration
        // every time someone runs "dotnet ef migrations add".
        builder.HasData(new UserEntity
        {
            Id = WellKnownIds.DefaultUser,
            DisplayName = "Usuario",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
    }
}
