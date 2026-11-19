using System.Security.Cryptography;
using System.Text;

namespace Assistant.Channels.Delivery;

/// <summary>First 16 hex characters of SHA-256. The raw target is never stored.</summary>
internal static class TargetRefHash
{
    public static string Compute(string? targetRef)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(targetRef ?? ""));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }
}
