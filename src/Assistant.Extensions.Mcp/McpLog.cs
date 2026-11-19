using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Mcp;

internal static partial class McpLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Skipping MCP server with invalid id '{ServerId}'. Expected ^[a-z][a-z0-9_]{{0,19}}$.")]
    public static partial void InvalidServerId(ILogger logger, string serverId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Skipping MCP server '{ServerId}': transport '{Transport}' is missing required fields.")]
    public static partial void MissingTransportFields(ILogger logger, string serverId, string transport);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Omitting MCP tool '{ToolName}' from server '{ServerId}': projected name is invalid or too long.")]
    public static partial void OmittingTool(ILogger logger, string toolName, string serverId);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "MCP server '{ServerId}' connected with {ToolCount} tool(s).")]
    public static partial void ServerConnected(ILogger logger, string serverId, int toolCount);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "MCP server '{ServerId}' failed to connect; it contributes zero tools.")]
    public static partial void ServerConnectFailed(ILogger logger, Exception exception, string serverId);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Skipping MCP server '{ServerId}': enabled is false.")]
    public static partial void ServerDisabled(ILogger logger, string serverId);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Warning,
        Message = "Skipping MCP server '{ServerId}': {Detail}.")]
    public static partial void SecretRejected(ILogger logger, string serverId, string detail);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Warning,
        Message = "Extension manifest '{Path}' is invalid; loading zero servers.")]
    public static partial void ManifestInvalid(ILogger logger, string path);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Extension manifest '{Path}' is empty; loading zero servers.")]
    public static partial void ManifestEmpty(ILogger logger, string path);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "Extension manifest '{Path}' has a duplicate server id; loading zero servers.")]
    public static partial void ManifestDuplicateServerId(ILogger logger, string path);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Warning,
        Message = "MCP tool '{ToolName}' on server '{ServerId}' has no usable policy; treating it as undeclared and untrusted.")]
    public static partial void ToolPolicyUnusable(ILogger logger, string toolName, string serverId);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Information,
        Message = "Applying MCP server '{ServerId}' after a management change.")]
    public static partial void ServerApplying(ILogger logger, string serverId);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Warning,
        Message = "MCP server '{ServerId}' failed to list prompts; its tools stay available.")]
    public static partial void PromptListFailed(ILogger logger, Exception exception, string serverId);

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Warning,
        Message = "Omitting MCP prompt '{PromptName}' from server '{ServerId}': the name is invalid or duplicated.")]
    public static partial void OmittingPrompt(ILogger logger, string promptName, string serverId);

    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Warning,
        Message = "Omitting calendar write tool '{ToolName}' from server '{ServerId}'.")]
    public static partial void OmittingCalendarWrite(ILogger logger, string toolName, string serverId);

    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Warning,
        Message = "MCP server '{ServerId}' uses oauth and has no access token; it stays disconnected.")]
    public static partial void OAuthDisconnected(ILogger logger, string serverId);
}
