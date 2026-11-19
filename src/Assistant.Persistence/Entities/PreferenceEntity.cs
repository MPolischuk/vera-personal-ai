using Assistant.Core.Memory;

namespace Assistant.Persistence.Entities;

internal sealed class PreferenceEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public WriteProvenance WriteProvenance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public UserEntity User { get; set; } = null!;
}
