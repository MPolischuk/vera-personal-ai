using System.Text.Json;
using Assistant.Core.Memory;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class PreferenceConfiguration : IEntityTypeConfiguration<PreferenceEntity>
{
    public void Configure(EntityTypeBuilder<PreferenceEntity> builder)
    {
        builder.ToTable("preferences");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Key).IsRequired();
        builder.Property(p => p.Value).IsRequired();
        builder.Property(p => p.WriteProvenance)
            .HasConversion(
                value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()),
                value => Enum.Parse<WriteProvenance>(
                    value.Replace("_", "", StringComparison.Ordinal), ignoreCase: true))
            .HasMaxLength(40)
            .IsRequired()
            .HasDefaultValue(WriteProvenance.ModelClean);

        builder.HasIndex(p => new { p.UserId, p.Key }).IsUnique();

        builder.HasOne(p => p.User)
            .WithMany(u => u.Preferences)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
