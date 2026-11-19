using System.Text.Json;
using Assistant.Core.Tools;

namespace Assistant.Extensions.Proactive;

/// <summary>
/// Turns a <c>list_events</c> payload into fenced events.
/// The title is the origin and the description is the body.
/// </summary>
internal static class CalendarEventText
{
    private const int MaxEvents = 20;

    public static string Format(string toolContent)
    {
        var events = Read(toolContent);
        if (events.Count == 0)
        {
            return ExternalContent.Wrap("calendar", "event", "list_events", toolContent);
        }

        var parts = new string[events.Count];
        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            parts[index] = ExternalContent.Wrap("calendar", "event", item.Title, item.Description ?? item.Title);
        }

        return string.Join("\n\n", parts);
    }

    private static List<CalendarEvent> Read(string toolContent)
    {
        var found = new List<CalendarEvent>();
        if (!TrySlice(toolContent, out var json))
        {
            return found;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            Walk(document.RootElement, found);
        }
        catch (JsonException)
        {
            found.Clear();
        }

        return found;
    }

    private static void Walk(JsonElement element, List<CalendarEvent> found)
    {
        if (found.Count >= MaxEvents)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryEvent(element, out var calendarEvent))
                {
                    found.Add(calendarEvent);
                    return;
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, found);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, found);
                }

                break;
        }
    }

    private static bool TryEvent(JsonElement element, out CalendarEvent calendarEvent)
    {
        calendarEvent = default;
        var title = FirstString(element, "title", "summary");
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var description = FirstString(element, "description", "body");
        calendarEvent = new CalendarEvent(title.Trim(), string.IsNullOrWhiteSpace(description) ? null : description.Trim());
        return true;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static bool TrySlice(string text, out string json)
    {
        json = string.Empty;
        var objectAt = text.IndexOf('{');
        var arrayAt = text.IndexOf('[');
        var start = objectAt < 0 ? arrayAt : arrayAt < 0 ? objectAt : Math.Min(objectAt, arrayAt);
        if (start < 0)
        {
            return false;
        }

        var end = Math.Max(text.LastIndexOf('}'), text.LastIndexOf(']'));
        if (end <= start)
        {
            return false;
        }

        json = text[start..(end + 1)];
        return true;
    }

    private readonly record struct CalendarEvent(string Title, string? Description);
}
