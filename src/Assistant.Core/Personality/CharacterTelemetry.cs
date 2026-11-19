using System.Diagnostics.Metrics;

namespace Assistant.Core.Personality;

internal static class CharacterTelemetry
{
    public const string MeterName = "Assistant.Core";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> SituationalHits =
        Meter.CreateCounter<long>("character.situational.hits");

    public static readonly Counter<long> SituationalDropped =
        Meter.CreateCounter<long>("character.situational.dropped");

    public static readonly Counter<long> CueParsed =
        Meter.CreateCounter<long>("character.cue.parsed");

    public static readonly Counter<long> CueInvalid =
        Meter.CreateCounter<long>("character.cue.invalid");

    public static readonly Counter<long> CueAbsent =
        Meter.CreateCounter<long>("character.cue.absent");
}
