using Assistant.Abstractions.Channels;
using Assistant.Core;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Webhooks;

/// <summary>
/// Ensures the fixed Index peer exists when the <c>index</c> source is enabled.
/// The peer is born approved: the signature secret is the proof, so there is no pairing code.
/// </summary>
internal sealed class IndexPeerSeed(
    IServiceScopeFactory scopes,
    IOptions<InboundWebhookOptions> options,
    TimeProvider clock,
    ILogger<IndexPeerSeed> logger) : IHostedService
{
    public const string ExternalPeerId = "index-01";

    public const string DisplayName = "Index 01";

    public static bool IsPaired(StoredChannelPeer? peer)
        => peer is { Status: ChannelPeerStatus.Approved, UserId: var userId }
           && userId == WellKnownIds.DefaultUser;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsIndexEnabled(options.Value))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IChannelPairingStore>();
        var existing = await store.FindAsync(ChannelId.Index, ExternalPeerId, cancellationToken);
        if (existing is null)
        {
            await store.InsertAsync(
                new NewChannelPeer(
                    ChannelId.Index,
                    ExternalPeerId,
                    DisplayName,
                    Username: null,
                    ChannelPeerStatus.Approved,
                    WellKnownIds.DefaultUser,
                    CodeHash: null,
                    CodeExpiresAt: null,
                    ApprovedByDeviceId: null,
                    clock.GetUtcNow()),
                cancellationToken);
            return;
        }

        if (!IsPaired(existing))
        {
            IndexRingLog.PeerNotApproved(logger);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsIndexEnabled(InboundWebhookOptions options)
    {
        foreach (var (name, source) in options.Sources)
        {
            if (source is { Enabled: true }
                && string.Equals(name, IndexRingEndpoint.SourceName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
