using System.Diagnostics;
using System.Diagnostics.Metrics;
using Assistant.Core.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory;

internal static class MemoryTelemetry
{
    public const string ActivitySourceName = "Assistant.Extensions.Memory";

    public const string MeterName = "Assistant.Extensions.Memory";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Inserts =
        Meter.CreateCounter<long>("assistant.memory.insert");

    public static readonly Counter<long> Updates =
        Meter.CreateCounter<long>("assistant.memory.update");

    public static readonly Counter<long> Forgets =
        Meter.CreateCounter<long>("assistant.memory.forget");

    public static readonly Counter<long> PreferenceUpserts =
        Meter.CreateCounter<long>("assistant.preference.upsert");

    public static readonly Counter<long> PreferenceHits =
        Meter.CreateCounter<long>("assistant.memory.preferences.hits");

    public static readonly Counter<long> PreferenceDropped =
        Meter.CreateCounter<long>("assistant.memory.preferences.dropped");

    public static readonly Counter<long> RelevantHits =
        Meter.CreateCounter<long>("assistant.memory.relevant.hits");

    public static readonly Counter<long> RelevantDropped =
        Meter.CreateCounter<long>("assistant.memory.relevant.dropped");
}

internal static partial class MemoryLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Memory {MemoryId} stored as {Kind}, updated={Updated}")]
    public static partial void Stored(ILogger logger, Guid memoryId, MemoryKind kind, bool updated);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Memory {MemoryId} forgotten")]
    public static partial void Forgotten(ILogger logger, Guid memoryId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Memory embed failed for turn {TurnId}")]
    public static partial void EmbedFailed(ILogger logger, Exception exception, Guid turnId);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Preference {Key} upserted")]
    public static partial void PreferenceUpserted(ILogger logger, string key);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Preference keys included: {Keys}")]
    public static partial void PreferenceKeysIncluded(ILogger logger, string keys);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Relevant memories included: {Items}")]
    public static partial void RelevantMemoriesIncluded(ILogger logger, string items);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "Memory consolidation due. Daily={Daily}, users over threshold={UserCount}")]
    public static partial void ConsolidationDue(ILogger logger, bool daily, int userCount);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Warning,
        Message = "Memory consolidation failed")]
    public static partial void ConsolidationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Memory consolidation returned no JSON for user {UserId}")]
    public static partial void ConsolidationUnreadable(ILogger logger, Guid userId);
}
