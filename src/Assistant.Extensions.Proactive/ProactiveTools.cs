using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Scheduling;
using Assistant.Abstractions.Time;
using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Proactive;

internal sealed class SetGoalTool(IGoalStore goals, TimeProvider clock) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["text"],
          "properties": {
            "text": { "type": "string", "minLength": 1, "maxLength": 200, "description": "La meta, dicha por la persona. No la infieras del chat." }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "set_goal",
        "Guarda una meta explícita que la persona pidió. No inventes metas.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("text", out var text))
        {
            return ToolResult.Failed("Falta el texto de la meta.");
        }

        try
        {
            var goal = await goals.InsertAsync(context.UserId, text, clock.GetUtcNow(), ct);
            return ToolResult.Ok(goal.Id.ToString("D"));
        }
        catch (ArgumentException)
        {
            return ToolResult.Failed("La meta tiene que tener entre 1 y 200 caracteres.");
        }
    }
}

internal sealed class CompleteGoalTool(IGoalStore goals, TimeProvider clock) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["goal_id"],
          "properties": {
            "goal_id": { "type": "string", "description": "Id de una meta activa de esta persona." }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "complete_goal",
        "Marca como hecha una meta activa de la persona.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("goal_id", out var raw) || !Guid.TryParse(raw, out var goalId))
        {
            return ToolResult.Failed("Falta el id de la meta.");
        }

        var done = await goals.CompleteAsync(context.UserId, goalId, clock.GetUtcNow(), ct);
        return done ? ToolResult.Ok("ok") : ToolResult.Failed("No encontré esa meta activa.");
    }
}

internal sealed class ListGoalsTool(IGoalStore goals) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        { "type": "object", "additionalProperties": false, "properties": {} }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "list_goals",
        "Lista las metas activas. No incluye las hechas.",
        Schema,
        PermissionLevel.Read,
        "proactive");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var active = await goals.ListActiveAsync(context.UserId, ct);
        var lines = active.Select(goal => FormattableString.Invariant($"{goal.Id:D} {goal.Text}"));
        return ToolResult.Ok(active.Count == 0 ? "[]" : string.Join('\n', lines));
    }
}

internal sealed class SetPhraseShortcutTool(IPhraseShortcutStore shortcuts) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["phrase"],
          "properties": {
            "phrase": { "type": "string", "minLength": 1, "maxLength": 40, "description": "La frase que la persona quiere usar para pedir el briefing de hoy." }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "set_phrase_shortcut",
        "Guarda una frase que devuelve el briefing de hoy sin llamar al modelo. La única acción es briefing.latest.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("phrase", out var phrase) || !PhraseNormalizer.TryNormalize(phrase, out var normalized))
        {
            return ToolResult.Failed("La frase tiene que tener entre 1 y 40 caracteres.");
        }

        var saved = await shortcuts.SetAsync(context.UserId, normalized, PhraseActions.BriefingLatest, ct);
        return ToolResult.Ok(saved.PhraseNormalized);
    }
}

internal sealed class ListAutomationsTool(IAutomationStore automations) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        { "type": "object", "additionalProperties": false, "properties": {} }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "list_automations",
        "Lista las automatizaciones: título, cadencia, estado y próxima corrida. No incluye la instrucción.",
        Schema,
        PermissionLevel.Read,
        "proactive");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var rows = await automations.ListAsync(context.UserId, ct);
        if (rows.Count == 0)
        {
            return ToolResult.Ok("[]");
        }

        var lines = rows.Select(row => FormattableString.Invariant(
            $"{row.Id:D} {row.Title} {row.Cadence} {row.Status} {row.NextRunAt:O}"));
        return ToolResult.Ok(string.Join('\n', lines));
    }
}

