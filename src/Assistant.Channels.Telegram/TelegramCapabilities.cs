using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Outbound;

namespace Assistant.Channels.Telegram;

/// <summary>
/// Capabilities of the Telegram channel. Rich markdown and draft streaming apply
/// only when both options are on; otherwise the channel keeps the HTML limits.
/// </summary>
public static class TelegramCapabilities
{
    public static ChannelCapabilities Value { get; } = Create(rich: true);

    public static ChannelCapabilities For(TelegramOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Create(options.UseRichMessages && options.UseDraftStreaming);
    }

    private static ChannelCapabilities Create(bool rich) => new()
    {
        SupportsInbound = true,
        SupportsVoiceInbound = true,
        SupportsVoiceOutbound = true,
        SupportsMedia = false,
        SupportsInteractiveConfirm = true,
        SupportsNotificationActions = true,
        SupportsTypingIndicator = true,
        SupportsMarkAsRead = false,
        SupportsMessageEditing = true,
        SupportsDraftStreaming = rich,
        SupportsSilentDelivery = true,
        RequiresTemplatesOutsideWindow = false,
        ServiceWindow = null,
        RequiresAck = false,
        MaxTextLength = rich ? TelegramTextSplitter.RichMaxLength : 4096,
        MaxButtons = 8,
        MaxButtonLabelLength = 32,
        MaxActionPayloadBytes = 64,
        TextFormat = rich ? ChannelTextFormat.TelegramRichMarkdown : ChannelTextFormat.TelegramHtml,
    };
}
