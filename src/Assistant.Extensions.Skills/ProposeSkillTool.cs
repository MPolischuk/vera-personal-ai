using System.Text;
using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Audit;
using Assistant.Core.Security;
using Assistant.Core.Skills;

namespace Assistant.Extensions.Skills;

internal sealed class ProposeSkillTool(ISkillProposalStore proposals, IAuditLogStore audit) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Nombre de la skill, en minúsculas y con guiones."
                },
                "description": {
                  "type": "string",
                  "description": "Una línea que dice cuándo usarla. De 1 a 1024 caracteres."
                },
                "body": {
                  "type": "string",
                  "description": "El procedimiento, en markdown, sin el frontmatter."
                },
                "requires_tools": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Tools de catálogo que la skill necesita. Opcional."
                },
                "requires_servers": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Servidores MCP que la skill necesita. Opcional."
                }
              },
              "required": ["name", "description", "body"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "propose_skill",
        "Redactá una propuesta de skill pendiente. No escribe el archivo: una persona la aprueba o la descarta en Extensiones. Cargarla no otorga permisos.",
        Schema,
        PermissionLevel.ReversibleAction,
        "skills",
        OutputTrust.Trusted,
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!TryRequired(args, "name", out var name))
        {
            return ToolResult.Failed(MissingOrEmpty("name", args));
        }

        if (!TryRequired(args, "description", out var description))
        {
            return ToolResult.Failed(MissingOrEmpty("description", args));
        }

        if (!TryRequired(args, "body", out var body))
        {
            return ToolResult.Failed(MissingOrEmpty("body", args));
        }

        name = name.Trim();
        if (!SkillParser.IsAcceptableName(name))
        {
            return ToolResult.Failed("El nombre no es válido.");
        }

        description = CollapseLines(description);
        if (description.Length is < 1 or > 1024)
        {
            return ToolResult.Failed("La descripción tiene que tener entre 1 y 1024 caracteres.");
        }

        body = body.Trim();
        if (body.Length == 0)
        {
            return ToolResult.Failed("El parámetro 'body' no puede estar vacío.");
        }

        if (!TryReadList(args, "requires_tools", out var tools))
        {
            return ToolResult.Failed("Un requisito no es válido.");
        }

        if (!TryReadList(args, "requires_servers", out var servers))
        {
            return ToolResult.Failed("Un requisito no es válido.");
        }

        var markdown = Assemble(name, description, body, tools, servers);
        if (Encoding.UTF8.GetByteCount(markdown) > SkillParser.MaxBytes)
        {
            return ToolResult.Failed("El archivo supera el tamaño máximo de 64 KiB.");
        }

        if (!SkillParser.TryParse(name, markdown, out _, out _))
        {
            return ToolResult.Failed("La propuesta no se puede leer como skill.");
        }

        if (PromptInjectionScanner.Inspect(markdown).IsRejected)
        {
            return ToolResult.Failed(PromptInjectionScanner.RejectionMessage);
        }

        SkillProposal created;
        try
        {
            created = await proposals.CreateAsync(
                new CreateSkillProposal(context.UserId, name, description, markdown),
                ct);
        }
        catch (SkillProposalConflictException)
        {
            return ToolResult.Failed("Ya hay una propuesta pendiente con ese nombre.");
        }

        await audit.AppendAsync(
            new AuditEntry(
                context.UserId,
                "skill_proposal.created",
                "skill_proposal",
                created.Id,
                NamePayload(name)),
            ct);

        return ToolResult.Ok(
            $"Quedó una propuesta pendiente para '{name}'. No escribí el archivo. Se aprueba en Extensiones.");
    }

    private static bool TryRequired(ToolArguments args, string parameter, out string value)
    {
        if (args.TryGetString(parameter, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string MissingOrEmpty(string parameter, ToolArguments args)
        => args.TryGetString(parameter, out _)
            ? $"El parámetro '{parameter}' no puede estar vacío."
            : $"Falta el parámetro obligatorio '{parameter}'.";

    private static string CollapseLines(string text)
        => string.Join(
            ' ',
            text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool TryReadList(ToolArguments args, string name, out string[] values)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        if (!document.RootElement.TryGetProperty(name, out var property))
        {
            values = [];
            return true;
        }

        if (property.ValueKind is not JsonValueKind.Array)
        {
            values = [];
            return false;
        }

        var list = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String)
            {
                values = [];
                return false;
            }

            var text = item.GetString() ?? string.Empty;
            if (!SkillParser.IsAcceptableRequirement(text))
            {
                values = [];
                return false;
            }

            list.Add(text);
        }

        values = list.ToArray();
        return true;
    }

    internal static string Assemble(
        string name,
        string description,
        string body,
        IReadOnlyList<string> tools,
        IReadOnlyList<string> servers)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.Append("name: ").AppendLine(name);
        builder.Append("description: ").AppendLine(Quote(description));
        if (tools.Count > 0 || servers.Count > 0)
        {
            builder.AppendLine("metadata:");
            if (tools.Count > 0)
            {
                builder.Append("  assistant.requires.tools: ").AppendLine(string.Join(' ', tools));
            }

            if (servers.Count > 0)
            {
                builder.Append("  assistant.requires.servers: ").AppendLine(string.Join(' ', servers));
            }
        }

        builder.AppendLine("---");
        builder.AppendLine();
        builder.Append(body);
        return builder.ToString();
    }

    private static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    private static string NamePayload(string name)
        => JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = name });
}
