using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecordEntity>
{
    public void Configure(EntityTypeBuilder<UsageRecordEntity> builder)
    {
        builder.ToTable("usage_records");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Provider).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Model).HasMaxLength(100).IsRequired();
        builder.Property(u => u.TaskKind).HasMaxLength(32).IsRequired();
        builder.Property(u => u.EstimatedCostUsd).HasPrecision(18, 6);

        builder.HasIndex(u => u.CreatedAt);

        builder.HasOne(u => u.Conversation)
            .WithMany(c => c.UsageRecords)
            .HasForeignKey(u => u.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.Message)
            .WithMany(m => m.UsageRecords)
            .HasForeignKey(u => u.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
