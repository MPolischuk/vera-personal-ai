using System.Text;
using Assistant.Abstractions.Tasks;
using Assistant.Core.Models;
using Assistant.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Delegation;

/// <summary>One utility generate with no tools. Flushes the partial while the stream grows.</summary>
internal sealed class DraftWorker(
    IModelRouter router,
    IAgentTaskStore store,
    TimeProvider clock,
    IOptionsMonitor<DelegationOptions> options) : IAgentWorker
{
    public const string WorkerKind = "draft";

    public string Kind => WorkerKind;

    public async ValueTask<AgentWorkerResult> ExecuteAsync(ClaimedAgentTask task, CancellationToken ct)
    {
        var turnId = DraftHandoff.ResolveTurn(task.Id);
        var client = router.GetClientFor(
            TaskKind.Utility,
            new ModelCallContext(task.ConversationId, turnId));
        var wrapped = ExternalContent.Wrap("delegation", "task", task.Title, task.Instruction);
        var chat = new ChatOptions
        {
            Tools = null,
            ToolMode = ChatToolMode.None,
        };

        var text = new StringBuilder();
        var sinceFlush = 0;
        var flushAt = Math.Max(1, options.CurrentValue.PartialFlushCharacters);
        UsageDetails? usage = null;
        string? model = null;

        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, wrapped)],
                           chat,
                           ct))
        {
            var piece = update.Text;
            if (!string.IsNullOrEmpty(piece))
            {
                text.Append(piece);
                sinceFlush += piece.Length;
                if (sinceFlush >= flushAt)
                {
                    await store.SavePartialAsync(
                        task.Id,
                        AgentRunScope.WorkerId,
                        text.ToString(),
                        clock.GetUtcNow(),
                        ct);
                    sinceFlush = 0;
                }
            }

            if (!string.IsNullOrEmpty(update.ModelId))
            {
                model = update.ModelId;
            }

            if (update.Contents is null)
            {
                continue;
            }

            foreach (var content in update.Contents)
            {
                if (content is UsageContent used && used.Details is not null)
                {
                    usage = used.Details;
                }
            }
        }

        var full = text.ToString();
        await store.SavePartialAsync(task.Id, AgentRunScope.WorkerId, full, clock.GetUtcNow(), ct);
        DraftHandoff.RememberText(task.Id, full);
        return Tokens(usage, model);
    }

    private static AgentWorkerResult Tokens(UsageDetails? usage, string? model)
    {
        if (usage?.InputTokenCount is null && usage?.OutputTokenCount is null)
        {
            return new AgentWorkerResult(null, null, model);
        }

        return new AgentWorkerResult(Clamp(usage?.InputTokenCount), Clamp(usage?.OutputTokenCount), model);
    }

    private static int Clamp(long? value)
        => value is null or < 0 ? 0 : (int)Math.Min(value.Value, int.MaxValue);
}
