using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTaskEntity>
{
    public void Configure(EntityTypeBuilder<AgentTaskEntity> builder)
    {
        builder.ToTable("agent_tasks", table =>
        {
            table.HasCheckConstraint(
                "ck_agent_tasks_state",
                "state IN ('submitted', 'working', 'input-required', 'auth-required', 'completed', 'failed', 'canceled', 'rejected')");
            table.HasCheckConstraint(
                "ck_agent_tasks_failure_reason",
                "failure_reason IS NULL OR failure_reason IN ('interrupted', 'provider_budget', 'network', 'cost_cap', 'deadline', 'unknown_kind', 'worker_error')");
            table.HasCheckConstraint(
                "ck_agent_tasks_instruction_length",
                "char_length(instruction) BETWEEN 1 AND 8000");
        });
        builder.HasKey(task => task.Id);

        builder.Property(task => task.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(task => task.Kind).HasMaxLength(64).IsRequired();
        builder.Property(task => task.Title).HasMaxLength(60).IsRequired();
        builder.Property(task => task.Instruction).HasColumnType("text").IsRequired();
        builder.Property(task => task.State).HasMaxLength(32).IsRequired();
        builder.Property(task => task.FailureReason).HasMaxLength(64);
        builder.Property(task => task.PartialResult).HasColumnType("text");
        builder.Property(task => task.CostUsd).HasPrecision(12, 6).HasDefaultValue(0m);
        builder.Property(task => task.CostCapUsd).HasPrecision(12, 6);
        builder.Property(task => task.ExternalTaskId).HasMaxLength(200);
        builder.Property(task => task.NoticeArmed).HasDefaultValue(true);
        builder.Property(task => task.LockedBy).HasMaxLength(128);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(task => task.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConversationEntity>()
            .WithMany()
            .HasForeignKey(task => task.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(task => task.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_agent_tasks_idempotency_key");

        builder.HasIndex(task => task.ExternalTaskId)
            .IsUnique()
            .HasDatabaseName("ux_agent_tasks_external_task_id")
            .HasFilter("external_task_id IS NOT NULL");

        builder.HasIndex(task => new { task.State, task.LockedUntil })
            .HasDatabaseName("ix_agent_tasks_state_locked_until")
            .HasFilter("state = 'working'");
    }
}
