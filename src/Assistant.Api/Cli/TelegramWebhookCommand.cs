using Assistant.Channels.Telegram;
using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;
using Assistant.Channels.Telegram.Webhook;
using Assistant.Api.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Cli;

internal static class TelegramWebhookCommand
{
    public static bool IsRequest(string[] args)
        => args is ["telegram-webhook", "info" or "set" or "delete"];

    public static async Task<int> RunAsync(
        string[] args,
        IServiceProvider services,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        var stdout = output ?? Console.Out;
        var stderr = error ?? Console.Error;
        var options = services.GetRequiredService<IOptions<TelegramOptions>>().Value;
        if (!options.Enabled)
        {
            stderr.WriteLine("Telegram is disabled.");
            return 1;
        }

        var client = services.GetRequiredService<ITelegramBotClient>();
        var publicBaseUrl = services.GetRequiredService<IOptions<ChannelsOptions>>().Value.PublicBaseUrl;
        try
        {
            switch (args[1])
            {
                case "info":
                    await WriteInfoAsync(client, stdout, CancellationToken.None);
                    return 0;
                case "set":
                    await client.SetWebhookAsync(
                        new SetWebhookRequest
                        {
                            Url = TelegramWebhookAddress.Build(publicBaseUrl),
                            SecretToken = options.WebhookSecret,
                            MaxConnections = options.MaxConnections,
                            AllowedUpdates = TelegramAllowedUpdates.All,
                            DropPendingUpdates = false,
                        },
                        CancellationToken.None);
                    await WriteInfoAsync(client, stdout, CancellationToken.None);
                    return 0;
                case "delete":
                    await client.DeleteWebhookAsync(dropPendingUpdates: false, CancellationToken.None);
                    stdout.WriteLine("deleted");
                    return 0;
                default:
                    stderr.WriteLine("usage: dotnet Assistant.Api.dll telegram-webhook info|set|delete");
                    return 2;
            }
        }
        catch (TelegramApiException exception)
        {
            stderr.WriteLine($"Telegram API error {exception.ErrorCode}.");
            return 1;
        }
    }

    private static async Task WriteInfoAsync(ITelegramBotClient client, TextWriter output, CancellationToken cancellationToken)
    {
        var info = await client.GetWebhookInfoAsync(cancellationToken);
        output.WriteLine($"url: {info.Url}");
        output.WriteLine($"pending_update_count: {info.PendingUpdateCount}");
        output.WriteLine($"last_error_message: {info.LastErrorMessage}");
    }
}
