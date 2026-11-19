using System.Net;
using System.Net.Sockets;

namespace Assistant.Abstractions.Net;

/// <summary>
/// Rejects outbound addresses that must not be dialed.
/// The decision is on <see cref="IPAddress"/>, not on a hostname allow-list.
/// A digit-only host or a label that is <c>0</c> followed by digits is rejected before DNS.
/// </summary>
public static class OutboundAddressPolicy
{
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlockedV4(address.MapToIPv4());
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6
            && TryExtractEmbeddedIPv4(address, out var embedded))
        {
            return IsBlockedV4(embedded);
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedV4(address),
            AddressFamily.InterNetworkV6 => IsBlockedV6(address),
            _ => true,
        };
    }

    /// <summary>
    /// True when <paramref name="host"/> must be rejected before DNS.
    /// A normal hostname returns false.
    /// </summary>
    public static bool IsBlockedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var text = NormalizeHost(host);
        if (text.Length == 0 || IsDigitOnly(text) || HasOctalLabel(text))
        {
            return true;
        }

        return IPAddress.TryParse(text, out var address) && IsBlocked(address);
    }

    /// <summary>
    /// True when the URI's scheme, original host text, or normalized host must not be requested.
    /// Inspecting <see cref="Uri.OriginalString"/> catches an octal label that <see cref="Uri.Host"/> rewrote into a public address.
    /// </summary>
    public static bool IsBlockedTarget(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var originalHost = ReadOriginalHost(uri.OriginalString);
        if (originalHost is not null && IsBlockedHost(originalHost))
        {
            return true;
        }

        return IsBlockedHost(uri.IdnHost) || IsBlockedHost(NormalizeHost(uri.Host));
    }

    private static bool IsBlockedV4(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        var first = bytes[0];
        var second = bytes[1];
        if (first is 0 or 10 or 127)
        {
            return true;
        }

        // 100.64.0.0/10 carrier-grade NAT.
        if (first == 100 && second is >= 64 and <= 127)
        {
            return true;
        }

        if (first == 169 && second == 254)
        {
            return true;
        }

        if (first == 172 && second is >= 16 and <= 31)
        {
            return true;
        }

        if (first == 192 && second == 168)
        {
            return true;
        }

        // 224.0.0.0/4 multicast. Limited broadcast is handled above.
        return first is >= 224 and <= 239;
    }

    private static bool IsBlockedV6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        // fc00::/7 unique local.
        if ((bytes[0] & 0xFE) == 0xFC)
        {
            return true;
        }

        // fec0::/10 site local. IPAddress.IsIPv6SiteLocal is obsolete and always false.
        return bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0;
    }

    private static bool TryExtractEmbeddedIPv4(IPAddress address, out IPAddress ipv4)
    {
        ipv4 = IPAddress.None;
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 16)
        {
            return false;
        }

        // 6to4 2002::/16. The IPv4 is the next 32 bits.
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            ipv4 = new IPAddress(bytes.AsSpan(2, 4));
            return true;
        }

        // Teredo 2001:0000::/32. The client IPv4 is the last 32 bits XOR 0xffffffff.
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            Span<byte> decoded = stackalloc byte[4];
            decoded[0] = (byte)~bytes[12];
            decoded[1] = (byte)~bytes[13];
            decoded[2] = (byte)~bytes[14];
            decoded[3] = (byte)~bytes[15];
            ipv4 = new IPAddress(decoded);
            return true;
        }

        // NAT64 well-known prefix 64:ff9b::/96.
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B
            && bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0
            && bytes[8] == 0 && bytes[9] == 0 && bytes[10] == 0 && bytes[11] == 0)
        {
            ipv4 = new IPAddress(bytes.AsSpan(12, 4));
            return true;
        }

        return false;
    }

    private static string NormalizeHost(string host)
    {
        var text = host.Trim().TrimEnd('.');
        if (text.Length >= 2 && text[0] == '[' && text[^1] == ']')
        {
            text = text[1..^1];
        }

        return text;
    }

    private static bool IsDigitOnly(string text)
    {
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    private static bool HasOctalLabel(string host)
    {
        var start = 0;
        for (var i = 0; i <= host.Length; i++)
        {
            if (i != host.Length && host[i] != '.')
            {
                continue;
            }

            var length = i - start;
            if (length >= 2 && host[start] == '0' && IsDigitOnly(host.AsSpan(start, length)))
            {
                return true;
            }

            start = i + 1;
        }

        return false;
    }

    private static bool IsDigitOnly(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    private static string? ReadOriginalHost(string original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return null;
        }

        var text = original.Trim();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        var start = scheme >= 0 ? scheme + 3 : 0;
        if (start >= text.Length)
        {
            return null;
        }

        var path = text.IndexOfAny(['/', '?', '#'], start);
        var authorityEnd = path < 0 ? text.Length : path;
        if (start >= authorityEnd)
        {
            return null;
        }

        var at = text.LastIndexOf('@', authorityEnd - 1, authorityEnd - start);
        if (at >= start)
        {
            start = at + 1;
        }

        if (start >= authorityEnd)
        {
            return null;
        }

        var authority = text[start..authorityEnd];
        if (authority.Length == 0)
        {
            return null;
        }

        if (authority[0] == '[')
        {
            var close = authority.IndexOf(']', StringComparison.Ordinal);
            return close > 1 ? authority[1..close] : null;
        }

        var colon = authority.LastIndexOf(':');
        if (colon > 0 && authority.IndexOf(':') == colon)
        {
            return authority[..colon];
        }

        return authority;
    }
}
