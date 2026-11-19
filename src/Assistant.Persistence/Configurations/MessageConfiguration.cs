using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<MessageEntity>
{
    public void Configure(EntityTypeBuilder<MessageEntity> builder)
    {
        // D4: table name is explicit because the entity carries the Entity suffix.
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);

        // D10: role is stored as text so the table can be read without the code beside it.
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.Metadata).HasColumnType("jsonb");

        // D7: the unique index is what turns a race into a visible error.
        builder.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique();

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(m => m.DeletedAt == null);
    }
}
