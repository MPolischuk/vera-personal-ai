using System.Diagnostics.Metrics;

namespace Assistant.Scheduling;

/// <summary>Meter <c>Assistant.Scheduling</c>, instruments <c>assistant.scheduler.*</c>.</summary>
public static class SchedulerTelemetry
{
    public const string MeterName = "Assistant.Scheduling";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> JobsClaimed =
        Meter.CreateCounter<long>("assistant.scheduler.jobs_claimed");

    public static readonly Counter<long> JobsCompleted =
        Meter.CreateCounter<long>("assistant.scheduler.jobs_completed");

    public static readonly Counter<long> JobsFailed =
        Meter.CreateCounter<long>("assistant.scheduler.jobs_failed");

    public static readonly Counter<long> JobsRetried =
        Meter.CreateCounter<long>("assistant.scheduler.jobs_retried");

    public static readonly Counter<long> JobsReaped =
        Meter.CreateCounter<long>("assistant.scheduler.jobs_reaped");

    public static readonly Counter<long> FenceRejected =
        Meter.CreateCounter<long>("assistant.scheduler.fence_rejected");

    public static readonly Histogram<double> JobDuration =
        Meter.CreateHistogram<double>("assistant.scheduler.job_duration", unit: "ms");
}
