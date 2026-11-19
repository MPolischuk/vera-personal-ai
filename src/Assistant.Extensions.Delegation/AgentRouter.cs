using System.Collections.Concurrent;
using System.Diagnostics;
using Assistant.Abstractions.Tasks;
using Assistant.Core.Observability;

namespace Assistant.Extensions.Delegation;

/// <summary>
/// Opens <see cref="GenAiSpans.InvokeAgent"/> and runs the claimed worker.
/// The source name matches Core so the existing listener records it.
/// </summary>
internal static class AgentRouter
{
    public const string ActivitySourceName = "Assistant.Core";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static async ValueTask<AgentWorkerResult> RunAsync(
        IAgentWorker worker,
        ClaimedAgentTask task,
        string workerId,
        CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity(GenAiSpans.InvokeAgent, ActivityKind.Client);
        activity?.SetTag("gen_ai.conversation.id", task.ConversationId.ToString("D"));
        activity?.SetTag("gen_ai.agent.name", task.Kind);
        activity?.SetTag("vera.task.id", task.Id.ToString("D"));
        AgentRunScope.Set(workerId);
        try
        {
            var result = await worker.ExecuteAsync(task, ct);
            activity?.SetTag("vera.task.state", AgentTaskStatuses.Completed);
            return result;
        }
        catch (Exception)
        {
            activity?.SetTag("vera.task.state", AgentTaskStatuses.Failed);
            throw;
        }
        finally
        {
            AgentRunScope.Clear();
        }
    }
}

/// <summary>Worker id of the claim that is executing on this flow.</summary>
internal static class AgentRunScope
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string WorkerId
        => Current.Value ?? throw new InvalidOperationException("No claimed worker is running.");

    public static void Set(string workerId) => Current.Value = workerId;

    public static void Clear() => Current.Value = null;
}

/// <summary>
/// Origin turn and draft text for one task. The claim record does not carry the turn id.
/// </summary>
internal static class DraftHandoff
{
    private sealed record Slot(Guid TurnId, string? Text);

    private static readonly ConcurrentDictionary<Guid, Slot> Slots = new();

    public static void RememberTurn(Guid taskId, Guid turnId)
        => Slots.AddOrUpdate(
            taskId,
            new Slot(turnId, null),
            (_, slot) => slot with { TurnId = turnId });

    public static Guid ResolveTurn(Guid taskId)
        => Slots.TryGetValue(taskId, out var slot) ? slot.TurnId : Guid.CreateVersion7();

    public static void RememberText(Guid taskId, string text)
        => Slots.AddOrUpdate(
            taskId,
            new Slot(Guid.CreateVersion7(), text),
            (_, slot) => slot with { Text = text });

    public static bool TryTake(Guid taskId, out string text, out Guid turnId)
    {
        if (Slots.TryGetValue(taskId, out var slot) && slot.Text is not null)
        {
            Slots.TryRemove(taskId, out _);
            text = slot.Text;
            turnId = slot.TurnId;
            return true;
        }

        text = "";
        turnId = Guid.Empty;
        return false;
    }

    public static void Drop(Guid taskId) => Slots.TryRemove(taskId, out _);
}
