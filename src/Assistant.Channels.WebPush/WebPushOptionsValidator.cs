using Microsoft.Extensions.Options;

namespace Assistant.Channels.WebPush;

/// <summary>
/// Checks Web Push options only when the channel is enabled, so a default install still starts.
/// A mismatched VAPID pair fails startup.
/// </summary>
public sealed class WebPushOptionsValidator : IValidateOptions<WebPushOptions>
{
    public ValidateOptionsResult Validate(string? name, WebPushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (!IsSubject(options.VapidSubject))
        {
            failures.Add("WebPush:VapidSubject is required when Web Push is enabled and must be a mailto: address or an absolute https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.VapidPublicKey) || string.IsNullOrWhiteSpace(options.VapidPrivateKey))
        {
            failures.Add("WebPush:VapidPublicKey and WebPush:VapidPrivateKey are required when Web Push is enabled.");
        }
        else if (!VapidKeys.PairMatches(options.VapidPublicKey, options.VapidPrivateKey))
        {
            failures.Add("WebPush:VapidPublicKey does not match WebPush:VapidPrivateKey.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSubject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("mailto:", StringComparison.Ordinal))
        {
            var address = value["mailto:".Length..].Trim();
            var at = address.IndexOf('@');
            return at > 0
                && at < address.Length - 1
                && !address.Contains(' ', StringComparison.Ordinal)
                && !address.Contains('\r', StringComparison.Ordinal)
                && !address.Contains('\n', StringComparison.Ordinal);
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(uri.Host);
    }
}
