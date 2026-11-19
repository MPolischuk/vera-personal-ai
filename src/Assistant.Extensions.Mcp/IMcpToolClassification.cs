using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Mcp;

public readonly record struct McpToolPolicy(
    PermissionLevel Level,
    OutputTrust OutputTrust,
    bool WritesDurableState);

public interface IMcpToolClassification
{
    public McpToolPolicy PolicyFor(string serverId, string toolName);
}
