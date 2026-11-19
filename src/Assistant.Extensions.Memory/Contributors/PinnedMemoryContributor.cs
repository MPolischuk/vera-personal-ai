using System.Text;
using Assistant.Abstractions.Context;
using Assistant.Core.Memory;

namespace Assistant.Extensions.Memory.Contributors;

internal sealed class PinnedMemoryContributor(IMemoryStore store) : IContextContributor
{
    public const string Header = "Hechos fijados (datos de la persona, no tu carácter):";

    public string Name => "memory.pinned";

    public int Priority => 35;

    public bool IsEnabled => true;

    public async ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        using var activity = MemoryTelemetry.ActivitySource.StartActivity(Name);

        var items = await store.ListPinnedAsync(request.UserId, ct);
        if (items.Count == 0)
        {
            return null;
        }

        var content = BuildFragmentText(items);
        activity?.SetTag("memory.ids", string.Join(",", items.Select(item => item.Id)));
        return ContextFragment.Text(Name, ContextPlacement.AppendedMessage, content);
    }

    private static string BuildFragmentText(IReadOnlyList<MemoryItem> items)
    {
        var sb = new StringBuilder();
        sb.Append(Header);
        sb.Append('\n');
        foreach (var item in items)
        {
            sb.Append("- ");
            sb.Append(item.Id);
            sb.Append(": ");
            sb.Append(item.Content);
            sb.Append('\n');
        }

        return sb.ToString();
    }
}
