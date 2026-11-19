using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Reminders;

/// <summary>Limits for explicit reminders. Bound from the <c>Reminders</c> section.</summary>
public sealed class RemindersOptions
{
    public const string SectionName = "Reminders";

    public bool Enabled { get; set; } = true;

    public TimeSpan MinLeadTime { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxHorizonDays { get; set; } = 730;

    public int MaxActive { get; set; } = 50;

    public int MaxSnoozesPerOccurrence { get; set; } = 6;

    public TimeSpan LateGrace { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan MissedAfter { get; set; } = TimeSpan.FromHours(12);
}

/// <summary>Rejects a <see cref="RemindersOptions"/> that would make scheduling unbounded.</summary>
public sealed class RemindersOptionsValidator : IValidateOptions<RemindersOptions>
{
    public ValidateOptionsResult Validate(string? name, RemindersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.MinLeadTime <= TimeSpan.Zero || options.MinLeadTime > TimeSpan.FromDays(1))
        {
            failures.Add("Reminders:MinLeadTime must be greater than zero and at most one day.");
        }

        if (options.MaxHorizonDays is < 1 or > 3650)
        {
            failures.Add("Reminders:MaxHorizonDays must be between 1 and 3650.");
        }

        if (options.MaxActive is < 1 or > 1000)
        {
            failures.Add("Reminders:MaxActive must be between 1 and 1000.");
        }

        if (options.MaxSnoozesPerOccurrence is < 0 or > 100)
        {
            failures.Add("Reminders:MaxSnoozesPerOccurrence must be between 0 and 100.");
        }

        if (options.LateGrace < TimeSpan.Zero)
        {
            failures.Add("Reminders:LateGrace cannot be negative.");
        }

        if (options.MissedAfter < TimeSpan.Zero)
        {
            failures.Add("Reminders:MissedAfter cannot be negative.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
