using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Assistant.Abstractions.Skills;
using Assistant.Abstractions.Tools;
using Assistant.Core.Observability;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Skills;

internal sealed class LoadSkillTool(
    SkillCatalog catalog,
    ILogger<LoadSkillTool> logger,
    IExternalSkillSource? externalSkills = null) : ITool, IBindsToolNames
{
    public const int MaxSupportFileBytes = 65_536;

    private const string BaseDescription =
        "Cargá el procedimiento antes de seguirlo. Cargarlo no otorga permisos.";

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Nombre de la skill o clave del prompt a cargar."
                },
                "path": {
                  "type": "string",
                  "description": "Ruta relativa a un archivo de apoyo dentro del directorio de la skill."
                }
              },
              "required": ["name"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    private static readonly Regex IsValidPromptName = new(
        "^[A-Za-z0-9._:-]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private Func<IReadOnlyCollection<string>>? _toolNames;

    public ToolDescriptor Descriptor => new(
        "load_skill",
        BuildDescription(),
        Schema,
        PermissionLevel.Read,
        "skills",
        OutputTrust.Trusted,
        WritesDurableState: false);

    public void Bind(Func<IReadOnlyCollection<string>> toolNames) => _toolNames = toolNames;

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        using var activity = SkillTelemetry.ActivitySource.StartActivity(GenAiSpans.LoadSkill);

        if (!args.TryGetString("name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            return ToolResult.Failed(
                args.TryGetString("name", out _)
                    ? "El parámetro 'name' no puede estar vacío."
                    : "Falta el parámetro obligatorio 'name'.");
        }

        activity?.SetTag(GenAiSpans.SkillName, name);

        if (TryFindPrompt(name, out var prompt))
        {
            if (args.TryGetString("path", out _))
            {
                return ToolResult.Failed("Un prompt de servidor no tiene archivos de apoyo.");
            }

            return await LoadPromptAsync(prompt, ct);
        }

        if (!catalog.TryLoad(name, CurrentToolNames(), out var skill))
        {
            return ToolResult.Failed("No hay una skill disponible con ese nombre.");
        }

        if (args.TryGetString("path", out var path))
        {
            return await ReadSupportFileAsync(skill.DirectoryPath, path, ct);
        }

        return ToolResult.Ok(skill.Body);
    }

    private async ValueTask<ToolResult> LoadPromptAsync(ExternalSkillSummary prompt, CancellationToken ct)
    {
        if (externalSkills is null)
        {
            return ToolResult.Failed("No hay una skill disponible con ese nombre.");
        }

        var text = await externalSkills.LoadTextAsync(prompt.Key, ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ToolResult.Failed("El prompt no devolvió texto.");
        }

        var serverId = prompt.Key[..prompt.Key.IndexOf('/', StringComparison.Ordinal)];
        return ToolResult.Ok($"Origen: {serverId}.\n{text}", OutputTrust.Untrusted);
    }

    private static async ValueTask<ToolResult> ReadSupportFileAsync(
        string skillDirectory, string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return ToolResult.Failed("El path tiene que ser relativo al directorio de la skill.");
        }

        var root = Path.GetFullPath(skillDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(skillDirectory, path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ToolResult.Failed("El path tiene que ser relativo al directorio de la skill.");
        }

        if (!fullPath.StartsWith(root, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            return ToolResult.Failed("El path queda fuera del directorio de la skill.");
        }

        if (!File.Exists(fullPath))
        {
            return ToolResult.Failed("El path no es un archivo de la skill.");
        }

        var length = new FileInfo(fullPath).Length;
        if (length > MaxSupportFileBytes)
        {
            return ToolResult.Failed("El archivo supera el tamaño máximo de 64 KiB.");
        }

        var content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct);
        return ToolResult.Ok(content);
    }

    private string BuildDescription()
    {
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var skill in catalog.ListAvailable(CurrentToolNames()))
        {
            if (!rows.TryAdd(skill.Key, OneLine(skill.Description)))
            {
                SkillLog.OmittingDuplicateKey(logger, skill.Key);
            }
        }

        if (externalSkills is not null)
        {
            foreach (var prompt in externalSkills.List())
            {
                if (!TryFindPromptKey(prompt.Key, out var serverId, out _))
                {
                    SkillLog.OmittingPrompt(logger, prompt.Key);
                    continue;
                }

                var text = $"{OneLine(prompt.Description)} Origen: {serverId}, no confiable.";
                if (!rows.TryAdd(prompt.Key, text))
                {
                    SkillLog.OmittingPrompt(logger, prompt.Key);
                }
            }
        }

        var builder = new StringBuilder(BaseDescription);
        if (rows.Count == 0)
        {
            builder.AppendLine();
            builder.Append("No hay procedimientos disponibles.");
            return builder.ToString();
        }

        foreach (var row in rows)
        {
            builder.AppendLine();
            builder.Append("- ");
            builder.Append(row.Key);
            builder.Append(": ");
            builder.Append(row.Value);
        }

        return builder.ToString();
    }

    private bool TryFindPrompt(string key, out ExternalSkillSummary prompt)
    {
        if (externalSkills is not null && TryFindPromptKey(key, out _, out _))
        {
            foreach (var candidate in externalSkills.List())
            {
                if (string.Equals(candidate.Key, key, StringComparison.Ordinal))
                {
                    prompt = candidate;
                    return true;
                }
            }
        }

        prompt = null!;
        return false;
    }

    private static bool TryFindPromptKey(string key, out string serverId, out string promptName)
    {
        serverId = string.Empty;
        promptName = string.Empty;

        var slash = key.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash != key.LastIndexOf('/'))
        {
            return false;
        }

        serverId = key[..slash];
        promptName = key[(slash + 1)..];
        return IsValidPromptName.IsMatch(promptName);
    }

    private IReadOnlyCollection<string>? CurrentToolNames() => _toolNames?.Invoke();

    private static string OneLine(string text) => text.ReplaceLineEndings(" ");
}
