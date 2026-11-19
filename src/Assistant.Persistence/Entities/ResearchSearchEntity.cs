namespace Assistant.Persistence.Entities;

internal sealed class ResearchSearchEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
