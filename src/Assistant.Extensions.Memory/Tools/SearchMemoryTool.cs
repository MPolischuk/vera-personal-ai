using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory.Tools;

internal sealed class SearchMemoryTool(MemoryRecorder recorder, ILogger<SearchMemoryTool> logger) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Una consulta en lenguaje natural. No la combines con queries."
                },
                "queries": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 3,
                  "items": { "type": "string", "minLength": 1 },
                  "description": "Hasta 3 consultas en la misma llamada, de lo amplio a lo específico. No la combines con query."
                },
                "kind": {
                  "type": "string",
                  "enum": ["fact", "episode", "context", "task"],
                  "description": "Filtra por tipo de recuerdo."
                },
                "since": {
                  "type": "string",
                  "format": "date-time",
                  "description": "Sólo recuerdos creados a partir de esta fecha ISO."
                },
                "include_history": {
                  "type": "boolean",
                  "description": "Pasalo sólo si la pregunta es por un dato anterior, no por el vigente. Por ejemplo, dónde vivía antes."
                }
              },
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "search_memory",
        "Busca recuerdos con filtros opcionales de tipo (fact, episode, context, task) y fecha desde (since). "
        + "include_history sólo si la pregunta es por un dato anterior, no por el vigente: por ejemplo, dónde vivía antes. "
        + "Para elegir o recomendar, pasá queries (hasta 3) en la misma llamada, de lo amplio a lo específico. "
        + "query sigue siendo una sola consulta. "
        + "Si el usuario pide qué recuerdos tenés o un listado, usala. "
        + "Si no hay resultados, devolvé lista vacía; no inventes.",
        Schema,
        PermissionLevel.Read,
        "memory");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!TryResolveQueries(args, out var queries, out var queryError))
        {
            return ToolResult.Failed(queryError);
        }

        MemoryKind? kind = null;
        if (args.TryGetString("kind", out var kindText))
        {
            if (!Enum.TryParse<MemoryKind>(kindText, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return ToolResult.Failed("El parámetro 'kind' debe ser fact, episode, context o task.");
            }

            kind = parsed;
        }

        DateTimeOffset? since = null;
        if (args.TryGetString("since", out var sinceText))
        {
            if (!DateTimeOffset.TryParse(sinceText, out var parsed))
            {
                return ToolResult.Failed("El parámetro 'since' no es una fecha ISO válida.");
            }

            since = parsed;
        }

        if (!TryReadIncludeHistory(args, out var includeHistory, out var historyError))
        {
            return ToolResult.Failed(historyError);
        }

        try
        {
            var hits = await recorder.SearchAsync(
                queries,
                kind,
                since,
                context,
                MemoryToolDefaults.RecallTopK,
                MemoryToolDefaults.RecallMinScore,
                ct,
                includeHistory);
            return ToolResult.Ok(MemoryToolJson.Hits(hits));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            MemoryLog.EmbedFailed(logger, e, context.TurnId);
            return ToolResult.Failed("No pude buscar recuerdos porque falló el embedding.");
        }
    }

    private static bool TryReadIncludeHistory(ToolArguments args, out bool includeHistory, out string error)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        includeHistory = false;
        error = "";
        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty("include_history", out var property))
        {
            return true;
        }

        if (property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            error = "El parámetro 'include_history' tiene que ser booleano.";
            return false;
        }

        includeHistory = property.GetBoolean();
        return true;
    }

    private static bool TryResolveQueries(
        ToolArguments args, out IReadOnlyList<string> queries, out string error)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            queries = [];
            error = "Falta query o queries.";
            return false;
        }

        var root = document.RootElement;
        var hasQuery = root.TryGetProperty("query", out var queryProperty);
        var hasQueries = root.TryGetProperty("queries", out var queriesProperty);

        if (hasQuery && hasQueries)
        {
            queries = [];
            error = "Indicá query o queries, no los dos.";
            return false;
        }

        if (!hasQuery && !hasQueries)
        {
            queries = [];
            error = "Falta query o queries.";
            return false;
        }

        if (hasQuery)
        {
            if (queryProperty.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(queryProperty.GetString()))
            {
                queries = [];
                error = queryProperty.ValueKind is JsonValueKind.String
                    ? "El parámetro 'query' no puede estar vacío."
                    : "El parámetro 'query' tiene que ser texto.";
                return false;
            }

            queries = [queryProperty.GetString()!];
            error = "";
            return true;
        }

        if (queriesProperty.ValueKind is not JsonValueKind.Array
            || queriesProperty.GetArrayLength() is < 1 or > MemoryToolDefaults.MaxSearchQueries)
        {
            queries = [];
            error = "El parámetro 'queries' tiene que ser de 1 a 3 textos, sin vacíos.";
            return false;
        }

        var list = new List<string>(queriesProperty.GetArrayLength());
        foreach (var item in queriesProperty.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                queries = [];
                error = "El parámetro 'queries' tiene que ser de 1 a 3 textos, sin vacíos.";
                return false;
            }

            list.Add(item.GetString()!);
        }

        queries = list;
        error = "";
        return true;
    }
}
