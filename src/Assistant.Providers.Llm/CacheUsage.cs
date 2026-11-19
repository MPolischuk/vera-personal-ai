using Microsoft.Extensions.AI;

namespace Assistant.Providers.Llm;

/// <summary>
/// Reads cache-hit and cache-write counts from provider usage. Never throws:
/// a missing count is zero.
/// </summary>
internal static class CacheUsage
{
    // MEAI 10.9.0 UsageDetails has no RawRepresentation. Write counts come only
    // from AdditionalCounts; a missing key stays zero (V2-01.1 D5).
    private static readonly string[] WriteCountKeys =
    [
        "cache_write_tokens",
        "cache_creation_input_tokens",
        "input_cache_creation_tokens",
    ];

    public static (int CachedInput, int CacheWrite) Read(UsageDetails? usage)
    {
        if (usage is null)
        {
            return (0, 0);
        }

        return (ToCount(usage.CachedInputTokenCount), ReadWrite(usage));
    }

    private static int ReadWrite(UsageDetails usage)
    {
        if (usage.AdditionalCounts is not { } counts)
        {
            return 0;
        }

        foreach (var key in WriteCountKeys)
        {
            if (counts.TryGetValue(key, out var value) && value >= 0)
            {
                return ToCount(value);
            }
        }

        return 0;
    }

    private static int ToCount(long? value) => value is null ? 0 : ToCount(value.Value);

    private static int ToCount(long value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)value;
    }
}
