namespace Assistant.Abstractions.Channels;

/// <summary>
/// Stable channel identifier. Lowercase, no spaces.
/// </summary>
public readonly record struct ChannelId(string Value)
{
    public static readonly ChannelId Hub = new("hub");
    public static readonly ChannelId WebPush = new("webpush");
    public static readonly ChannelId Telegram = new("telegram");
    public static readonly ChannelId WhatsApp = new("whatsapp");
    public static readonly ChannelId Index = new("index");

    public override string ToString() => Value;
}
