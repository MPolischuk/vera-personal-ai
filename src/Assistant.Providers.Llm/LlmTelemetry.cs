using System.Diagnostics.Metrics;

namespace Assistant.Providers.Llm;

internal static class LlmTelemetry
{
    public const string MeterName = "Assistant.Providers.Llm";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> TimeToFirstToken =
        Meter.CreateHistogram<double>("assistant.llm.time_to_first_token", "ms");

    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("assistant.llm.duration", "ms");

    public static readonly Counter<long> Tokens =
        Meter.CreateCounter<long>("assistant.llm.tokens");

    public static readonly Counter<double> Cost =
        Meter.CreateCounter<double>("assistant.llm.cost_usd");

    /// <summary>
    /// How far off the PT-03 D5 heuristic is, measured against the provider figure.
    /// If this drifts from 1 consistently, a real tokenizer stops being optional.
    /// </summary>
    public static readonly Histogram<double> EstimationError =
        Meter.CreateHistogram<double>("assistant.llm.estimation_error_ratio");

    /// <summary>
    /// Prefix-cache hit for one model call: cached input tokens divided by input tokens.
    /// Recorded only when input is greater than zero.
    /// </summary>
    public static readonly Histogram<double> CacheHitRatio =
        Meter.CreateHistogram<double>("assistant.llm.cache_hit_ratio", unit: "1");
}
