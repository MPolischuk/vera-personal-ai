using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Telegram;

/// <summary>
/// Checks Telegram options only when the channel is enabled, so a default install still starts.
/// </summary>
public sealed partial class TelegramOptionsValidator(IConfiguration configuration) : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (!Enum.IsDefined(options.Mode))
        {
            failures.Add("Telegram:Mode must be Webhook or Polling.");
        }

        if (string.IsNullOrWhiteSpace(options.BotToken) || !BotTokenPattern().IsMatch(options.BotToken))
        {
            failures.Add("Telegram:BotToken is required when Telegram is enabled and must look like <digits>:<secret>.");
        }

        if (string.IsNullOrWhiteSpace(options.BotUsername)
            || options.BotUsername.StartsWith('@')
            || !BotUsernamePattern().IsMatch(options.BotUsername))
        {
            failures.Add("Telegram:BotUsername is required when Telegram is enabled, without @, and must be 5–32 letters, digits, or underscores.");
        }

        if (options.Mode == TelegramReceiveMode.Webhook && !WebhookSecretPattern().IsMatch(options.WebhookSecret ?? ""))
        {
            failures.Add("Telegram:WebhookSecret is required in Webhook mode and must be 32–256 characters from [A-Za-z0-9_-].");
        }

        var publicBaseUrl = configuration["Channels:PublicBaseUrl"];
        if (!IsHttpsUrl(publicBaseUrl))
        {
            failures.Add("Channels:PublicBaseUrl must be an absolute https URL when Telegram is enabled.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsHttpsUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(uri.Host);
    }

    [GeneratedRegex(@"^\d+:[A-Za-z0-9_-]{30,}$", RegexOptions.CultureInvariant)]
    private static partial Regex BotTokenPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_]{5,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex BotUsernamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{32,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookSecretPattern();
}
