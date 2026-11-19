using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Inbound;
using Assistant.Channels.Telegram.Api.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Telegram.Webhook;

/// <summary>
/// On startup in webhook mode: <c>getMe</c>, then <c>setWebhook</c> only when the current registration differs.
/// <c>Telegram:ManageWebhook=false</c> skips the webhook calls. Does not call <c>setMyCommands</c>.
/// </summary>
internal sealed class TelegramWebhookReconciler(
    ITelegramBotClient client,
    IOptions<TelegramOptions> options,
    IConfiguration configuration,
    TelegramChannelGate gate,
    TimeProvider clock,
    ILogger<TelegramWebhookReconciler> logger) : BackgroundService
{
    public static readonly TimeSpan RecentErrorWindow = TimeSpan.FromHours(24);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramWebhookLog.ReconcileFailed(logger, exception.GetType().Name);
        }

        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        TelegramUser user;
        try
        {
            user = await client.GetMeAsync(cancellationToken);
        }
        catch (TelegramApiException exception) when (exception.ErrorCode == 401)
        {
            TelegramWebhookLog.TokenRejected(logger, exception.ErrorCode ?? 401);
            gate.Disable();
            return;
        }
        catch (Exception exception)
        {
            TelegramWebhookLog.GetMeFailed(logger, exception.GetType().Name);
            return;
        }

        if (!string.Equals(user.Username, options.Value.BotUsername, StringComparison.OrdinalIgnoreCase))
        {
            TelegramWebhookLog.UsernameMismatch(logger);
            gate.Disable();
            return;
        }

        await TelegramBotCommands.ApplyAsync(client, logger, cancellationToken);

        if (!options.Value.ManageWebhook)
        {
            return;
        }

        var info = await client.GetWebhookInfoAsync(cancellationToken);
        var expectedUrl = TelegramWebhookAddress.Build(configuration["Channels:PublicBaseUrl"]);
        if (Matches(info, expectedUrl, options.Value.MaxConnections, clock.GetUtcNow()))
        {
            TelegramWebhookLog.Unchanged(logger, info.PendingUpdateCount, info.LastErrorMessage ?? "");
            return;
        }

        await client.SetWebhookAsync(
            new SetWebhookRequest
            {
                Url = expectedUrl,
                SecretToken = options.Value.WebhookSecret,
                MaxConnections = options.Value.MaxConnections,
                AllowedUpdates = TelegramAllowedUpdates.All,
                DropPendingUpdates = false,
            },
            cancellationToken);
        TelegramWebhookLog.Registered(logger, info.PendingUpdateCount, info.LastErrorMessage ?? "");
    }

    internal static bool Matches(WebhookInfo info, string expectedUrl, int maxConnections, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!string.Equals(info.Url, expectedUrl, StringComparison.Ordinal))
        {
            return false;
        }

        if (info.MaxConnections != maxConnections)
        {
            return false;
        }

        if (!SameUpdates(info.AllowedUpdates))
        {
            return false;
        }

        return !HasRecentError(info.LastErrorDate, now);
    }

    private static bool SameUpdates(IReadOnlyList<string>? actual)
    {
        if (actual is null || actual.Count != TelegramAllowedUpdates.All.Count)
        {
            return false;
        }

        var expected = new HashSet<string>(TelegramAllowedUpdates.All, StringComparer.Ordinal);
        return actual.All(expected.Contains);
    }

    private static bool HasRecentError(long? lastErrorDate, DateTimeOffset now)
    {
        if (lastErrorDate is not > 0)
        {
            return false;
        }

        var when = DateTimeOffset.FromUnixTimeSeconds(lastErrorDate.Value);
        return now - when <= RecentErrorWindow;
    }
}
