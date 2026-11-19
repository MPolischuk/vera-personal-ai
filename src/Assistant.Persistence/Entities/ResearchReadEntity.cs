namespace Assistant.Persistence.Entities;

internal sealed class ResearchReadEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ConversationId { get; set; }

    public Guid TurnId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string Claims { get; set; } = "[]";

    public string SourceUrl { get; set; } = string.Empty;

    public DateTimeOffset RetrievedAt { get; set; }
}
