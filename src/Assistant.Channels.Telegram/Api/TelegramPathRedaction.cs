using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Assistant.Channels.Telegram.Api;

/// <summary>
/// Replaces a Bot API token embedded in a URL path (<c>/bot&lt;token&gt;/</c> or <c>/file/bot&lt;token&gt;/</c>).
/// </summary>
public static partial class TelegramPathRedaction
{
    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return BotTokenInPath().Replace(value, "/bot***");
    }

    public static void RedactActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        foreach (var tag in activity.TagObjects.ToArray())
        {
            if (tag.Value is string text)
            {
                var redacted = Redact(text);
                if (!string.Equals(redacted, text, StringComparison.Ordinal))
                {
                    activity.SetTag(tag.Key, redacted);
                }
            }
        }

        if (activity.DisplayName.Contains("/bot", StringComparison.Ordinal))
        {
            activity.DisplayName = Redact(activity.DisplayName);
        }

        if (activity.StatusDescription is { } status && status.Contains("/bot", StringComparison.Ordinal))
        {
            activity.SetStatus(activity.Status, Redact(status));
        }
    }

    [GeneratedRegex(@"/bot\d+:[A-Za-z0-9_-]{30,}", RegexOptions.CultureInvariant)]
    private static partial Regex BotTokenInPath();
}
