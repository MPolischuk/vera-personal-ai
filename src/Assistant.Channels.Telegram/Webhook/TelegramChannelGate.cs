namespace Assistant.Channels.Telegram.Webhook;

/// <summary>Set when <c>getMe</c> says the token does not belong to <c>Telegram:BotUsername</c>.</summary>
public sealed class TelegramChannelGate
{
    private int _disabled;

    public bool IsDisabled => Volatile.Read(ref _disabled) == 1;

    public void Disable() => Volatile.Write(ref _disabled, 1);
}
