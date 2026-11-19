using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Webhooks;
using Assistant.Core;
using Assistant.Core.Events;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Webhooks;

/// <summary>
/// <c>POST /api/hooks/index</c>. Anonymous, behind the inbound signature middleware.
/// </summary>
internal static class IndexRingEndpoint
{
    public const string SourceName = "index";

    public const string TranscriptRequired = "transcript_required";

    public const string PeerNotPaired = "peer_not_paired";

    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost("/api/hooks/index", HandleAsync)
            .AllowAnonymous()
            .RequireVerifiedInboundWebhook();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IChannelPairingStore peers,
        ITurnIngress ingress,
        IEventBus bus,
        INotificationPublisher notifications,
        IOptions<InboundWebhookOptions> options,
        ILogger<IndexRingSink> logger,
        CancellationToken cancellationToken)
    {
        var feature = context.Features.Get<IVerifiedInboundWebhookFeature>();
        if (feature is null || !string.Equals(feature.Source, SourceName, StringComparison.Ordinal))
        {
            return Results.Text(
                InboundWebhookCodes.SignatureInvalid,
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (IndexRingPayload.IsTest(context.Request))
        {
            return Results.NoContent();
        }

        var transcription = await IndexRingPayload.ReadTranscriptionAsync(
            context.Request,
            MaxBody(options.Value),
            cancellationToken);
        if (transcription is null)
        {
            return Results.Text(TranscriptRequired, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var peer = await peers.FindAsync(ChannelId.Index, IndexPeerSeed.ExternalPeerId, cancellationToken);
        if (!IndexPeerSeed.IsPaired(peer))
        {
            return Results.Text(PeerNotPaired, statusCode: StatusCodes.Status403Forbidden);
        }

        var request = new ChannelTurnRequest(
            ChannelId.Index,
            peer!.Id,
            WellKnownIds.DefaultUser,
            feature.DeliveryId,
            [new InboundPart.Text(transcription, ContentTrust.OwnerAuthored)],
            feature.Timestamp);
        var sink = new IndexRingSink(notifications, logger, WellKnownIds.DefaultUser, feature.DeliveryId);
        var ack = await ingress.SubmitAsync(request, sink, cancellationToken);
        if (!ack.Accepted)
        {
            return Results.Text(
                ack.RejectionCode ?? "rejected",
                statusCode: StatusCodes.Status409Conflict);
        }

        await bus.PublishAsync(
            new InboundFactReceived(WellKnownIds.DefaultUser, SourceName, feature.DeliveryId, transcription),
            cancellationToken);
        return Results.Text(InboundWebhookCodes.Accepted, statusCode: StatusCodes.Status202Accepted);
    }

    private static int MaxBody(InboundWebhookOptions options)
    {
        foreach (var (name, source) in options.Sources)
        {
            if (source is { Enabled: true }
                && string.Equals(name, SourceName, StringComparison.OrdinalIgnoreCase))
            {
                return source.MaxBodyBytes;
            }
        }

        return InboundWebhookSourceOptions.DefaultMaxBodyBytes;
    }
}
