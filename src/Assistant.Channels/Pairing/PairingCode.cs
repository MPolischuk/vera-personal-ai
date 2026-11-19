using System.Security.Cryptography;

namespace Assistant.Channels.Pairing;

/// <summary>8-character Crockford base32 pairing code, shown as <c>XXXX-XXXX</c>.</summary>
public static class PairingCode
{
    /// <summary>Crockford alphabet without I, L, O, and U.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Creates a code. <paramref name="random"/> fills 8 bytes; production uses <see cref="RandomNumberGenerator"/>.</summary>
    public static string Create(RandomNumberGenerator? random = null)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (random is null)
        {
            RandomNumberGenerator.Fill(bytes);
        }
        else
        {
            random.GetBytes(bytes);
        }

        Span<char> chars = stackalloc char[9];
        chars[4] = '-';
        for (var i = 0; i < 8; i++)
        {
            var index = i < 4 ? i : i + 1;
            chars[index] = Alphabet[bytes[i] & 31];
        }

        return new string(chars);
    }

    /// <summary>Strips spaces and hyphens and uppercases. False when the result is not 8 alphabet characters.</summary>
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        Span<char> buffer = stackalloc char[8];
        var length = 0;
        foreach (var character in raw)
        {
            if (character is '-' or ' ' or '\t')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(character);
            if (length == 8 || Alphabet.IndexOf(upper) < 0)
            {
                return false;
            }

            buffer[length++] = upper;
        }

        if (length != 8)
        {
            return false;
        }

        normalized = new string(buffer);
        return true;
    }

    /// <summary>Display form of an already normalized 8-character code.</summary>
    public static string Format(string normalized)
        => normalized.Length == 8 ? string.Concat(normalized.AsSpan(0, 4), "-", normalized.AsSpan(4, 4)) : normalized;
}
