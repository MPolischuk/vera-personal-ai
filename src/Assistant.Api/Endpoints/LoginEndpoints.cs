using System.Security.Claims;
using Assistant.Api.Authentication;
using Assistant.Persistence.Auth;

namespace Assistant.Api.Endpoints;

internal static class LoginEndpoints
{
    public static IEndpointRouteBuilder MapLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", LoginAsync).AllowAnonymous();
        endpoints.MapPost("/api/auth/refresh", RefreshAsync).AllowAnonymous();
        return endpoints;
    }

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/auth/devices/{id:guid}/revoke", RevokeAsync);
        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest? request,
        PasswordCredentialService credentials,
        IDeviceStore devices,
        AccessTokenIssuer issuer,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!await credentials.VerifyAsync(request?.Password, cancellationToken))
        {
            return Results.Unauthorized();
        }

        var name = request?.DeviceName?.Trim() ?? "";
        if (name.Length is < 1 or > 80)
        {
            return Results.BadRequest();
        }

        var issued = RefreshTokenProtector.Issue();
        var row = await devices.AddAsync(name, issued.Hash, cancellationToken);
        RefreshTokenProtector.Append(http.Response, issued.ClearToken);
        return Results.Ok(new AccessTokenResponse(issuer.Issue(row.Id)));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        IDeviceStore devices,
        AccessTokenIssuer issuer,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!http.Request.Cookies.TryGetValue(RefreshTokenProtector.CookieName, out var cookie)
            || !RefreshTokenProtector.TryUnprotect(cookie, out var hash))
        {
            return Results.Unauthorized();
        }

        var issued = RefreshTokenProtector.Issue();
        var row = await devices.RotateAsync(
            hash,
            issued.Hash,
            time.GetUtcNow(),
            RefreshTokenProtector.Lifetime,
            cancellationToken);
        if (row is null)
        {
            return Results.Unauthorized();
        }

        RefreshTokenProtector.Append(http.Response, issued.ClearToken);
        return Results.Ok(new AccessTokenResponse(issuer.Issue(row.Id)));
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext http,
        IDeviceStore devices,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await devices.RevokeAsync(id, time.GetUtcNow(), cancellationToken))
        {
            return Results.NotFound();
        }

        var current = user.FindFirstValue("device_id");
        if (string.Equals(current, id.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            RefreshTokenProtector.Delete(http.Response);
        }

        return Results.NoContent();
    }
}

internal sealed record LoginRequest(string? Password, string? DeviceName);

internal sealed record AccessTokenResponse(string AccessToken);
