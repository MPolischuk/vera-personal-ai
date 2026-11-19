using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Assistant.Mcp.Coolify;

[McpServerToolType]
internal sealed class CoolifyTools(ICoolifyApi api)
{
    [McpServerTool(Name = "status", ReadOnly = true, OpenWorld = false)]
    [Description("Lee el estado de una aplicación de Coolify de la lista permitida: nombre, estado, dominio y último deploy.")]
    public Task<CallToolResult> Status(
        [Description("Nombre permitido de la aplicación.")] string app,
        CancellationToken cancellationToken)
        => api.GetStatusAsync(app, cancellationToken);

    [McpServerTool(Name = "logs", ReadOnly = true, OpenWorld = false)]
    [Description("Lee los logs recientes de una aplicación de Coolify de la lista permitida.")]
    public Task<CallToolResult> Logs(
        [Description("Nombre permitido de la aplicación.")] string app,
        CancellationToken cancellationToken,
        [Description("Cantidad de líneas. Por defecto 100, máximo 500.")] int? lines = null)
        => api.GetLogsAsync(app, lines, cancellationToken);

    [McpServerTool(Name = "control", OpenWorld = false)]
    [Description("Arranca, detiene, reinicia o despliega una aplicación de Coolify de la lista permitida.")]
    public Task<CallToolResult> Control(
        [Description("Nombre permitido de la aplicación.")] string app,
        [Description("Acción: start, stop, restart o deploy.")] string action,
        CancellationToken cancellationToken)
        => api.ControlAsync(app, action, cancellationToken);
}
