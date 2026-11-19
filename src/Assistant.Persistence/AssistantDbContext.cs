using Assistant.Persistence.Entities;
using Assistant.Persistence.Mcp;
using Assistant.Persistence.Proactive;
using Assistant.Persistence.Reminders;
using Assistant.Persistence.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence;

public sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    internal DbSet<UserEntity> Users => Set<UserEntity>();
    internal DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    internal DbSet<MessageEntity> Messages => Set<MessageEntity>();
    internal DbSet<PersonalityProfileEntity> PersonalityProfiles => Set<PersonalityProfileEntity>();
    internal DbSet<ToolInvocationEntity> ToolInvocations => Set<ToolInvocationEntity>();
    internal DbSet<UsageRecordEntity> UsageRecords => Set<UsageRecordEntity>();
    internal DbSet<PendingConfirmationEntity> PendingConfirmations => Set<PendingConfirmationEntity>();
    internal DbSet<AuditLogEntity> AuditLog => Set<AuditLogEntity>();
    internal DbSet<MemoryEntity> Memories => Set<MemoryEntity>();
    internal DbSet<PreferenceEntity> Preferences => Set<PreferenceEntity>();
    internal DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    internal DbSet<SkillProposalEntity> SkillProposals => Set<SkillProposalEntity>();
    internal DbSet<ChannelPeerEntity> ChannelPeers => Set<ChannelPeerEntity>();
    internal DbSet<ChannelLinkTokenEntity> ChannelLinkTokens => Set<ChannelLinkTokenEntity>();
    internal DbSet<ChannelInboxEntity> ChannelInbox => Set<ChannelInboxEntity>();
    internal DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    internal DbSet<NotificationDeliveryEntity> NotificationDeliveries => Set<NotificationDeliveryEntity>();
    internal DbSet<PushSubscriptionEntity> PushSubscriptions => Set<PushSubscriptionEntity>();
    internal DbSet<ScheduledJobEntity> ScheduledJobs => Set<ScheduledJobEntity>();

    internal DbSet<InboundWebhookDeliveryEntity> InboundWebhookDeliveries => Set<InboundWebhookDeliveryEntity>();

    internal DbSet<ReminderEntity> Reminders => Set<ReminderEntity>();

    internal DbSet<ResearchSearchEntity> ResearchSearches => Set<ResearchSearchEntity>();

    internal DbSet<ResearchReadEntity> ResearchReads => Set<ResearchReadEntity>();

    internal DbSet<NoteEntity> Notes => Set<NoteEntity>();

    internal DbSet<NoteSourceEntity> NoteSources => Set<NoteSourceEntity>();

    internal DbSet<NoteLinkEntity> NoteLinks => Set<NoteLinkEntity>();

    internal DbSet<AgentTaskEntity> AgentTasks => Set<AgentTaskEntity>();

    internal DbSet<GoalEntity> Goals => Set<GoalEntity>();

    internal DbSet<PhraseShortcutEntity> PhraseShortcuts => Set<PhraseShortcutEntity>();

    internal DbSet<AutomationEntity> Automations => Set<AutomationEntity>();

    internal DbSet<BriefingEntity> Briefings => Set<BriefingEntity>();

    internal DbSet<BriefingItemEntity> BriefingItems => Set<BriefingItemEntity>();

    internal DbSet<McpOAuthCredentialEntity> McpOAuthCredentials => Set<McpOAuthCredentialEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssistantDbContext).Assembly);
    }
}
