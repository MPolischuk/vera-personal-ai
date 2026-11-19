using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assistant.Abstractions.Agents;

/// <summary>
/// One skill announced to a peer. The card has no tool list.
/// </summary>
public sealed record AgentSkill(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);

/// <summary>
/// Peer card. It serializes the name, the description, and the skills.
/// </summary>
public sealed record AgentCard(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("skills")] IReadOnlyList<AgentSkill> Skills);

/// <summary>
/// Builds a card from skills. There is no tool parameter and no registry.
/// </summary>
public static class AgentCardBuilder
{
    /// <summary>Name written on every card.</summary>
    public const string AgentName = "Vera";

    /// <summary>Description written on every card. It does not name a tool.</summary>
    public const string AgentDescription =
        "Asistente personal. Un par puede proponer un pedido; la persona lo confirma.";

    /// <summary>Card for the given skills.</summary>
    /// <param name="skills">Skills to announce. The list is copied.</param>
    /// <returns>The card.</returns>
    public static AgentCard Create(IReadOnlyList<AgentSkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        return new AgentCard(AgentName, AgentDescription, skills.ToArray());
    }

    /// <summary>JSON of the card. The document has no <c>tools</c> member.</summary>
    /// <param name="card">Card to serialize.</param>
    /// <returns>JSON text.</returns>
    public static string ToJson(AgentCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return JsonSerializer.Serialize(card);
    }
}
