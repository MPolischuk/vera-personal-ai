namespace Assistant.Api.Webhooks;

/// <summary>Stable response and log codes. None of them carry the body, the signature, or a secret.</summary>
internal static class InboundWebhookCodes
{
    public const string SignatureInvalid = "webhook_signature_invalid";

    public const string TimestampOutOfRange = "webhook_timestamp_out_of_range";

    public const string Replayed = "webhook_replayed";

    public const string BodyTooLarge = "webhook_body_too_large";

    public const string UnknownSource = "webhook_unknown_source";

    public const string Accepted = "accepted";
}
