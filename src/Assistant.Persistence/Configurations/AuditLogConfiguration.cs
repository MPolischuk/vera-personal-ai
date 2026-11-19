using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntity>
{
    public void Configure(EntityTypeBuilder<AuditLogEntity> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EventType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.SubjectType).HasMaxLength(100);
        builder.Property(a => a.Payload).HasColumnType("jsonb");

        builder.HasOne(a => a.User)
            .WithMany(u => u.AuditEntries)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
