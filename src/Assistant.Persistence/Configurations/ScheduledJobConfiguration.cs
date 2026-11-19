using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJobEntity>
{
    public void Configure(EntityTypeBuilder<ScheduledJobEntity> builder)
    {
        builder.ToTable("scheduled_jobs", table => table.HasCheckConstraint(
            "ck_scheduled_jobs_status",
            "status IN ('scheduled', 'running', 'succeeded', 'failed', 'cancelled')"));
        builder.HasKey(job => job.Id);

        builder.Property(job => job.Kind).HasMaxLength(200).IsRequired();
        builder.Property(job => job.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(job => job.IdempotencyKey).HasMaxLength(300);
        builder.Property(job => job.Status).HasMaxLength(20).IsRequired();
        builder.Property(job => job.LockedBy).HasMaxLength(200);
        builder.Property(job => job.LastError).HasMaxLength(500);

        builder.HasIndex(job => new { job.Status, job.RunAt })
            .HasDatabaseName("ix_scheduled_jobs_status_run_at")
            .HasFilter("status = 'scheduled'");

        builder.HasIndex(job => job.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_scheduled_jobs_idempotency_key")
            .HasFilter("idempotency_key IS NOT NULL");
    }
}
