using System.Security.Claims;
using Assistant.Abstractions.Proactive;
using Assistant.Api.Authentication;
using Assistant.Extensions.Mcp;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Endpoints;

internal static class ExtensionEndpoints
{
    public static IEndpointRouteBuilder MapExtensionEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/extensions", ListAsync);
        group.MapPut("/extensions/{serverId}/enabled", SetEnabledAsync);
        group.MapPut("/extensions/{serverId}/tools", ClassifyAsync);
        return group;
    }

    private static async Task<ExtensionInventoryResponse> ListAsync(
        ClaimsPrincipal user,
        IExtensionManager manager,
        IOptions<McpOptions> options,
        IMcpOAuthTokenStore tokens,
        CancellationToken ct)
    {
        var inventory = manager.List();
        var servers = new List<ExtensionServerResponse>(inventory.Servers.Count);
        foreach (var server in inventory.Servers)
        {
            bool? connected = null;
            var configured = options.Value.Servers.Find(item =>
                string.Equals(item.Id, server.Id, StringComparison.Ordinal));
            if (configured is not null
                && string.Equals(configured.Auth, "oauth", StringComparison.OrdinalIgnoreCase))
            {
                connected = await tokens.HasRefreshAsync(user.UserId(), server.Id, ct);
            }

            servers.Add(ToResponse(server, connected));
        }

        return new ExtensionInventoryResponse(servers);
    }

    private static async Task<IResult> SetEnabledAsync(
        string serverId,
        SetExtensionEnabledRequest request,
        IExtensionManager manager,
        CancellationToken ct)
    {
        var result = await manager.SetEnabledAsync(serverId, request.Enabled, ct);
        return result switch
        {
            ExtensionMutationResult.Success => Results.NoContent(),
            ExtensionMutationResult.NotFound => Results.Problem(
                title: "Extension server not found.",
                statusCode: StatusCodes.Status404NotFound),
            _ => Results.Problem(
                title: "The request is invalid.",
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static async Task<IResult> ClassifyAsync(
        string serverId,
        ClassifyExtensionToolRequest request,
        IExtensionManager manager,
        CancellationToken ct)
    {
        var result = await manager.ClassifyAsync(
            serverId,
            request.ToolName ?? string.Empty,
            request.PermissionLevel ?? string.Empty,
            request.OutputTrust ?? string.Empty,
            request.WritesDurableState,
            ct);

        return result switch
        {
            ExtensionMutationResult.Success => Results.NoContent(),
            ExtensionMutationResult.NotFound => Results.Problem(
                title: "Extension server or tool not found.",
                statusCode: StatusCodes.Status404NotFound),
            _ => Results.Problem(
                title: "The classification is invalid.",
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static ExtensionServerResponse ToResponse(ExtensionServerSnapshot server, bool? oauthConnected)
        => new(
            server.Id,
            server.Enabled,
            server.Transport,
            server.Status,
            server.Tools.Select(ToResponse).ToList(),
            oauthConnected);

    private static ExtensionToolResponse ToResponse(ExtensionToolSnapshot tool)
        => new(
            tool.RemoteName,
            tool.CatalogName,
            tool.PermissionLevel,
            tool.OutputTrust,
            tool.WritesDurableState,
            tool.Declared);

    private sealed record ExtensionInventoryResponse(IReadOnlyList<ExtensionServerResponse> Servers);

    private sealed record ExtensionServerResponse(
        string Id,
        bool Enabled,
        string Transport,
        string Status,
        IReadOnlyList<ExtensionToolResponse> Tools,
        bool? OauthConnected);

    private sealed record ExtensionToolResponse(
        string RemoteName,
        string? CatalogName,
        string PermissionLevel,
        string OutputTrust,
        bool WritesDurableState,
        bool Declared);

    private sealed record SetExtensionEnabledRequest(bool Enabled);

    private sealed record ClassifyExtensionToolRequest(
        string? ToolName,
        string? PermissionLevel,
        string? OutputTrust,
        bool WritesDurableState);
}
