using System.Diagnostics.Metrics;

namespace Assistant.Core;

internal static class CoreTelemetry
{
    public const string MeterName = "Assistant.Core";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> FragmentsDropped =
        Meter.CreateCounter<long>("assistant.context.fragments_dropped");

    public static readonly Counter<long> PartsTrimmed =
        Meter.CreateCounter<long>("assistant.context.parts_trimmed");

    public static readonly Counter<long> Invocations =
        Meter.CreateCounter<long>("assistant.tools.invocations");
}
