namespace Assistant.Persistence.Entities;

internal interface IHasCreatedAt
{
    public DateTimeOffset CreatedAt { get; set; }
}

internal interface IHasUpdatedAt
{
    public DateTimeOffset UpdatedAt { get; set; }
}
