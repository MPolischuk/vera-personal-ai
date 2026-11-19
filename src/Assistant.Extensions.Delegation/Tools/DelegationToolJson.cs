using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Abstractions.Tasks;

namespace Assistant.Extensions.Delegation.Tools;

internal static class DelegationToolJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Listed(IReadOnlyList<AgentTaskListItem> items)
        => JsonSerializer.Serialize(new ListedBody(items), Options);

    public static string Cancelled(Guid id)
        => JsonSerializer.Serialize(new CancelledBody(id, AgentTaskStatuses.Canceled), Options);

    private sealed record ListedBody(IReadOnlyList<AgentTaskListItem> Items);

    private sealed record CancelledBody(Guid Id, string State);
}
