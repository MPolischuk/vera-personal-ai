using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class ResearchSearchConfiguration : IEntityTypeConfiguration<ResearchSearchEntity>
{
    public void Configure(EntityTypeBuilder<ResearchSearchEntity> builder)
    {
        builder.ToTable("research_searches");
        builder.HasKey(search => search.Id);
        builder.HasIndex(search => new { search.UserId, search.CreatedAt });

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(search => search.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
