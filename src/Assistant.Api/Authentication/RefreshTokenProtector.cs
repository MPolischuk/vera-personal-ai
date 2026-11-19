using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Assistant.Api.Authentication;

internal static class RefreshTokenProtector
{
    public const string CookieName = "assistant_refresh";

    public const string CookiePath = "/api/auth/refresh";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static RefreshTokenIssue Issue()
    {
        var clear = RandomNumberGenerator.GetBytes(32);
        return new RefreshTokenIssue(WebEncoders.Base64UrlEncode(clear), SHA256.HashData(clear));
    }

    public static bool TryUnprotect(string? cookie, out byte[] hash)
    {
        hash = [];
        if (string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        try
        {
            var clear = WebEncoders.Base64UrlDecode(cookie);
            if (clear.Length != 32)
            {
                return false;
            }

            hash = SHA256.HashData(clear);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static void Append(HttpResponse response, string clearToken)
        => response.Cookies.Append(CookieName, clearToken, PersistentCookie());

    public static void Delete(HttpResponse response)
        => response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = CookiePath,
        });

    private static CookieOptions PersistentCookie()
        => new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = CookiePath,
            MaxAge = Lifetime,
        };
}

internal readonly record struct RefreshTokenIssue(string ClearToken, byte[] Hash);
