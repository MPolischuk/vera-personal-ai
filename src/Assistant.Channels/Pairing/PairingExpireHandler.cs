using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Pairing;

/// <summary>Job <c>channel.pairing.expire</c>. An expired pending code is replaced.</summary>
public sealed class PairingExpireHandler(
    IChannelPairingAdmin pairing,
    IEnumerable<IPairingMessenger> messengers,
    ILogger<PairingExpireHandler> logger) : IJobHandler
{
    public const string KindName = PairingExpireSchedule.Kind;

    public string Kind => KindName;

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        if (!PairingExpireSchedule.TryRead(context.PayloadJson, out var peerId))
        {
            return new JobOutcome.Fail("payload");
        }

        var rotation = await pairing.RotateExpiredAsync(peerId, ct);
        switch (rotation)
        {
            case CodeRotation.Waiting waiting:
                return new JobOutcome.Reschedule(waiting.ExpiresAt);
            case CodeRotation.Rotated rotated:
                await SendAsync(rotated, ct);
                return new JobOutcome.Succeeded();
            default:
                return new JobOutcome.Succeeded();
        }
    }

    private async ValueTask SendAsync(CodeRotation.Rotated rotated, CancellationToken ct)
    {
        var messenger = messengers.FirstOrDefault(item => item.Channel == rotated.Channel);
        if (messenger is null)
        {
            PairingLog.ReplySkipped(logger, rotated.Channel.Value);
            return;
        }

        try
        {
            await messenger.SendAsync(
                rotated.ExternalPeerId,
                new PairingNotice.NewCode(rotated.Code, rotated.ExpiresAt),
                ct);
            if (pairing is IPairingService service)
            {
                await service.NoteReminderSentAsync(rotated.Channel, rotated.ExternalPeerId, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PairingLog.ReplyFailed(logger, exception.GetType().Name);
        }
    }
}
