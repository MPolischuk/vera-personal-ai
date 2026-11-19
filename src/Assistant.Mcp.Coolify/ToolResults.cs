using ModelContextProtocol.Protocol;

namespace Assistant.Mcp.Coolify;

internal static class ToolResults
{
    public static CallToolResult Ok(string text) => new()
    {
        IsError = false,
        Content = [new TextContentBlock { Text = text }],
    };

    public static CallToolResult Fail(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }],
    };
}
