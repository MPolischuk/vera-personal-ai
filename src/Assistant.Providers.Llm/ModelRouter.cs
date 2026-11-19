using Assistant.Abstractions;
using Assistant.Core.Events;
using Assistant.Core.Models;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Llm;

internal sealed class ModelRouter(
    IOptions<ModelsOptions> models,
    IOptions<CostOptions> costs,
    ChatClientCache cache,
    IEventBus events,
    ITokenEstimator estimator,
    TimeProvider clock,
    ILoggerFactory loggers) : IModelRouter
{
    public IChatClient GetClientFor(TaskKind kind, ModelCallContext context)
    {
        var profile = Resolve(kind);

        // The decorator is per call and does not own the inner client (D5).
        return new UsageAccountingChatClient(
            cache.Get(profile),
            profile,
            kind,
            context,
            costs.Value,
            events,
            estimator,
            clock,
            loggers.CreateLogger<UsageAccountingChatClient>());
    }

    // Utility falls back to Conversation, Summarization to Utility. Prefer a TaskKind
    // without config using the good model over failing the turn: the only loss is cost
    // savings, and that shows up on the cost dashboard.
    internal ModelProfileOptions Resolve(TaskKind kind) => kind switch
    {
        TaskKind.Conversation => models.Value.Conversation,
        TaskKind.Utility => models.Value.Utility ?? models.Value.Conversation,
        TaskKind.Summarization => models.Value.Summarization
            ?? models.Value.Utility
            ?? models.Value.Conversation,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
