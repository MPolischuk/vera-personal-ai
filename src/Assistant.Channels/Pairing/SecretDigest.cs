using System.Security.Cryptography;
using System.Text;

namespace Assistant.Channels.Pairing;

/// <summary>SHA-256 hex digest. Pairing codes and deep-link tokens are stored only in this form.</summary>
internal static class SecretDigest
{
    public static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static bool FixedTimeEquals(string leftHex, string rightHex)
    {
        if (leftHex.Length != rightHex.Length || leftHex.Length == 0 || leftHex.Length % 2 != 0)
        {
            return false;
        }

        try
        {
            var left = Convert.FromHexString(leftHex);
            var right = Convert.FromHexString(rightHex);
            return CryptographicOperations.FixedTimeEquals(left, right);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
