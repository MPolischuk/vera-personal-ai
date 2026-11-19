using Assistant.Core.Skills;

namespace Assistant.Persistence.Entities;

internal sealed class SkillProposalEntity : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public SkillProposalStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public UserEntity User { get; set; } = null!;
}
