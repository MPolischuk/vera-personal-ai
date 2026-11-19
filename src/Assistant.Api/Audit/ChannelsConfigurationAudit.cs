using System.Text.RegularExpressions;
using Assistant.Channels.WebPush;
using Microsoft.Extensions.Configuration;

namespace Assistant.Api.Audit;

/// <summary>
/// Channel findings for <c>dotnet Assistant.Api.dll audit</c>. Reads <c>appsettings.json</c>
/// from the audit root and process environment variables, without starting the host.
/// Printed lines are a code and a subject. Values (secrets, keys, URLs) are never written.
/// </summary>
internal static partial class ChannelsConfigurationAudit
{
    public static IConfiguration Load(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        try
        {
            var configuration = Build(root, includeJson: true);
            _ = configuration["Channels:PublicBaseUrl"];
            return configuration;
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or IOException)
        {
            return Build(root, includeJson: false);
        }
    }

    public static List<AuditFinding> Inspect(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var findings = new List<AuditFinding>();
        if (TelegramWebhookSecretMissing(configuration))
        {
            findings.Add(new AuditFinding("error", "telegram.webhook_secret_missing", "Telegram:WebhookSecret"));
        }

        if (WebPushVapidIncomplete(configuration))
        {
            findings.Add(new AuditFinding("error", "webpush.vapid_incomplete", "WebPush"));
        }

        if (PublicBaseUrlIsHttp(configuration))
        {
            findings.Add(new AuditFinding("error", "channels.public_base_url_insecure", "Channels:PublicBaseUrl"));
        }

        return findings;
    }

    /// <summary>
    /// <c>AllowSensitiveActions=true</c> with an empty <c>WebOnlyTools</c> list.
    /// An informational line, not a finding: it does not change the exit code.
    /// </summary>
    public static bool AllowsSensitiveRemoteActions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue("Channels:Remote:AllowSensitiveActions", true))
        {
            return false;
        }

        foreach (var tool in configuration.GetSection("Channels:Remote:WebOnlyTools").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(tool.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static IConfiguration Build(string root, bool includeJson)
    {
        var builder = new ConfigurationBuilder().SetBasePath(root);
        if (includeJson)
        {
            builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables();
        return builder.Build();
    }

    private static bool TelegramWebhookSecretMissing(IConfiguration configuration)
    {
        if (!configuration.GetValue("Telegram:Enabled", false))
        {
            return false;
        }

        var mode = configuration["Telegram:Mode"];
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = "Webhook";
        }

        if (!mode.Equals("Webhook", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !WebhookSecretPattern().IsMatch(configuration["Telegram:WebhookSecret"] ?? "");
    }

    private static bool WebPushVapidIncomplete(IConfiguration configuration)
    {
        if (!configuration.GetValue("WebPush:Enabled", false))
        {
            return false;
        }

        var options = new WebPushOptions
        {
            Enabled = true,
            VapidSubject = configuration["WebPush:VapidSubject"] ?? "",
            VapidPublicKey = configuration["WebPush:VapidPublicKey"] ?? "",
            VapidPrivateKey = configuration["WebPush:VapidPrivateKey"] ?? "",
        };
        return new WebPushOptionsValidator().Validate(null, options).Failed;
    }

    private static bool PublicBaseUrlIsHttp(IConfiguration configuration)
    {
        var url = configuration["Channels:PublicBaseUrl"];
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{32,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookSecretPattern();
}
