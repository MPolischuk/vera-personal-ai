namespace Assistant.Persistence.Entities;

internal sealed class UserEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ConversationEntity> Conversations { get; } = [];
    public ICollection<PersonalityProfileEntity> PersonalityProfiles { get; } = [];
    public ICollection<AuditLogEntity> AuditEntries { get; } = [];
    public ICollection<MemoryEntity> Memories { get; } = [];
    public ICollection<PreferenceEntity> Preferences { get; } = [];
    public ICollection<SkillProposalEntity> SkillProposals { get; } = [];
}
