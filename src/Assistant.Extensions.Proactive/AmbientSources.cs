using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Tools;
using Assistant.Core.Permissions;
using Assistant.Core.Tools;

namespace Assistant.Extensions.Proactive;

/// <summary>Active goals, one id per line. Empty when the user has none.</summary>
public sealed class GoalAmbientSource(IGoalStore goals) : IAmbientSource
{
    public string Name => "goals";

    public async ValueTask<string?> ReadAsync(Guid userId, CancellationToken ct)
    {
        var active = await goals.ListActiveAsync(userId, ct);
        if (active.Count == 0)
        {
            return null;
        }

        return string.Join(
            '\n',
            active.Select(goal => FormattableString.Invariant($"{goal.Id:D} {goal.Text}")));
    }
}

/// <summary>Calls <c>list_reminders</c>. A denial or a failure omits the source.</summary>
public sealed class ReminderAmbientSource(IToolDispatcher dispatcher) : IAmbientSource
{
    public string Name => "reminders";

    public async ValueTask<string?> ReadAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var result = await dispatcher.InvokeAsync(
                "list_reminders",
                ToolArguments.FromJson("""{"status":"active","limit":20}"""),
                Context(userId),
                ct);
            if (result.Permission != PermissionOutcome.Allow
                || result.Result is not { Status: ToolStatus.Ok, Content: { Length: > 0 } content })
            {
                return null;
            }

            return content;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static ToolExecutionContext Context(Guid userId)
        => new(userId, Guid.Empty, Guid.CreateVersion7());
}

/// <summary>
/// Calls <c>calendar_list_events</c> and fences each event.
/// A disconnected calendar returns null.
/// </summary>
public sealed class CalendarAmbientSource(IToolDispatcher dispatcher, TimeProvider clock) : IAmbientSource
{
    public string Name => "calendar";

    public async ValueTask<string?> ReadAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var result = await dispatcher.InvokeAsync(
                "calendar_list_events",
                ToolArguments.FromJson(Window()),
                new ToolExecutionContext(userId, Guid.Empty, Guid.CreateVersion7()),
                ct);
            if (result.Permission != PermissionOutcome.Allow
                || result.Result is not { Status: ToolStatus.Ok, Content: { Length: > 0 } content })
            {
                return null;
            }

            return CalendarEventText.Format(content);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private string Window()
    {
        var local = BriefingClock.LocalDate(clock.GetUtcNow());
        var start = BriefingClock.AtLocalEight(local).AddHours(-8);
        var end = start.AddDays(1);
        return JsonSerializer.Serialize(new
        {
            calendarId = "primary",
            timeMin = start.ToString("O", CultureInfo.InvariantCulture),
            timeMax = end.ToString("O", CultureInfo.InvariantCulture),
        });
    }
}
