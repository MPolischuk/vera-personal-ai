using Assistant.Abstractions.Channels;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Pairing;

/// <summary>Whether the inbox should continue into the channel handler.</summary>
/// <param name="ContinueToHandler">True when the peer is already approved.</param>
/// <param name="Metric">Inbound metric when <paramref name="ContinueToHandler"/> is false.</param>
internal readonly record struct PairingInboxResult(bool ContinueToHandler, string Metric);

/// <summary>Runs pairing for one private message and sends the fixed reply.</summary>
internal sealed class PairingInboxStep(
    IPairingService pairing,
    IEnumerable<IPairingMessenger> messengers,
    ILogger<PairingInboxStep> logger)
{
    public async ValueTask<PairingInboxResult> ApplyAsync(
        InboundEnvelope envelope,
        PairingAdmission admission,
        CancellationToken ct)
    {
        if (PairingAdmissionReader.TryReadStartToken(admission.Text, out var token))
        {
            var redeemed = await pairing.RedeemDeepLinkAsync(
                envelope.Channel,
                envelope.ExternalPeerId,
                admission.DisplayName,
                admission.Username,
                token,
                ct);
            switch (redeemed)
            {
                case PairingDecision.Approved:
                    return new PairingInboxResult(true, "turn");
                case PairingDecision.Silent { Reason: PairingReasons.Welcomed }:
                    await SendAsync(envelope, new PairingNotice.Welcome(), noteReminder: false, ct);
                    return new PairingInboxResult(false, "command");
                case PairingDecision.Silent { Reason: PairingReasons.Blocked }:
                case PairingDecision.Silent { Reason: PairingReasons.Flood }:
                case PairingDecision.Silent { Reason: PairingReasons.Limit }:
                    return new PairingInboxResult(false, "ignored");
                case PairingDecision.Silent:
                    await SendAsync(envelope, new PairingNotice.LinkRejected(), noteReminder: false, ct);
                    break;
            }
        }

        var decision = await pairing.EvaluateAsync(
            envelope.Channel,
            envelope.ExternalPeerId,
            admission.DisplayName,
            admission.Username,
            ct);
        switch (decision)
        {
            case PairingDecision.Approved:
                return new PairingInboxResult(true, "turn");
            case PairingDecision.NewCode code:
                await SendAsync(envelope, new PairingNotice.NewCode(code.Code, code.ExpiresAt), noteReminder: true, ct);
                return new PairingInboxResult(false, "unpaired");
            case PairingDecision.StillPending { RemindCode: { } remind }:
                await SendAsync(envelope, new PairingNotice.Reminder(remind), noteReminder: true, ct);
                return new PairingInboxResult(false, "unpaired");
            case PairingDecision.StillPending:
                return new PairingInboxResult(false, "unpaired");
            default:
                return new PairingInboxResult(false, "ignored");
        }
    }

    private async ValueTask SendAsync(
        InboundEnvelope envelope,
        PairingNotice notice,
        bool noteReminder,
        CancellationToken ct)
    {
        var messenger = messengers.FirstOrDefault(item => item.Channel == envelope.Channel);
        if (messenger is null)
        {
            PairingLog.ReplySkipped(logger, envelope.Channel.Value);
        }
        else
        {
            try
            {
                await messenger.SendAsync(envelope.ExternalPeerId, notice, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PairingLog.ReplyFailed(logger, exception.GetType().Name);
                throw;
            }
        }

        if (noteReminder)
        {
            await pairing.NoteReminderSentAsync(envelope.Channel, envelope.ExternalPeerId, ct);
        }
    }
}
