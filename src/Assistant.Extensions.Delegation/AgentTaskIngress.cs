using Assistant.Abstractions.Agents;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Tasks;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Delegation;

/// <summary>
/// Records a peer proposal as <c>input-required</c> and asks the user. It does not run a worker.
/// </summary>
public sealed class AgentTaskIngress(
    IAgentTaskStore store,
    INotificationPublisher publisher,
    IOptionsMonitor<DelegationOptions> options,
    TimeProvider clock) : IAgentTaskIngress
{
    /// <summary>Value of <c>agent_tasks.kind</c> for a proposal that arrived from a peer.</summary>
    public const string TaskKind = "inbound";

    /// <inheritdoc />
    public async ValueTask<Guid> AcceptAsync(InboundTaskRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = options.CurrentValue;
        var now = clock.GetUtcNow();
        var row = await store.InsertAsync(
            new AgentTaskInsert(
                request.UserId,
                request.ConversationId,
                request.OriginTurnId,
                request.IdempotencyKey,
                TaskKind,
                request.Title,
                request.Instruction,
                settings.CostCapUsd,
                now.Add(settings.TimeCap),
                Idempotent: false,
                UsageTurnId: null,
                AgentTaskStatuses.InputRequired),
            now,
            ct);

        if (row.State != AgentTaskStatuses.InputRequired)
        {
            return row.Id;
        }

        var title = row.Title.Length <= 60 ? row.Title : row.Title[..60];
        var body = $"Un agente pidió «{title}». ¿Lo tomo?";
        await publisher.PublishAsync(
            new NotificationRequest(
                row.UserId,
                NotificationKind.ConfirmationRequest,
                $"task:{row.Id:D}:ingress",
                title,
                body.Length <= 1000 ? body : body[..1000],
                ConversationId: row.ConversationId,
                SourceType: "task",
                SourceId: row.Id.ToString("D"),
                Trust: NotificationContentTrust.Untrusted),
            ct);
        return row.Id;
    }
}
