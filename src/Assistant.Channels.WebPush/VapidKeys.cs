using System.Buffers.Text;
using System.Security.Cryptography;

namespace Assistant.Channels.WebPush;

/// <summary>VAPID P-256 key pair in base64url, the form the push service expects.</summary>
public static class VapidKeys
{
    // PKCS#8 prefix for a P-256 private scalar (RFC 5208 + prime256v1). The 32-byte scalar follows.
    private static readonly byte[] Pkcs8Prefix = Convert.FromHexString(
        "3041020100301306072A8648CE3D020106082A8648CE3D030107042730250201010420");

    public static (string PublicKey, string PrivateKey) Generate()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdh.ExportParameters(includePrivateParameters: true);
        var publicKey = EncodePublic(parameters.Q);
        var privateKey = Base64Url.EncodeToString(parameters.D!);
        return (publicKey, privateKey);
    }

    /// <summary>Derives the public key from the private scalar and compares it to <paramref name="publicKey"/>.</summary>
    public static bool PairMatches(string? publicKey, string? privateKey)
    {
        if (!TryDecode(publicKey, 65, out var publicBytes) || publicBytes[0] != 0x04)
        {
            return false;
        }

        if (!TryDecode(privateKey, 32, out var privateBytes))
        {
            return false;
        }

        var derived = DerivePublic(privateBytes);
        return derived is not null && CryptographicOperations.FixedTimeEquals(derived, publicBytes);
    }

    private static byte[]? DerivePublic(byte[] privateKey)
    {
        var pkcs8 = new byte[Pkcs8Prefix.Length + privateKey.Length];
        Pkcs8Prefix.CopyTo(pkcs8, 0);
        privateKey.CopyTo(pkcs8.AsSpan(Pkcs8Prefix.Length));

        using var ecdh = ECDiffieHellman.Create();
        try
        {
            ecdh.ImportPkcs8PrivateKey(pkcs8, out var read);
            if (read != pkcs8.Length)
            {
                return null;
            }
        }
        catch (CryptographicException)
        {
            return null;
        }

        return EncodePublicBytes(ecdh.ExportParameters(includePrivateParameters: false).Q);
    }

    private static string EncodePublic(ECPoint point)
        => Base64Url.EncodeToString(EncodePublicBytes(point));

    private static byte[] EncodePublicBytes(ECPoint point)
    {
        var raw = new byte[65];
        raw[0] = 0x04;
        CopyCoordinate(point.X, raw.AsSpan(1, 32));
        CopyCoordinate(point.Y, raw.AsSpan(33, 32));
        return raw;
    }

    private static void CopyCoordinate(byte[]? source, Span<byte> destination)
    {
        if (source is null || source.Length > destination.Length)
        {
            throw new CryptographicException("The P-256 coordinate is missing or too long.");
        }

        source.CopyTo(destination[(destination.Length - source.Length)..]);
    }

    private static bool TryDecode(string? value, int expectedLength, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return false;
        }

        return bytes.Length == expectedLength;
    }
}
