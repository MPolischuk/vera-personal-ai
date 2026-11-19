using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assistant.Extensions.Reminders.Tools;

internal static class ReminderToolJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Created(
        Guid id,
        int shortRef,
        string localDue,
        string relative,
        string? recurrence,
        string channelHint,
        string? dstNote)
        => JsonSerializer.Serialize(
            new CreatedBody(id, shortRef, localDue, relative, recurrence, channelHint, dstNote),
            Options);

    public static string Listed(IReadOnlyList<ListedItem> items)
        => JsonSerializer.Serialize(new ListedBody(items), Options);

    public static string Cancelled(Guid id, string text)
        => JsonSerializer.Serialize(new CancelledBody(id, "cancelled", text), Options);

    private sealed record CreatedBody(
        Guid Id,
        int ShortRef,
        string LocalDue,
        string Relative,
        string? Recurrence,
        string ChannelHint,
        string? DstNote);

    internal sealed record ListedItem(
        Guid Id,
        int? ShortRef,
        string Text,
        string Status,
        string? LocalDue,
        string? Recurrence);

    private sealed record ListedBody(IReadOnlyList<ListedItem> Reminders);

    private sealed record CancelledBody(Guid Id, string Status, string Text);
}
