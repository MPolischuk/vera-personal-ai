namespace Assistant.Api.Webhooks;

internal static partial class InboundWebhookLog
{
    [LoggerMessage(
        EventId = 910,
        Level = LogLevel.Information,
        Message = "Inbound webhook {Source} {Code} {DeliveryId}")]
    public static partial void Decided(ILogger logger, string source, string code, string deliveryId);

    [LoggerMessage(
        EventId = 911,
        Level = LogLevel.Warning,
        Message = "Inbound webhook purge seed failed ({ExceptionType})")]
    public static partial void PurgeSeedFailed(ILogger logger, string exceptionType);
}
