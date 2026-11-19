using ModelContextProtocol.Protocol;

namespace Assistant.Mcp.Coolify;

internal interface ICoolifyApi
{
    public Task<CallToolResult> GetStatusAsync(string? app, CancellationToken cancellationToken);

    public Task<CallToolResult> GetLogsAsync(string? app, int? lines, CancellationToken cancellationToken);

    public Task<CallToolResult> ControlAsync(string? app, string? action, CancellationToken cancellationToken);
}
