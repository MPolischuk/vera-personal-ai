using Microsoft.Extensions.Logging;

namespace Assistant.Mcp.Coolify;

/// <summary>
/// Application name to Coolify UUID. A malformed entry fails closed: the whole list is empty.
/// </summary>
internal sealed class CoolifyAllowlist
{
    private readonly Dictionary<string, string> _apps;

    private CoolifyAllowlist(Dictionary<string, string> apps) => _apps = apps;

    public IReadOnlyCollection<string> Names => _apps.Keys;

    public bool TryGetUuid(string name, out string uuid) => _apps.TryGetValue(name, out uuid!);

    public static CoolifyAllowlist Parse(string? raw, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new CoolifyAllowlist(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        var apps = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseEntry(entry, out var name, out var uuid) || apps.ContainsKey(name))
            {
                CoolifyLog.AllowlistMalformed(logger);
                return new CoolifyAllowlist(new Dictionary<string, string>(StringComparer.Ordinal));
            }

            apps.Add(name, uuid);
        }

        return new CoolifyAllowlist(apps);
    }

    private static bool TryParseEntry(string entry, out string name, out string uuid)
    {
        name = string.Empty;
        uuid = string.Empty;

        var separator = entry.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == entry.Length - 1)
        {
            return false;
        }

        name = entry[..separator].Trim();
        uuid = entry[(separator + 1)..].Trim();
        return name.Length > 0
            && uuid.Length > 0
            && !name.Contains(' ', StringComparison.Ordinal)
            && !uuid.Contains(' ', StringComparison.Ordinal)
            && !uuid.Contains('=', StringComparison.Ordinal);
    }
}