internal sealed class CreateAutomationTool(
    IAutomationStore automations,
    IJobScheduler scheduler,
    IUserTimeZoneProvider timeZones,
    TimeProvider clock) : ITool
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["title", "instruction", "cadence"],
          "properties": {
            "title": { "type": "string", "minLength": 1, "maxLength": 80 },
            "instruction": { "type": "string", "minLength": 1, "maxLength": 2000 },
            "cadence": { "type": "string", "enum": ["once", "interval", "daily"] },
            "local_time": { "type": "string", "description": "Hora local HH:mm. Obligatoria si cadence es daily." },
            "interval_minutes": { "type": "integer", "minimum": 30, "description": "Obligatorio si cadence es interval. Mínimo 30." },
            "run_at": { "type": "string", "description": "Instante ISO-8601. Obligatorio si cadence es once." }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "create_automation",
        "Crea una automatización once, interval (mínimo 30 minutos) o daily a una hora local HH:mm. No crea la del briefing.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("title", out var title)
            || !args.TryGetString("instruction", out var instruction)
            || !args.TryGetString("cadence", out var cadence))
        {
            return ToolResult.Failed("Faltan título, instrucción o cadencia.");
        }

        args.TryGetString("local_time", out var localTime);
        var interval = args.TryGetInt32("interval_minutes", out var minutes) ? minutes : (int?)null;
        DateTimeOffset? runAt = null;
        if (args.TryGetString("run_at", out var runAtText))
        {
            if (!DateTimeOffset.TryParse(runAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return ToolResult.Failed("run_at no es un instante ISO-8601.");
            }

            runAt = parsed;
        }

        var now = clock.GetUtcNow();
        if (cadence == AutomationCadence.Daily)
        {
            if (!AutomationRules.TryParseLocalTime(localTime, out var hhmm))
            {
                return ToolResult.Failed("Una automatización diaria necesita una hora HH:mm.");
            }

            localTime = hhmm;
            var zone = await timeZones.GetTimeZoneAsync(context.UserId, ct);
            runAt = NextDaily(zone, now, hhmm);
        }
        else if (cadence == AutomationCadence.Interval)
        {
            runAt = now.AddMinutes(interval ?? 0);
        }

        try
        {
            var created = await automations.InsertUserAsync(
                new NewUserAutomation(
                    context.UserId,
                    title,
                    instruction,
                    cadence,
                    localTime,
                    interval,
                    context.ConversationId,
                    runAt),
                now,
                ct);
            if (created.NextRunAt is DateTimeOffset due)
            {
                await scheduler.ScheduleAsync(
                    new JobSpec(
                        AutomationJobs.RunKind,
                        due,
                        JsonSerializer.Serialize(
                            new AutomationRunPayload(created.UserId, created.Id),
                            PayloadJson),
                        AutomationJobs.Key(created.Id, due)),
                    ct);
            }

            return ToolResult.Ok(created.Id.ToString("D"));
        }
        catch (ArgumentException exception)
        {
            return ToolResult.Failed(exception.Message);
        }
    }

    private static DateTimeOffset NextDaily(TimeZoneInfo zone, DateTimeOffset utc, string hhmm)
    {
        var parsed = TimeOnly.ParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture);
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var candidate = new DateTime(
            local.Year, local.Month, local.Day, parsed.Hour, parsed.Minute, 0, DateTimeKind.Unspecified);
        var instant = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
        if (instant <= utc)
        {
            candidate = candidate.AddDays(1);
            instant = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
        }

        return instant;
    }
}

internal sealed class PauseAutomationTool(
    IAutomationStore automations,
    IJobScheduler scheduler,
    TimeProvider clock) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["automation_id"],
          "properties": {
            "automation_id": { "type": "string" }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "pause_automation",
        "Pausa una automatización de la persona. No pausa el briefing del sistema.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!TryId(args, out var id))
        {
            return ToolResult.Failed("Falta el id de la automatización.");
        }

        var row = await automations.FindAsync(context.UserId, id, ct);
        if (row is null)
        {
            return ToolResult.Failed("No encontré esa automatización.");
        }

        if (row.Cadence == AutomationCadence.SystemDaily)
        {
            return ToolResult.Failed("El briefing del sistema no se pausa.");
        }

        if (row.NextRunAt is DateTimeOffset due)
        {
            await scheduler.CancelAsync(AutomationJobs.Key(row.Id, due), ct);
        }

        var paused = await automations.PauseAsync(context.UserId, id, clock.GetUtcNow(), ct);
        return paused ? ToolResult.Ok("ok") : ToolResult.Failed("No pude pausarla.");
    }

    private static bool TryId(ToolArguments args, out Guid id)
    {
        id = Guid.Empty;
        return args.TryGetString("automation_id", out var raw) && Guid.TryParse(raw, out id);
    }
}

internal sealed class DeleteAutomationTool(IAutomationStore automations, IJobScheduler scheduler) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["automation_id"],
          "properties": {
            "automation_id": { "type": "string" }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "delete_automation",
        "Borra una automatización de la persona y cancela su próxima corrida. No borra el briefing del sistema.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("automation_id", out var raw) || !Guid.TryParse(raw, out var id))
        {
            return ToolResult.Failed("Falta el id de la automatización.");
        }

        var row = await automations.FindAsync(context.UserId, id, ct);
        if (row is null)
        {
            return ToolResult.Failed("No encontré esa automatización.");
        }

        if (row.Cadence == AutomationCadence.SystemDaily)
        {
            return ToolResult.Failed("El briefing del sistema no se borra.");
        }

        if (row.NextRunAt is DateTimeOffset due)
        {
            await scheduler.CancelAsync(AutomationJobs.Key(row.Id, due), ct);
        }

        var deleted = await automations.DeleteAsync(context.UserId, id, ct);
        return deleted ? ToolResult.Ok("ok") : ToolResult.Failed("No pude borrarla.");
    }
}
