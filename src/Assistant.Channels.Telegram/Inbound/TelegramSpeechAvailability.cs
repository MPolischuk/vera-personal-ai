namespace Assistant.Channels.Telegram.Inbound;

/// <summary>
/// True when <c>Assistant:Speech:Stt:Provider</c> is set, which is what registers <c>ISpeechToTextProvider</c>.
/// The adapter does not reference the speech stack.
/// </summary>
internal sealed class TelegramSpeechAvailability(bool isConfigured)
{
    public bool IsConfigured { get; } = isConfigured;
}
