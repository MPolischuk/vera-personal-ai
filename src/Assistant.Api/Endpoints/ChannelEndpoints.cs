using System.Security.Claims;
using Assistant.Abstractions.Channels;
using Assistant.Api.Authentication;
using Assistant.Channels.Telegram;
using Assistant.Channels.Telegram.Pairing;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Endpoints;

internal static class ChannelEndpoints
{
    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/channels", ListChannelsAsync);
        group.MapGet("/channels/peers", ListPeersAsync);
        group.MapPost("/channels/peers/{id:guid}/approve", ApproveAsync);
        group.MapPost("/channels/peers/{id:guid}/revoke", RevokeAsync);
        group.MapPost("/channels/peers/{id:guid}/block", BlockAsync);
        group.MapPost("/channels/telegram/link", LinkTelegramAsync);
        return group;
    }

    private static async Task<IResult> ListChannelsAsync(
        ClaimsPrincipal user,
        IChannelPeerDirectory peers,
        IOptions<TelegramOptions> telegram,
        CancellationToken ct)
    {
        var options = telegram.Value;
        var linked = await peers.FindApprovedForUserAsync(ChannelId.Telegram, user.UserId(), ct);
        return Results.Ok(new ChannelsResponse(
        [
            new ChannelStatusResponse(
                ChannelId.Telegram.Value,
                options.Enabled,
                string.IsNullOrWhiteSpace(options.BotUsername) ? null : options.BotUsername.Trim().TrimStart('@'),
                linked is not null),
        ]));
    }

    private static async Task<IResult> ListPeersAsync(IChannelPairingAdmin pairing, CancellationToken ct)
    {
        var peers = await pairing.ListPeersAsync(ct);
        return Results.Ok(new PeerListResponse(peers.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        ApprovePeerRequest? request,
        ClaimsPrincipal user,
        IChannelPairingAdmin pairing,
        IEnumerable<IPairingMessenger> messengers,
        CancellationToken ct)
    {
        if (!TryDeviceId(user, out var deviceId))
        {
            return Problem(StatusCodes.Status409Conflict, "A device is required.", "device_required");
        }

        var result = await pairing.ApproveWithCodeAsync(id, request?.Code ?? "", user.UserId(), deviceId, ct);
        switch (result)
        {
            case PeerApprovalResult.Approved approved:
                await SendWelcomeAsync(messengers, approved.Peer, ct);
                return Results.NoContent();
            case PeerApprovalResult.InvalidCode:
                return Problem(StatusCodes.Status422UnprocessableEntity, "The pairing code is invalid.", "pairing_code_invalid");
            case PeerApprovalResult.Expired:
                return Problem(StatusCodes.Status410Gone, "The pairing code has expired.", "pairing_code_expired");
            case PeerApprovalResult.Blocked:
                return Problem(StatusCodes.Status422UnprocessableEntity, "The peer is blocked.", "pairing_code_invalid");
            case PeerApprovalResult.LimitReached:
                return Problem(StatusCodes.Status409Conflict, "Another peer is already approved.", "peer_limit_reached");
            default:
                return Problem(StatusCodes.Status404NotFound, "Peer not found.", "peer_not_found");
        }
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        ClaimsPrincipal user,
        IChannelPairingAdmin pairing,
        CancellationToken ct)
    {
        var result = await pairing.RevokeAsync(id, user.UserId(), ct);
        return result is PeerMutationResult.Ok
            ? Results.NoContent()
            : Problem(StatusCodes.Status404NotFound, "Peer not found.", "peer_not_found");
    }

    private static async Task<IResult> BlockAsync(
        Guid id,
        IChannelPairingAdmin pairing,
        CancellationToken ct)
    {
        var result = await pairing.BlockAsync(id, ct);
        return result is PeerMutationResult.Ok
            ? Results.NoContent()
            : Problem(StatusCodes.Status404NotFound, "Peer not found.", "peer_not_found");
    }

    private static async Task<IResult> LinkTelegramAsync(
        ClaimsPrincipal user,
        IChannelPairingAdmin pairing,
        IOptions<TelegramOptions> telegram,
        CancellationToken ct)
    {
        var username = telegram.Value.BotUsername?.Trim().TrimStart('@');
        if (!telegram.Value.Enabled || string.IsNullOrWhiteSpace(username))
        {
            return Problem(StatusCodes.Status409Conflict, "Telegram is not available.", "telegram_unavailable");
        }

        var issued = await pairing.IssueDeepLinkAsync(user.UserId(), ChannelId.Telegram, ct);
        return Results.Ok(new TelegramLinkResponse(
            TelegramPairingMessages.DeepLink(username, issued.Token),
            issued.ExpiresAt));
    }

    private static async Task SendWelcomeAsync(
        IEnumerable<IPairingMessenger> messengers,
        ChannelPeer peer,
        CancellationToken ct)
    {
        var messenger = messengers.FirstOrDefault(item => item.Channel == peer.Channel);
        if (messenger is null)
        {
            return;
        }

        await messenger.SendAsync(peer.ExternalPeerId, new PairingNotice.Welcome(), ct);
    }

    private static bool TryDeviceId(ClaimsPrincipal user, out Guid deviceId)
    {
        var raw = user.FindFirstValue("device_id");
        return Guid.TryParse(raw, out deviceId) && deviceId != Guid.Empty;
    }

    private static IResult Problem(int status, string title, string code)
        => Results.Problem(
            title: title,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static PeerResponse ToResponse(PeerSummary peer)
        => new(
            peer.Id,
            peer.Channel.Value,
            peer.Status.ToString().ToLowerInvariant(),
            peer.DisplayName,
            peer.Username,
            peer.MaskedExternalId,
            peer.CreatedAt,
            peer.LastInboundAt);

    private sealed record ApprovePeerRequest(string? Code);

    private sealed record ChannelsResponse(IReadOnlyList<ChannelStatusResponse> Channels);

    private sealed record ChannelStatusResponse(string Id, bool Enabled, string? BotUsername, bool Linked);

    private sealed record PeerListResponse(IReadOnlyList<PeerResponse> Peers);

    private sealed record PeerResponse(
        Guid Id,
        string Channel,
        string Status,
        string? DisplayName,
        string? Username,
        string MaskedExternalId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastInboundAt);

    private sealed record TelegramLinkResponse(string Url, DateTimeOffset ExpiresAt);
}
