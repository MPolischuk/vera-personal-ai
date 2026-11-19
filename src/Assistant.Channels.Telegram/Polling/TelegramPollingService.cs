using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;
using Assistant.Channels.Telegram.Inbound;
using Assistant.Channels.Telegram.Webhook;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Polling;

/// <summary>
/// Development long polling. Writes through the same factory and inbox as the webhook.
/// Deletes the webhook on startup so <c>getUpdates</c> is allowed.
/// </summary>
internal sealed class TelegramPollingService(
    ITelegramBotClient client,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<TelegramPollingService> logger) : BackgroundService
{
    private const int LongPollSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TelegramWebhookLog.PollingDeletesWebhook(logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await client.DeleteWebhookAsync(dropPendingUpdates: false, stoppingToken);
                await TelegramBotCommands.ApplyAsync(client, logger, stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                TelegramWebhookLog.PollingFailed(logger, exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        long? offset = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await client.GetUpdatesAsync(
                    new GetUpdatesRequest
                    {
                        Offset = offset,
                        Timeout = LongPollSeconds,
                        AllowedUpdates = TelegramAllowedUpdates.All,
                    },
                    stoppingToken);
                foreach (var update in updates)
                {
                    await AcceptAsync(update, stoppingToken);
                    offset = update.UpdateId + 1;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                TelegramWebhookLog.PollingFailed(logger, exception.GetType().Name);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task AcceptAsync(TelegramUpdate update, CancellationToken cancellationToken)
    {
        var raw = TelegramUpdatePayload.Serialize(update);
        if (!TelegramUpdateEnvelopeFactory.TryCreate(raw, clock.GetUtcNow(), out var parsed))
        {
            TelegramWebhookLog.Unparseable(logger);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var acceptor = provider.GetRequiredService<IInboxAcceptor>();
        var acceptance = await acceptor.AcceptAsync(parsed.Envelope, parsed.Kind, cancellationToken);
        if (parsed.Kind is InboundUpdateKind.StoppedMessageGeneration)
        {
            await provider.GetRequiredService<TelegramStopHandler>().AfterAcceptAsync(
                acceptance,
                parsed.Kind,
                raw,
                provider.GetRequiredService<IChannelInboxStore>(),
                cancellationToken);
        }
    }
}
