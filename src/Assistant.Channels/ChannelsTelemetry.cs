using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Assistant.Channels;

/// <summary>Meter <c>Assistant.Channels</c>, instruments <c>assistant.notification.*</c> and <c>assistant.channel.*</c>.</summary>
public static class ChannelsTelemetry
{
    public const string MeterName = "Assistant.Channels";

    public const string ActivitySourceName = "Assistant.Channels";

    private static readonly Meter Meter = new(MeterName);

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Counter<long> Inbound =
        Meter.CreateCounter<long>("assistant.channel.inbound");

    public static readonly Counter<long> WebhookRejected =
        Meter.CreateCounter<long>("assistant.channel.webhook.rejected");

    public static readonly Counter<long> Outbound =
        Meter.CreateCounter<long>("assistant.channel.outbound");

    public static readonly Histogram<double> OutboundDuration =
        Meter.CreateHistogram<double>("assistant.channel.outbound.duration", unit: "ms");

    public static readonly Histogram<double> DeliveryLatency =
        Meter.CreateHistogram<double>("assistant.notification.delivery_latency", unit: "ms");

    public static readonly Counter<long> Status =
        Meter.CreateCounter<long>("assistant.notification.status");
}
