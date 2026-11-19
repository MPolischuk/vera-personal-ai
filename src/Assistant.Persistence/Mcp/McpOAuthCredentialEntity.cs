namespace Assistant.Persistence.Mcp;

internal sealed class McpOAuthCredentialEntity
{
    public Guid UserId { get; set; }

    public string ServerId { get; set; } = string.Empty;

    public string RefreshProtected { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
