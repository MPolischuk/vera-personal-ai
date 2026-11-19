using System.Reflection;
using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Models;
using Assistant.Core.Personality;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Tools;

internal sealed class GetAssistantInfoTool(
    IPersonalityService personality,
    IOptions<ModelsOptions> models) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse("""{"type":"object","properties":{},"additionalProperties":false}""")
        .RootElement.Clone();

    private static readonly string Version =
        typeof(AssemblyMarker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(AssemblyMarker).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public ToolDescriptor Descriptor { get; } = new(
        "get_assistant_info",
        "Devuelve el nombre del asistente, sus rasgos de personalidad, el modelo de conversación "
        + "y la versión del ensamblado. Usala cuando el usuario pregunte quién sos, cómo sos "
        + "o con qué modelo hablás. No vuelques esta llamada como JSON en el mensaje.",
        Schema,
        PermissionLevel.Read,
        "builtin");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var active = await personality.GetActiveAsync(ct);
        var conversation = models.Value.Conversation;

        var payload = JsonSerializer.Serialize(new
        {
            name = active.Profile.Name,
            relationship = active.Profile.Relationship,
            traits = active.Profile.Traits,
            model = conversation.Model,
            provider = conversation.Provider,
            version = Version,
            task_kind = nameof(TaskKind.Conversation),
        });

        return ToolResult.Ok(payload);
    }
}
