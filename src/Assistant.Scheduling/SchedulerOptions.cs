using Microsoft.Extensions.Options;

namespace Assistant.Scheduling;

/// <summary><c>Scheduler</c> section. Defaults match the durable scheduler design.</summary>
public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    public bool Enabled { get; set; } = true;

    public TimeSpan PulseInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 10;

    public int MaxDegreeOfParallelism { get; set; } = 4;

    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ShutdownGrace { get; set; } = TimeSpan.FromSeconds(20);

    public TimeSpan SucceededRetention { get; set; } = TimeSpan.FromDays(14);

    public TimeSpan FailedRetention { get; set; } = TimeSpan.FromDays(30);

    public TimeOnly PurgeLocalTime { get; set; } = new(4, 20);

    public string PurgeTimeZoneId { get; set; } = "America/Argentina/Buenos_Aires";
}

internal sealed class SchedulerOptionsValidator : IValidateOptions<SchedulerOptions>
{
    public ValidateOptionsResult Validate(string? name, SchedulerOptions options)
    {
        if (options.PulseInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Scheduler:PulseInterval must be positive.");
        }

        if (options.BatchSize < 1)
        {
            return ValidateOptionsResult.Fail("Scheduler:BatchSize must be at least 1.");
        }

        if (options.MaxDegreeOfParallelism < 1)
        {
            return ValidateOptionsResult.Fail("Scheduler:MaxDegreeOfParallelism must be at least 1.");
        }

        if (options.Lease <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Scheduler:Lease must be positive.");
        }

        if (options.LeaseRenewalInterval <= TimeSpan.Zero || options.LeaseRenewalInterval >= options.Lease)
        {
            return ValidateOptionsResult.Fail("Scheduler:LeaseRenewalInterval must be positive and shorter than Lease.");
        }

        if (options.ShutdownGrace <= TimeSpan.Zero || options.ShutdownGrace >= TimeSpan.FromSeconds(30))
        {
            return ValidateOptionsResult.Fail("Scheduler:ShutdownGrace must be positive and shorter than the host shutdown timeout (30s).");
        }

        if (options.SucceededRetention <= TimeSpan.Zero || options.FailedRetention <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Scheduler retention windows must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.PurgeTimeZoneId))
        {
            return ValidateOptionsResult.Fail("Scheduler:PurgeTimeZoneId is required.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(options.PurgeTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return ValidateOptionsResult.Fail($"Scheduler:PurgeTimeZoneId '{options.PurgeTimeZoneId}' is not a known time zone.");
        }
        catch (InvalidTimeZoneException)
        {
            return ValidateOptionsResult.Fail($"Scheduler:PurgeTimeZoneId '{options.PurgeTimeZoneId}' is not a known time zone.");
        }

        return ValidateOptionsResult.Success;
    }
}
