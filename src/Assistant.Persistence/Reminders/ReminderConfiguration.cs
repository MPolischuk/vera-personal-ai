using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Reminders;

internal sealed class ReminderConfiguration : IEntityTypeConfiguration<ReminderEntity>
{
    public void Configure(EntityTypeBuilder<ReminderEntity> builder)
    {
        builder.ToTable("reminders", table =>
        {
            table.HasCheckConstraint(
                "ck_reminders_status",
                "status IN ('active', 'completed', 'cancelled', 'missed')");
            table.HasCheckConstraint(
                "ck_reminders_text_length",
                "char_length(text) BETWEEN 1 AND 500");
        });
        builder.HasKey(reminder => reminder.Id);

        builder.Property(reminder => reminder.Text).HasColumnType("text").IsRequired();
        builder.Property(reminder => reminder.Status).HasMaxLength(20).IsRequired();
        builder.Property(reminder => reminder.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(reminder => reminder.LocalStart).HasColumnType("timestamp without time zone");
        builder.Property(reminder => reminder.RecurrenceRule).HasMaxLength(300);
        builder.Property(reminder => reminder.OriginChannel).HasMaxLength(50).IsRequired();
        builder.Property(reminder => reminder.Provenance).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(reminder => new { reminder.UserId, reminder.Status, reminder.NextOccurrenceAt })
            .HasDatabaseName("ix_reminders_user_id_status_next_occurrence_at");

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(reminder => reminder.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConversationEntity>()
            .WithMany()
            .HasForeignKey(reminder => reminder.OriginConversationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
