using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Proactive;

internal sealed class GoalConfiguration : IEntityTypeConfiguration<GoalEntity>
{
    public void Configure(EntityTypeBuilder<GoalEntity> builder)
    {
        builder.ToTable("goals", table =>
        {
            table.HasCheckConstraint("ck_goals_text_length", "char_length(text) BETWEEN 1 AND 200");
            table.HasCheckConstraint("ck_goals_status", "status IN ('active', 'done')");
        });
        builder.HasKey(goal => goal.Id);
        builder.Property(goal => goal.Text).HasMaxLength(200).IsRequired();
        builder.Property(goal => goal.Status).HasMaxLength(16).IsRequired();
        builder.HasOne<UserEntity>().WithMany().HasForeignKey(goal => goal.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(goal => goal.UserId);
    }
}

internal sealed class PhraseShortcutConfiguration : IEntityTypeConfiguration<PhraseShortcutEntity>
{
    public void Configure(EntityTypeBuilder<PhraseShortcutEntity> builder)
    {
        builder.ToTable("phrase_shortcuts", table =>
        {
            table.HasCheckConstraint(
                "ck_phrase_shortcuts_phrase_length",
                "char_length(phrase_normalized) BETWEEN 1 AND 40");
            table.HasCheckConstraint("ck_phrase_shortcuts_action", "char_length(action) BETWEEN 1 AND 64");
        });
        builder.HasKey(shortcut => shortcut.Id);
        builder.Property(shortcut => shortcut.PhraseNormalized).HasMaxLength(40).IsRequired();
        builder.Property(shortcut => shortcut.Action).HasMaxLength(64).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(shortcut => shortcut.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(shortcut => new { shortcut.UserId, shortcut.PhraseNormalized })
            .IsUnique()
            .HasDatabaseName("ux_phrase_shortcuts_user_phrase");
    }
}

internal sealed class AutomationConfiguration : IEntityTypeConfiguration<AutomationEntity>
{
    public void Configure(EntityTypeBuilder<AutomationEntity> builder)
    {
        builder.ToTable("automations", table =>
        {
            table.HasCheckConstraint(
                "ck_automations_cadence",
                "cadence IN ('once', 'interval', 'daily', 'system_daily')");
            table.HasCheckConstraint("ck_automations_status", "status IN ('active', 'paused')");
            table.HasCheckConstraint("ck_automations_title_length", "char_length(title) BETWEEN 1 AND 80");
            table.HasCheckConstraint(
                "ck_automations_instruction_length",
                "char_length(instruction) BETWEEN 1 AND 2000");
            table.HasCheckConstraint(
                "ck_automations_interval",
                "interval_minutes IS NULL OR interval_minutes >= 30");
            table.HasCheckConstraint(
                "ck_automations_local_time",
                "local_time IS NULL OR local_time ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'");
        });
        builder.HasKey(automation => automation.Id);
        builder.Property(automation => automation.Title).HasMaxLength(80).IsRequired();
        builder.Property(automation => automation.Instruction).HasMaxLength(2000).IsRequired();
        builder.Property(automation => automation.Cadence).HasMaxLength(32).IsRequired();
        builder.Property(automation => automation.LocalTime).HasMaxLength(5);
        builder.Property(automation => automation.Status).HasMaxLength(16).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(automation => automation.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConversationEntity>()
            .WithMany()
            .HasForeignKey(automation => automation.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(automation => automation.UserId);
    }
}

internal sealed class BriefingConfiguration : IEntityTypeConfiguration<BriefingEntity>
{
    public void Configure(EntityTypeBuilder<BriefingEntity> builder)
    {
        builder.ToTable("briefings", table =>
        {
            table.HasCheckConstraint("ck_briefings_summary_length", "char_length(summary) BETWEEN 1 AND 1000");
        });
        builder.HasKey(briefing => briefing.Id);
        builder.Property(briefing => briefing.Summary).HasMaxLength(1000).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(briefing => briefing.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(briefing => new { briefing.UserId, briefing.LocalDate })
            .IsUnique()
            .HasDatabaseName("ux_briefings_user_local_date");
        builder.HasMany(briefing => briefing.Items)
            .WithOne(item => item.Briefing)
            .HasForeignKey(item => item.BriefingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BriefingItemConfiguration : IEntityTypeConfiguration<BriefingItemEntity>
{
    public void Configure(EntityTypeBuilder<BriefingItemEntity> builder)
    {
        builder.ToTable("briefing_items", table =>
        {
            table.HasCheckConstraint("ck_briefing_items_block", "block IN ('today', 'ahead')");
            table.HasCheckConstraint("ck_briefing_items_triage", "triage IN ('needs_you', 'can_wait')");
            table.HasCheckConstraint("ck_briefing_items_reason_length", "char_length(reason) BETWEEN 1 AND 120");
            table.HasCheckConstraint("ck_briefing_items_title_length", "char_length(title) BETWEEN 1 AND 200");
            table.HasCheckConstraint(
                "ck_briefing_items_feedback",
                "feedback IS NULL OR feedback IN ('done', 'dismissed', 'helpful', 'not_helpful')");
            table.HasCheckConstraint(
                "ck_briefing_items_ahead_goal",
                "block <> 'ahead' OR source = 'goal'");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Block).HasMaxLength(16).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Reason).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Triage).HasMaxLength(16).IsRequired();
        builder.Property(item => item.Source).HasMaxLength(40).IsRequired();
        builder.Property(item => item.SourceRef).HasMaxLength(80);
        builder.Property(item => item.Feedback).HasMaxLength(16);
        builder.HasIndex(item => item.BriefingId);
    }
}

internal sealed class SuggestionAuditConfiguration : IEntityTypeConfiguration<SuggestionAuditEntity>
{
    public void Configure(EntityTypeBuilder<SuggestionAuditEntity> builder)
    {
        builder.ToTable("suggestion_audits", table =>
        {
            table.HasCheckConstraint(
                "ck_suggestion_audits_outcome",
                "outcome IN ('delivered', 'discarded')");
            table.HasCheckConstraint(
                "ck_suggestion_audits_reason_length",
                "char_length(reason) BETWEEN 1 AND 200");
        });
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Outcome).HasMaxLength(16).IsRequired();
        builder.Property(audit => audit.Reason).HasMaxLength(200).IsRequired();
        builder.Property(audit => audit.SourceRef).HasMaxLength(80);
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(audit => audit.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(audit => new { audit.UserId, audit.LocalDate });
    }
}

internal sealed class ProposalConfiguration : IEntityTypeConfiguration<ProposalEntity>
{
    public void Configure(EntityTypeBuilder<ProposalEntity> builder)
    {
        builder.ToTable("proactive_proposals", table =>
        {
            table.HasCheckConstraint(
                "ck_proactive_proposals_origin",
                "origin IN ('heartbeat', 'consolidation')");
            table.HasCheckConstraint(
                "ck_proactive_proposals_status",
                "status IN ('pending', 'approved', 'rejected')");
            table.HasCheckConstraint(
                "ck_proactive_proposals_tool_name_length",
                "(origin = 'consolidation' AND char_length(tool_name) = 0) OR (origin <> 'consolidation' AND char_length(tool_name) BETWEEN 1 AND 128)");
            table.HasCheckConstraint(
                "ck_proactive_proposals_reason_length",
                "char_length(reason) BETWEEN 1 AND 500");
        });
        builder.HasKey(proposal => proposal.Id);
        builder.Property(proposal => proposal.Origin).HasMaxLength(32).IsRequired();
        builder.Property(proposal => proposal.ToolName).HasMaxLength(128).IsRequired();
        builder.Property(proposal => proposal.ArgumentsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(proposal => proposal.Reason).HasMaxLength(500).IsRequired();
        builder.Property(proposal => proposal.Status).HasMaxLength(16).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(proposal => proposal.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(proposal => proposal.UserId);
    }
}

internal sealed class HeartbeatCheckConfiguration : IEntityTypeConfiguration<HeartbeatCheckEntity>
{
    public void Configure(EntityTypeBuilder<HeartbeatCheckEntity> builder)
    {
        builder.ToTable("heartbeat_checks", table =>
        {
            table.HasCheckConstraint("ck_heartbeat_checks_text_length", "char_length(text) <= 2000");
        });
        builder.HasKey(check => check.UserId);
        builder.Property(check => check.UserId).ValueGeneratedNever();
        builder.Property(check => check.Text).HasMaxLength(2000).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(check => check.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StandingOrderConfiguration : IEntityTypeConfiguration<StandingOrderEntity>
{
    public void Configure(EntityTypeBuilder<StandingOrderEntity> builder)
    {
        builder.ToTable("standing_orders", table =>
        {
            table.HasCheckConstraint(
                "ck_standing_orders_text_length",
                "char_length(text) BETWEEN 1 AND 500");
            table.HasCheckConstraint("ck_standing_orders_status", "status IN ('active', 'paused')");
        });
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Text).HasMaxLength(500).IsRequired();
        builder.Property(order => order.Status).HasMaxLength(16).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(order => order.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(order => order.UserId);
    }
}

internal sealed class ConditionalIntentConfiguration : IEntityTypeConfiguration<ConditionalIntentEntity>
{
    public void Configure(EntityTypeBuilder<ConditionalIntentEntity> builder)
    {
        builder.ToTable("conditional_intents", table =>
        {
            table.HasCheckConstraint(
                "ck_conditional_intents_text_length",
                "char_length(text) BETWEEN 1 AND 300");
            table.HasCheckConstraint(
                "ck_conditional_intents_status",
                "status IN ('active', 'paused')");
            table.HasCheckConstraint(
                "ck_conditional_intents_event_name",
                "event_name IN ('inbound.fact')");
        });
        builder.HasKey(intent => intent.Id);
        builder.Property(intent => intent.EventName).HasMaxLength(64).IsRequired();
        builder.Property(intent => intent.Text).HasMaxLength(300).IsRequired();
        builder.Property(intent => intent.Status).HasMaxLength(16).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(intent => intent.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(intent => intent.UserId);
    }
}

internal sealed class DiaryConfiguration : IEntityTypeConfiguration<DiaryEntity>
{
    public void Configure(EntityTypeBuilder<DiaryEntity> builder)
    {
        builder.ToTable("consolidation_diary", table =>
        {
            table.HasCheckConstraint(
                "ck_consolidation_diary_reason_length",
                "char_length(reason) BETWEEN 1 AND 120");
            table.HasCheckConstraint(
                "ck_consolidation_diary_outcome",
                "outcome IN ('proposed', 'approved', 'rejected_version', 'rejected_scan', 'undone')");
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Reason).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.Outcome).HasMaxLength(32).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(entry => entry.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProposalEntity>()
            .WithMany()
            .HasForeignKey(entry => entry.ProposalId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => entry.UserId);
    }
}
