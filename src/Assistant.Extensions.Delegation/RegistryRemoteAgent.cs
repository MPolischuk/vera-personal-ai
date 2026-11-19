using Assistant.Abstractions.Agents;
using Assistant.Abstractions.Tasks;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Delegation;

/// <summary>
/// Writes a <c>remote</c> task into the registry. It does not open the network.
/// The host does not construct it: <see cref="DelegationOptions.RemoteEndpoint"/> stays null.
/// </summary>
public sealed class RegistryRemoteAgent(
    IAgentTaskStore store,
    IOptionsMonitor<DelegationOptions> options,
    TimeProvider clock) : IRemoteAgent
{
    /// <summary>Value of <c>agent_tasks.kind</c> for a peer this process asked.</summary>
    public const string TaskKind = "remote";

    /// <inheritdoc />
    public async ValueTask<Guid> SubmitAsync(RemoteTaskRequest request, CancellationToken ct)
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
                AgentTaskStatuses.Submitted),
            now,
            ct);
        return row.Id;
    }
}
