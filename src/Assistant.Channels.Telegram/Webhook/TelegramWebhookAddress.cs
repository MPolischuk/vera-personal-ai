namespace Assistant.Channels.Telegram.Webhook;

/// <summary>Public webhook URL derived from <c>Channels:PublicBaseUrl</c>.</summary>
public static class TelegramWebhookAddress
{
    public const string Path = "/webhooks/telegram";

    public static string Build(string? publicBaseUrl)
    {
        var trimmed = (publicBaseUrl ?? "").Trim().TrimEnd('/');
        return trimmed + Path;
    }
}
