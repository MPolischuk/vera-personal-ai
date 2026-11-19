namespace Assistant.Persistence.Entities;

internal sealed class ToolInvocationEntity : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ExtensionName { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string PermissionLevel { get; set; } = string.Empty;
    public string PermissionOutcome { get; set; } = string.Empty;
    public string? PermissionReason { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public MessageEntity Message { get; set; } = null!;
}
