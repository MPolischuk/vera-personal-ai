using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Assistant.Api.Configuration;
using Assistant.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Assistant.Api.Authentication;

internal sealed class StaticBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthOptions> authOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";
    private static readonly PathString HubPath = new("/hub/assistant");

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ResolveToken();
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var provided = Encoding.UTF8.GetBytes(token);
        var expected = Encoding.UTF8.GetBytes(authOptions.Value.BearerToken);

        if (provided.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, WellKnownIds.DefaultUser.ToString("D")),
        };
        var identity = new ClaimsIdentity(claims, StaticBearerDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, StaticBearerDefaults.AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? ResolveToken()
    {
        var authorization = Request.Headers[HeaderNames.Authorization].ToString();
        if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return authorization[BearerPrefix.Length..].Trim();
        }

        return Request.Path.StartsWithSegments(HubPath)
            ? Request.Query["access_token"].ToString()
            : null;
    }
}
