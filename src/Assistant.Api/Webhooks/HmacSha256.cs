using System.Security.Cryptography;

namespace Assistant.Api.Webhooks;

/// <summary>HMAC-SHA256 compared in constant time. A candidate that is not 32 bytes does not match.</summary>
internal static class HmacSha256
{
    public const int Length = 32;

    public static bool AnyMatch(IReadOnlyList<byte[]> secrets, ReadOnlySpan<byte> content, IReadOnlyList<byte[]> candidates)
    {
        if (secrets.Count == 0)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[Length];
        var matched = false;
        foreach (var secret in secrets)
        {
            if (!HMACSHA256.TryHashData(secret, content, expected, out var written) || written != Length)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (candidate.Length == Length
                    && CryptographicOperations.FixedTimeEquals(expected, candidate))
                {
                    matched = true;
                }
            }
        }

        return matched;
    }
}
