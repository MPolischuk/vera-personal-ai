using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Assistant.Core.Conversations;

internal static class TurnTelemetry
{
    public const string ActivitySourceName = "Assistant.Core";
    public const string MeterName = "Assistant.Core";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("assistant.turn.duration", unit: "ms");

    public static readonly Histogram<double> TimeToFirstToken =
        Meter.CreateHistogram<double>("assistant.turn.time_to_first_token", unit: "ms");

    public static readonly Histogram<int> ToolIterations =
        Meter.CreateHistogram<int>("assistant.turn.tool_iterations");
}
