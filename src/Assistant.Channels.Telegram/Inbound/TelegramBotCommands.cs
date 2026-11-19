using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Webhook;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Inbound;

/// <summary>Publishes the final command menu. A failure does not disable the channel.</summary>
internal static class TelegramBotCommands
{
    public static async Task ApplyAsync(ITelegramBotClient client, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            await client.SetMyCommandsAsync(TelegramCommands.Menu, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramWebhookLog.CommandsFailed(logger, exception.GetType().Name);
        }
    }
}
