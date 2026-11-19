using System.Text;
using System.Text.Json;
using Assistant.Channels.Telegram.Api.Dto;

namespace Assistant.Channels.Telegram.Polling;

/// <summary>
/// Rebuilds the raw update JSON from the fields the client kept.
/// Undefined elements are omitted so the inbox stores the same shape the webhook receives.
/// </summary>
internal static class TelegramUpdatePayload
{
    public static string Serialize(TelegramUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("update_id", update.UpdateId);
            Write(writer, "message", update.Message);
            Write(writer, "edited_message", update.EditedMessage);
            Write(writer, "callback_query", update.CallbackQuery);
            Write(writer, "my_chat_member", update.MyChatMember);
            WriteStopped(writer, update.StoppedMessageGeneration);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, string name, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return;
        }

        writer.WritePropertyName(name);
        value.WriteTo(writer);
    }

    private static void WriteStopped(Utf8JsonWriter writer, MessageGenerationStopped? stopped)
    {
        if (stopped is null)
        {
            return;
        }

        writer.WritePropertyName("stopped_message_generation");
        writer.WriteStartObject();
        writer.WritePropertyName("chat");
        writer.WriteStartObject();
        writer.WriteNumber("id", stopped.Chat.Id);
        if (!string.IsNullOrEmpty(stopped.Chat.Type))
        {
            writer.WriteString("type", stopped.Chat.Type);
        }

        writer.WriteEndObject();
        writer.WriteNumber("draft_id", stopped.DraftId);
        if (stopped.MessageThreadId is int threadId)
        {
            writer.WriteNumber("message_thread_id", threadId);
        }

        writer.WriteEndObject();
    }
}
