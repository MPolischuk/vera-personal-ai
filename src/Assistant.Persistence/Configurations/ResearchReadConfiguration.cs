using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class ResearchReadConfiguration : IEntityTypeConfiguration<ResearchReadEntity>
{
    public void Configure(EntityTypeBuilder<ResearchReadEntity> builder)
    {
        builder.ToTable("research_reads");
        builder.HasKey(read => read.Id);
        builder.Property(read => read.Summary).IsRequired();
        builder.Property(read => read.Claims).HasColumnType("jsonb").IsRequired();
        builder.Property(read => read.SourceUrl).IsRequired();
        builder.HasIndex(read => new { read.UserId, read.RetrievedAt });

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(read => read.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConversationEntity>()
            .WithMany()
            .HasForeignKey(read => read.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
