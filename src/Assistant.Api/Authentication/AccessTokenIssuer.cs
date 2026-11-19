using System.Security.Claims;
using System.Text;
using Assistant.Api.Configuration;
using Assistant.Core;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Assistant.Api.Authentication;

internal sealed class AccessTokenIssuer(IOptions<AuthOptions> options, TimeProvider time)
{
    public const int LifetimeSeconds = 900;

    public string Issue(Guid deviceId)
    {
        var signingKey = options.Value.JwtSigningKey
            ?? throw new InvalidOperationException("Auth:JwtSigningKey is required.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = WellKnownIds.DefaultUser.ToString("D"),
                ["device_id"] = deviceId.ToString("D"),
            },
            IssuedAt = issuedAt.UtcDateTime,
            Expires = issuedAt.AddSeconds(LifetimeSeconds).UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
