using Assistant.Abstractions.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Mcp;

internal sealed class ManifestToolClassification(
    IOptions<McpOptions> options,
    ILogger<ManifestToolClassification> logger) : IMcpToolClassification
{
    private static readonly McpToolPolicy Undeclared = new(
        PermissionLevel.Undeclared,
        OutputTrust.Untrusted,
        false);

    public McpToolPolicy PolicyFor(string serverId, string toolName)
    {
        var tool = FindTool(serverId, toolName);
        if (tool is null)
        {
            McpLog.ToolPolicyUnusable(logger, toolName, serverId);
            return Undeclared;
        }

        if (!TryParseName(tool.PermissionLevel, PermissionLevel.Undeclared, out PermissionLevel level)
            || !TryParseName(tool.OutputTrust, OutputTrust.Untrusted, out OutputTrust trust))
        {
            McpLog.ToolPolicyUnusable(logger, toolName, serverId);
            return Undeclared;
        }

        return new McpToolPolicy(level, trust, tool.WritesDurableState);
    }

    private McpToolManifestOptions? FindTool(string serverId, string toolName)
    {
        foreach (var server in options.Value.Servers)
        {
            if (!string.Equals(server.Id, serverId, StringComparison.Ordinal))
            {
                continue;
            }

            if (server.Tools is not null && server.Tools.TryGetValue(toolName, out var tool))
            {
                return tool;
            }

            return null;
        }

        return null;
    }

    private static bool TryParseName<TEnum>(string? text, TEnum whenAbsent, out TEnum value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = whenAbsent;
            return true;
        }

        if (!char.IsAsciiLetter(text[0]))
        {
            value = default;
            return false;
        }

        return Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value);
    }
}
