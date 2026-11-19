using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Assistant.Extensions.Reminders;

/// <summary>
/// Meter <c>Assistant.Extensions.Reminders</c>, instruments <c>assistant.reminder.*</c>.
/// Tags are <c>origin_channel</c> and <c>recurring</c>. The reminder text is not a tag.
/// </summary>
public static class ReminderTelemetry
{
    public const string MeterName = "Assistant.Extensions.Reminders";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Fired =
        Meter.CreateCounter<long>("assistant.reminder.fired");

    public static readonly Counter<long> Missed =
        Meter.CreateCounter<long>("assistant.reminder.missed");

    public static void Record(string originChannel, bool recurring, bool fired)
    {
        var tags = new TagList
        {
            { "origin_channel", originChannel },
            { "recurring", recurring },
        };
        (fired ? Fired : Missed).Add(1, tags);
    }
}
