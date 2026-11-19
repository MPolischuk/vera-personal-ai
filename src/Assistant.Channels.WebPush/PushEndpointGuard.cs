using System.Net;
using Assistant.Abstractions.Net;

namespace Assistant.Channels.WebPush;

/// <summary>
/// Accepts an absolute https endpoint only when its host is on the push-service list.
/// An IP literal never matches, even when the list names that address.
/// </summary>
internal static class PushEndpointGuard
{
    public static bool IsAllowed(string? endpoint, IReadOnlyList<string>? allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        return HostMatches(uri.Host, allowedHosts);
    }

    /// <summary>Delegates to <see cref="OutboundAddressPolicy.IsBlocked"/>.</summary>
    public static bool IsBlockedAddress(IPAddress address)
        => OutboundAddressPolicy.IsBlocked(address);

    internal static bool TryHostLiteral(string? host, out IPAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var text = host.Trim();
        if (text.Length >= 2 && text[0] == '[' && text[^1] == ']')
        {
            text = text[1..^1];
        }

        if (!IPAddress.TryParse(text, out var parsed))
        {
            return false;
        }

        address = parsed;
        return true;
    }

    private static bool HostMatches(string host, IReadOnlyList<string>? allowedHosts)
    {
        if (allowedHosts is null || allowedHosts.Count == 0 || TryHostLiteral(host, out _))
        {
            return false;
        }

        var normalized = host.Trim().TrimEnd('.');
        foreach (var entry in allowedHosts)
        {
            if (EntryMatches(normalized, entry))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EntryMatches(string host, string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var pattern = entry.Trim().TrimEnd('.');
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            if (pattern.IndexOf('*', 1) >= 0)
            {
                return false;
            }

            var suffix = pattern[1..];
            return host.Length > suffix.Length
                && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        return host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
