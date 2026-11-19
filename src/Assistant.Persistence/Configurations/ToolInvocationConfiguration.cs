using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class ToolInvocationConfiguration : IEntityTypeConfiguration<ToolInvocationEntity>
{
    public void Configure(EntityTypeBuilder<ToolInvocationEntity> builder)
    {
        builder.ToTable("tool_invocations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ToolName).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ExtensionName).HasMaxLength(100).IsRequired();
        // D10: text, not int. Permission enums live in Abstractions (PT-03);
        // here they persist as an opaque string until a store exists.
        builder.Property(t => t.PermissionLevel).HasMaxLength(32).IsRequired();
        builder.Property(t => t.PermissionOutcome).HasMaxLength(32).IsRequired();
        builder.Property(t => t.Arguments).HasColumnType("jsonb");
        builder.Property(t => t.Result).HasColumnType("jsonb");

        builder.HasOne(t => t.Message)
            .WithMany(m => m.ToolInvocations)
            .HasForeignKey(t => t.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
