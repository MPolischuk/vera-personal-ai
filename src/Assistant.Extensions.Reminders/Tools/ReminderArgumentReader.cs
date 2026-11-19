using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Extensions.Reminders.Time;

namespace Assistant.Extensions.Reminders.Tools;

internal static class ReminderArgumentReader
{
    public static bool TryReadCreate(
        ToolArguments args,
        out string text,
        out DueInput? due,
        out RecurrenceInput? recurrence,
        out string error)
    {
        text = "";
        due = null;
        recurrence = null;
        error = "";
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            error = ReminderMessages.DueInvalidShape;
            return false;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name is not ("text" or "due" or "recurrence"))
            {
                error = ReminderMessages.RecurrenceInvalid;
                return false;
            }
        }

        if (!document.RootElement.TryGetProperty("text", out var textProperty)
            || textProperty.ValueKind is not JsonValueKind.String
            || textProperty.GetString() is not { } rawText
            || string.IsNullOrWhiteSpace(rawText)
            || rawText.Length > 500)
        {
            error = ReminderMessages.TextInvalid;
            return false;
        }

        text = rawText;
        if (!TryReadDue(document.RootElement, out due, out error))
        {
            return false;
        }

        if (!document.RootElement.TryGetProperty("recurrence", out var recurrenceProperty))
        {
            return true;
        }

        return TryReadRecurrence(recurrenceProperty, out recurrence, out error);
    }

    public static bool TryReadList(
        ToolArguments args, out string? status, out int? limit, out string error)
    {
        status = null;
        limit = null;
        error = "";
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            error = ReminderMessages.ListStatusInvalid;
            return false;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name is not ("status" or "limit"))
            {
                error = ReminderMessages.ListStatusInvalid;
                return false;
            }
        }

        if (document.RootElement.TryGetProperty("status", out var statusProperty))
        {
            if (statusProperty.ValueKind is not JsonValueKind.String
                || statusProperty.GetString() is not ("active" or "missed" or "all"))
            {
                error = ReminderMessages.ListStatusInvalid;
                return false;
            }

            status = statusProperty.GetString();
        }

        if (document.RootElement.TryGetProperty("limit", out var limitProperty))
        {
            if (!limitProperty.TryGetInt32(out var parsed) || parsed is < 1 or > 50)
            {
                error = ReminderMessages.ListLimitInvalid;
                return false;
            }

            limit = parsed;
        }

        return true;
    }

    public static bool TryReadCancel(
        ToolArguments args, out Guid? id, out int? shortRef, out string error)
    {
        id = null;
        shortRef = null;
        error = "";
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            error = ReminderMessages.CancelMissing;
            return false;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name is not ("id" or "short_ref"))
            {
                error = ReminderMessages.CancelMissing;
                return false;
            }
        }

        var hasId = document.RootElement.TryGetProperty("id", out var idProperty)
            && idProperty.ValueKind is not JsonValueKind.Null;
        var hasShortRef = document.RootElement.TryGetProperty("short_ref", out var shortRefProperty)
            && shortRefProperty.ValueKind is not JsonValueKind.Null;
        if (hasId == hasShortRef)
        {
            error = ReminderMessages.CancelMissing;
            return false;
        }

        if (hasId)
        {
            if (idProperty.ValueKind is not JsonValueKind.String
                || !Guid.TryParse(idProperty.GetString(), out var parsed))
            {
                error = ReminderMessages.NotFound;
                return false;
            }

            id = parsed;
            return true;
        }

        if (!shortRefProperty.TryGetInt32(out var number) || number < 1)
        {
            error = ReminderMessages.ShortRefMissing;
            return false;
        }

        shortRef = number;
        return true;
    }

    private static bool TryReadDue(JsonElement root, out DueInput? due, out string error)
    {
        due = null;
        error = "";
        if (!root.TryGetProperty("due", out var dueProperty) || dueProperty.ValueKind is not JsonValueKind.Object)
        {
            error = ReminderMessages.DueInvalidShape;
            return false;
        }

        foreach (var property in dueProperty.EnumerateObject())
        {
            if (property.Name is not ("in" or "local_datetime"))
            {
                error = ReminderMessages.DueInvalidShape;
                return false;
            }
        }

        if (!TryOptionalString(dueProperty, "in", out var duration, out var invalid)
            || !TryOptionalString(dueProperty, "local_datetime", out var local, out invalid))
        {
            error = ReminderMessages.DueInvalidShape;
            return false;
        }

        if (invalid)
        {
            error = ReminderMessages.DueInvalidShape;
            return false;
        }

        due = new DueInput(
            string.IsNullOrWhiteSpace(duration) ? null : duration.Trim(),
            string.IsNullOrWhiteSpace(local) ? null : local.Trim());
        return true;
    }

    private static bool TryReadRecurrence(JsonElement recurrence, out RecurrenceInput? input, out string error)
    {
        input = null;
        error = "";
        if (recurrence.ValueKind is not JsonValueKind.Object)
        {
            error = ReminderMessages.RecurrenceInvalid;
            return false;
        }

        string? frequency = null;
        int? interval = null;
        IReadOnlyList<string>? days = null;
        string? until = null;
        int? count = null;

        foreach (var property in recurrence.EnumerateObject())
        {
            switch (property.Name)
            {
                case "frequency":
                    if (property.Value.ValueKind is not JsonValueKind.String)
                    {
                        error = ReminderMessages.RecurrenceInvalid;
                        return false;
                    }

                    frequency = property.Value.GetString();
                    break;
                case "interval":
                    if (!property.Value.TryGetInt32(out var parsedInterval))
                    {
                        error = ReminderMessages.RecurrenceInvalid;
                        return false;
                    }

                    interval = parsedInterval;
                    break;
                case "by_weekday":
                    if (!TryWeekdays(property.Value, out days))
                    {
                        error = ReminderMessages.RecurrenceInvalid;
                        return false;
                    }

                    break;
                case "until_local_date":
                    if (property.Value.ValueKind is not JsonValueKind.String)
                    {
                        error = ReminderMessages.RecurrenceInvalid;
                        return false;
                    }

                    until = property.Value.GetString();
                    break;
                case "count":
                    if (!property.Value.TryGetInt32(out var parsedCount))
                    {
                        error = ReminderMessages.RecurrenceInvalid;
                        return false;
                    }

                    count = parsedCount;
                    break;
                default:
                    error = ReminderMessages.RecurrenceInvalid;
                    return false;
            }
        }

        input = new RecurrenceInput(frequency, interval, days, until, count);
        return true;
    }

    private static bool TryWeekdays(JsonElement value, out IReadOnlyList<string>? days)
    {
        days = null;
        if (value.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String || item.GetString() is not { } code)
            {
                return false;
            }

            parsed.Add(code);
        }

        days = parsed;
        return true;
    }

    private static bool TryOptionalString(JsonElement obj, string name, out string? value, out bool invalid)
    {
        value = null;
        invalid = false;
        if (!obj.TryGetProperty(name, out var property) || property.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind is not JsonValueKind.String)
        {
            invalid = true;
            return false;
        }

        value = property.GetString();
        return true;
    }
}
