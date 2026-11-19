using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class PendingConfirmationConfiguration : IEntityTypeConfiguration<PendingConfirmationEntity>
{
    public void Configure(EntityTypeBuilder<PendingConfirmationEntity> builder)
    {
        builder.ToTable("pending_confirmations");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.ToolName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Prompt).IsRequired();
        builder.Property(p => p.Arguments).HasColumnType("jsonb");
        builder.Property(p => p.ResolvedOutcome).HasMaxLength(32);
        builder.Property(p => p.TurnContaminated).IsRequired().HasDefaultValue(false);

        builder.HasOne(p => p.Conversation)
            .WithMany(c => c.PendingConfirmations)
            .HasForeignKey(p => p.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
