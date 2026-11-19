using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Personality;

namespace Assistant.Api.Tools;

internal sealed class GetCurrentDateTimeTool(IPersonalityService personality, TimeProvider clock) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        // Without "properties", some OpenAI-compatible providers reject the declaration.
        .Parse("""{"type":"object","properties":{},"additionalProperties":false}""")
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "get_current_datetime",
        "Fuente autoritativa de la fecha y la hora actuales en la zona horaria del usuario. "
        + "Llamala siempre que el usuario pregunte qué hora es, qué día es o la fecha. "
        + "No inventes la hora ni copies ninguna hora del contexto del mensaje: "
        + "el contexto temporal es sólo orientación de tono y puede estar desfasado.",
        Schema,
        PermissionLevel.Read,
        "builtin");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var profile = (await personality.GetActiveAsync(ct)).Profile;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZone);
        var culture = CultureInfo.GetCultureInfo(profile.Language);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);

        var text = local.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)
            + $" ({profile.TimeZone}; {local.ToString("dddd d 'de' MMMM 'de' yyyy, HH:mm", culture)})";

        return ToolResult.Ok(text);
    }
}
