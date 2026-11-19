namespace Assistant.Abstractions.Channels;

/// <summary>
/// Text format accepted by the channel.
/// </summary>
public enum ChannelTextFormat
{
    Plain,
    TelegramHtml,
    WhatsAppMarkup,
    TelegramRichMarkdown
}

/// <summary>
/// Capabilities of a channel. A record with init accessors, not <c>[Flags]</c>,
/// because it carries numeric limits and <see cref="TimeSpan"/>. Each adapter
/// declares what its channel supports.
/// </summary>
public sealed record ChannelCapabilities
{
    /// <summary>Whether the user can write through this channel.</summary>
    public bool SupportsInbound { get; init; }

    /// <summary>Whether the user can send voice notes.</summary>
    public bool SupportsVoiceInbound { get; init; }

    /// <summary>Whether Vera can reply with voice.</summary>
    public bool SupportsVoiceOutbound { get; init; }

    /// <summary>Whether the channel accepts inbound images or documents. Stage 04b does not process them.</summary>
    public bool SupportsMedia { get; init; }

    /// <summary>Whether interactive buttons are available for PermissionEngine confirmations.</summary>
    public bool SupportsInteractiveConfirm { get; init; }

    /// <summary>Whether generic notification buttons are available (Done / +10 min).</summary>
    public bool SupportsNotificationActions { get; init; }

    /// <summary>Whether a typing indicator is available.</summary>
    public bool SupportsTypingIndicator { get; init; }

    /// <summary>Whether messages can be marked as read.</summary>
    public bool SupportsMarkAsRead { get; init; }

    /// <summary>Whether sent messages can be edited (for example, removing buttons after a reply).</summary>
    public bool SupportsMessageEditing { get; init; }

    /// <summary>Whether an ephemeral draft can stream while the reply is written.</summary>
    public bool SupportsDraftStreaming { get; init; }

    /// <summary>Whether delivery can be silent, without a sound.</summary>
    public bool SupportsSilentDelivery { get; init; }

    /// <summary>
    /// Whether approved templates are required outside the service window (WhatsApp).
    /// </summary>
    public bool RequiresTemplatesOutsideWindow { get; init; }

    /// <summary>
    /// Service window for free-form messages (WhatsApp: 24 h; otherwise null).
    /// </summary>
    public TimeSpan? ServiceWindow { get; init; }

    /// <summary>
    /// Whether client acknowledgement is required before delivery counts (hub).
    /// </summary>
    public bool RequiresAck { get; init; }

    /// <summary>Maximum text length, in characters.</summary>
    public int MaxTextLength { get; init; }

    /// <summary>Maximum number of buttons in one message.</summary>
    public int MaxButtons { get; init; }

    /// <summary>Maximum length of a button label.</summary>
    public int MaxButtonLabelLength { get; init; }

    /// <summary>Maximum length of an action payload, in bytes.</summary>
    public int MaxActionPayloadBytes { get; init; }

    /// <summary>Text format accepted by the channel.</summary>
    public ChannelTextFormat TextFormat { get; init; }
}
