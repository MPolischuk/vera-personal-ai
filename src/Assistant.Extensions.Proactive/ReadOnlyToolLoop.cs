using System.Text;
using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Tools;
using Assistant.Core.Tools;
using Microsoft.Extensions.AI;

namespace Assistant.Extensions.Proactive;

/// <summary>
/// At most <see cref="HeartbeatJobs.MaxToolRounds"/> model rounds.
/// Only <see cref="PermissionLevel.Read"/> tools are declared and dispatched.
/// Any other name is stored as a pending proposal and is not invoked.
/// </summary>
public sealed class ReadOnlyToolLoop(
    IToolRegistry registry,
    IToolDispatcher dispatcher,
    IProposalStore proposals,
    TimeProvider clock)
{
    public const string NotReadReason = "The tool is not Read.";

    public async ValueTask<ReadOnlyLoopResult> RunAsync(
        IChatClient client,
        IList<ChatMessage> messages,
        ToolExecutionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(context);

        var read = registry.GetAvailableTools(context)
            .Where(tool => tool.Level == PermissionLevel.Read)
            .ToArray();
        var readNames = read.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var declared = read
            .Select(tool => (AITool)AIFunctionFactory.CreateDeclaration(
                tool.Name, tool.Description, tool.ParametersSchema))
            .ToList();
        var options = new ChatOptions
        {
            Tools = declared,
            ToolMode = ChatToolMode.Auto,
        };

        var rounds = new List<ChatResponse>(HeartbeatJobs.MaxToolRounds);
        for (var round = 1; round <= HeartbeatJobs.MaxToolRounds; round++)
        {
            var response = await client.GetResponseAsync(messages, options, ct);
            rounds.Add(response);
            foreach (var message in response.Messages)
            {
                messages.Add(message);
            }
            var calls = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToList();
            if (calls.Count == 0)
            {
                return new ReadOnlyLoopResult(response.Text ?? string.Empty, rounds);
            }

            foreach (var call in calls)
            {
                if (string.IsNullOrEmpty(call.Name) || !readNames.Contains(call.Name))
                {
                    await proposals.InsertAsync(
                        new NewProposal(
                            context.UserId,
                            ProposalOrigins.Heartbeat,
                            string.IsNullOrWhiteSpace(call.Name) ? "unknown" : call.Name,
                            ArgumentsJson(call),
                            NotReadReason,
                            null),
                        clock.GetUtcNow(),
                        ct);
                    messages.Add(ToolMessage(call, "No ejecuté esta tool. Quedó como propuesta."));
                    continue;
                }

                var result = await dispatcher.InvokeAsync(call.Name, ToArguments(call), context, ct);
                messages.Add(ToolMessage(call, ContentForModel(result)));
            }

            if (round == HeartbeatJobs.MaxToolRounds)
            {
                return new ReadOnlyLoopResult(response.Text ?? string.Empty, rounds);
            }
        }

        return new ReadOnlyLoopResult(string.Empty, rounds);
    }

    private static ChatMessage ToolMessage(FunctionCallContent call, string content)
        => new(ChatRole.Tool, [new FunctionResultContent(call.CallId, content)]);

    private static string ContentForModel(ToolInvocationResult result)
    {
        if (result.Result?.Status is ToolStatus.Ok)
        {
            return result.Result.Content ?? string.Empty;
        }

        return result.Result?.Error ?? result.Reason ?? "La herramienta no se ejecutó.";
    }

    private static ToolArguments ToArguments(FunctionCallContent call)
        => ToolArguments.FromJson(ArgumentsJson(call));

    private static string ArgumentsJson(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return "{}";
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var pair in call.Arguments)
            {
                writer.WritePropertyName(pair.Key);
                WriteValue(writer, pair.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            default:
                JsonSerializer.Serialize(writer, value);
                break;
        }
    }
}

/// <summary>Final assistant text and every model round, so the caller can quote the cost.</summary>
public sealed record ReadOnlyLoopResult(string Text, IReadOnlyList<ChatResponse> Rounds);
