using System.Text;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>A button payload Telegram accepted. Invalid wire data is <see langword="null"/>.</summary>
internal abstract record CallbackPayload
{
    private CallbackPayload()
    {
    }

    /// <summary>A confirmation button. <paramref name="Approved"/> is <c>y</c> or <c>n</c>.</summary>
    internal sealed record Confirmation(Guid Id, bool Approved) : CallbackPayload;

    /// <summary>A notification button. <paramref name="ActionId"/> is <c>done</c> or <c>snooze:10m</c>.</summary>
    internal sealed record Notification(Guid Id, string ActionId) : CallbackPayload;
}

/// <summary>
/// Encodes button payloads inside Telegram's 64-byte <c>callback_data</c> limit.
/// Confirmation: <c>c:{guid:N}:{y|n}</c>. Notification: <c>n:{guid:N}:{done|s10}</c>.
/// </summary>
internal static class CallbackData
{
    public const int MaxBytes = 64;

    public static string EncodeConfirmation(Guid id, bool approved)
        => "c:" + id.ToString("N") + ":" + (approved ? "y" : "n");

    public static string? EncodeNotification(Guid notificationId, string actionId)
    {
        var code = actionId switch
        {
            "done" => "done",
            "snooze:10m" => "s10",
            _ => null,
        };
        if (code is null)
        {
            return null;
        }

        var data = "n:" + notificationId.ToString("N") + ":" + code;
        return Encoding.UTF8.GetByteCount(data) <= MaxBytes ? data : null;
    }

    public static CallbackPayload? Decode(string? data)
    {
        if (string.IsNullOrEmpty(data) || Encoding.UTF8.GetByteCount(data) > MaxBytes)
        {
            return null;
        }

        var parts = data.Split(':');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            return null;
        }

        return parts[0] switch
        {
            "c" when parts[2] == "y" => new CallbackPayload.Confirmation(id, Approved: true),
            "c" when parts[2] == "n" => new CallbackPayload.Confirmation(id, Approved: false),
            "n" when parts[2] == "done" => new CallbackPayload.Notification(id, "done"),
            "n" when parts[2] == "s10" => new CallbackPayload.Notification(id, "snooze:10m"),
            _ => null,
        };
    }
}
