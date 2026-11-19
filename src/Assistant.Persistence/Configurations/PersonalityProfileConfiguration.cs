using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class PersonalityProfileConfiguration : IEntityTypeConfiguration<PersonalityProfileEntity>
{
    public void Configure(EntityTypeBuilder<PersonalityProfileEntity> builder)
    {
        builder.ToTable("personality_profiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Definition).HasColumnType("jsonb");

        builder.HasOne(p => p.User)
            .WithMany(u => u.PersonalityProfiles)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // D13: one active profile per user. Name is set by hand because the convention
        // would derive ix_personality_profiles_user_id, which already exists.
        builder.HasIndex(p => p.UserId)
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("ux_personality_profiles_active_per_user");
    }
}
