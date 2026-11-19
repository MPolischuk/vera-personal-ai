using Microsoft.Extensions.Options;

namespace Assistant.Api.Webhooks;

internal sealed class InboundWebhookOptions
{
    public const string SectionName = "Webhooks:Inbound";

    public Dictionary<string, InboundWebhookSourceOptions> Sources { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class InboundWebhookSourceOptions
{
    public const int DefaultMaxBodyBytes = 1024 * 1024;

    public bool Enabled { get; set; }

    public string Format { get; set; } = StandardWebhooksVerifier.Format;

    public List<string> Secrets { get; set; } = [];

    public int MaxBodyBytes { get; set; } = DefaultMaxBodyBytes;
}

internal sealed class InboundWebhookOptionsValidator : IValidateOptions<InboundWebhookOptions>
{
    public ValidateOptionsResult Validate(string? name, InboundWebhookOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        foreach (var (sourceName, source) in options.Sources ?? [])
        {
            if (string.IsNullOrWhiteSpace(sourceName) || sourceName.Length > Assistant.Abstractions.Webhooks.InboundWebhookLimits.MaxSourceLength)
            {
                failures.Add("Webhooks:Inbound:Sources names must be 1 to 64 characters.");
                continue;
            }

            if (source is null)
            {
                failures.Add($"Webhooks:Inbound:Sources:{sourceName} is required.");
                continue;
            }

            if (!string.Equals(source.Format, StandardWebhooksVerifier.Format, StringComparison.Ordinal)
                && !string.Equals(source.Format, PebbleIndexSignatureVerifier.Format, StringComparison.Ordinal))
            {
                failures.Add(
                    $"Webhooks:Inbound:Sources:{sourceName}:Format must be standard or pebble-index.");
            }

            if (source.MaxBodyBytes <= 0)
            {
                failures.Add($"Webhooks:Inbound:Sources:{sourceName}:MaxBodyBytes must be positive.");
            }

            var present = (source.Secrets ?? []).Where(secret => !string.IsNullOrWhiteSpace(secret)).ToList();
            if (present.Count > 2)
            {
                failures.Add($"Webhooks:Inbound:Sources:{sourceName}:Secrets accepts at most two values.");
            }

            for (var index = 0; index < present.Count; index++)
            {
                if (!WebhookSecrets.TryDecode(present[index], out _))
                {
                    failures.Add(
                        $"Webhooks:Inbound:Sources:{sourceName}:Secrets[{index}] must be whsec_ followed by base64 of 24 to 64 bytes.");
                }
            }

            if (source.Enabled && present.Count == 0)
            {
                failures.Add(
                    $"Webhooks:Inbound:Sources:{sourceName}:Secrets is required when the source is enabled.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
