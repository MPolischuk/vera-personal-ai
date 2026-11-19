using System.Security.Claims;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Security;
using Assistant.Api.Authentication;
using Assistant.Extensions.Mcp;
using Microsoft.AspNetCore.DataProtection;

namespace Assistant.Api.Endpoints;

internal static class CalendarOAuthEndpoints
{
    public const string CallbackPath = "/api/extensions/calendar/oauth/callback";

    public const string StatePurpose = "Assistant.Mcp.OAuth.State";

    public static IEndpointRouteBuilder MapCalendarOAuthStart(this IEndpointRouteBuilder group)
    {
        group.MapGet("/extensions/calendar/oauth/start", StartAsync);
        return group;
    }

    public static IEndpointRouteBuilder MapCalendarOAuthCallback(this IEndpointRouteBuilder app)
    {
        app.MapGet(CallbackPath, CallbackAsync).AllowAnonymous();
        return app;
    }

    private static IResult StartAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IDataProtectionProvider protection,
        TimeProvider clock)
    {
        var clientId = Environment.GetEnvironmentVariable(CalendarAuthorization.ClientIdVariable);
        var clientSecret = Environment.GetEnvironmentVariable(CalendarAuthorization.ClientSecretVariable);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return Results.Problem(
                title: "Calendar OAuth is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var redirect = RedirectUri(request);
        var (verifier, challenge) = CalendarAuthorization.CreatePkce();
        var expires = clock.GetUtcNow().AddMinutes(10);
        var state = protection.CreateProtector(StatePurpose).Protect(
            $"{user.UserId():D}|{expires.ToUnixTimeSeconds()}|{verifier}");
        var options = CalendarAuthorization.CreateOptions(redirect, clientId, clientSecret);
        var url = CalendarAuthorization.BuildAuthorizationUri(options, state, challenge);
        return Results.Ok(new StartResponse(url.ToString()));
    }

    private static async Task<IResult> CallbackAsync(
        HttpRequest request,
        string? code,
        string? state,
        IDataProtectionProvider protection,
        IMcpOAuthTokenStore tokens,
        ISecretBox box,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(code)
            && !string.IsNullOrWhiteSpace(state)
            && TryReadState(protection, state, clock.GetUtcNow(), out var userId, out var verifier))
        {
            var clientId = Environment.GetEnvironmentVariable(CalendarAuthorization.ClientIdVariable);
            var clientSecret = Environment.GetEnvironmentVariable(CalendarAuthorization.ClientSecretVariable);
            if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
            {
                var refresh = await CalendarAuthorization.ExchangeCodeAsync(
                    code,
                    verifier,
                    RedirectUri(request).ToString(),
                    clientId,
                    clientSecret,
                    ct);
                if (!string.IsNullOrWhiteSpace(refresh))
                {
                    await tokens.SaveRefreshAsync(
                        userId,
                        CalendarWriteTools.ServerId,
                        box.Protect(refresh),
                        clock.GetUtcNow(),
                        ct);
                }
            }
        }

        return Results.Redirect("/?view=extensions");
    }

    private static bool TryReadState(
        IDataProtectionProvider protection,
        string state,
        DateTimeOffset now,
        out Guid userId,
        out string verifier)
    {
        userId = Guid.Empty;
        verifier = string.Empty;
        string plain;
        try
        {
            plain = protection.CreateProtector(StatePurpose).Unprotect(state);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }

        var parts = plain.Split('|');
        if (parts.Length != 3
            || !Guid.TryParse(parts[0], out userId)
            || !long.TryParse(parts[1], out var unix)
            || string.IsNullOrWhiteSpace(parts[2]))
        {
            return false;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(unix) < now)
        {
            return false;
        }

        verifier = parts[2];
        return true;
    }

    private static Uri RedirectUri(HttpRequest request)
    {
        var configured = Environment.GetEnvironmentVariable(CalendarAuthorization.RedirectUriVariable);
        if (!string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        return new Uri($"{request.Scheme}://{request.Host}{CallbackPath}");
    }

    private sealed record StartResponse(string Url);
}
