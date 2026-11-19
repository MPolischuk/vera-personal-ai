using System.Text.Json;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Time;
using Assistant.Abstractions.Tools;
using Assistant.Extensions.Reminders.Time;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Reminders.Tools;

internal sealed class CreateReminderTool(
    IReminderStore store,
    ReminderTimeResolver resolver,
    IUserTimeZoneProvider timeZones,
    ITurnOriginAccessor origins,
    TimeProvider clock,
    IOptions<RemindersOptions> options) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["text", "due"],
          "properties": {
            "text": {
              "type": "string",
              "minLength": 1,
              "maxLength": 500,
              "description": "Qué hay que recordarle, en segunda persona y breve. Ej.: 'Sacar la basura'. Si el usuario dijo 'a la tarde' o 'a las 10' sin cerrar la hora, no completes este campo: no llames la tool."
            },
            "due": {
              "type": "object",
              "additionalProperties": false,
              "description": "Cuándo, sólo si la hora es unívoca. 'in' para un plazo que el usuario dijo ('en 20 minutos'). 'local_datetime' para una fecha y hora concretas. 'a las 10' y 'a la tarde' no se completan: no llames la tool. Exactamente uno.",
              "properties": {
                "in": { "type": "string", "description": "Duración ISO 8601: PT20M, PT2H, P1D." },
                "local_datetime": { "type": "string", "description": "Fecha y hora local sin zona: 2026-10-08T18:00." }
              }
            },
            "recurrence": {
              "type": "object",
              "additionalProperties": false,
              "description": "Sólo si el usuario pidió que se repita. Si no, omitilo.",
              "properties": {
                "frequency": { "type": "string", "enum": ["daily", "weekly", "monthly"] },
                "interval": { "type": "integer", "minimum": 1, "maximum": 12 },
                "by_weekday": {
                  "type": "array",
                  "items": { "type": "string", "enum": ["MO", "TU", "WE", "TH", "FR", "SA", "SU"] }
                },
                "until_local_date": { "type": "string", "description": "Último día (incluido), AAAA-MM-DD." },
                "count": { "type": "integer", "minimum": 1, "maximum": 500 }
              }
            }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "create_reminder",
        "No la llames y preguntá antes cuando falte una hora concreta. "
        + "\"A la tarde acordate de llamar a mamá\": la única respuesta correcta es \"¿a qué hora?\"; cero llamadas, sin due. "
        + "\"A las 10 avisame de la reunión\": cero llamadas; preguntá antes si es mañana a las 10 o hoy a las 22. No agendes las 10:00. "
        + "Aunque la personalidad diga que elijas la más probable, en esos mensajes la diferencia cambia el recordatorio. "
        + "\"En 20 minutos\" sí se agenda con due.in. No calculés UTC ni armes RRULE. "
        + "Después de crearlo, confirmale en una frase la hora que devuelve la tool, no la que calculaste vos.",
        Schema,
        PermissionLevel.ReversibleAction,
        "reminders",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return ToolResult.Failed(ReminderMessages.Disabled);
        }

        if (!ReminderArgumentReader.TryReadCreate(args, out var text, out var due, out var recurrence, out var error)
            || due is null)
        {
            return ToolResult.Failed(error);
        }

        var resolved = await resolver.ResolveAsync(context.UserId, due, recurrence, ct);
        if (resolved.Error is not null)
        {
            return ToolResult.Failed(resolved.Error);
        }

        var origin = origins.Current.Channel.Value;
        if (string.IsNullOrWhiteSpace(origin))
        {
            origin = "hub";
        }

        var zone = await timeZones.GetTimeZoneAsync(context.UserId, ct);
        var created = await store.CreateAsync(
            new NewReminder(
                context.UserId,
                text,
                resolved.TimeZoneId,
                resolved.LocalStart,
                resolved.OccurrenceUtc,
                resolved.Recurrence?.ToRule(resolved.LocalStart, zone),
                origin,
                context.ConversationId,
                new ReminderProvenance(context.TurnId, context.ToolCallId, ReminderFire.SourceTool)),
            options.Value.MaxActive,
            ct);

        if (created is CreateReminderResult.Rejected rejected)
        {
            return ToolResult.Failed(rejected.Reason switch
            {
                ReminderRejection.TooManyActive => ReminderMessages.TooManyActive(options.Value.MaxActive),
                _ => ReminderMessages.NotFound,
            });
        }

        var stored = (CreateReminderResult.Created)created;
        var nowLocal = ReminderTimeText.NowLocal(clock.GetUtcNow(), zone);
        return ToolResult.Ok(ReminderToolJson.Created(
            stored.Reminder.Id,
            stored.ShortRef,
            ReminderTimeText.LocalDue(resolved.LocalStart),
            ReminderTimeText.Relative(resolved.LocalStart, nowLocal),
            resolved.Recurrence?.Describe(resolved.LocalStart),
            ReminderTimeText.ChannelHint(origin),
            resolved.DstNote));
    }
}
