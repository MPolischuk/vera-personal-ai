using Assistant.Abstractions.Scheduling;

namespace Assistant.Scheduling;

/// <summary>Handlers registered for this process, keyed by <see cref="IJobHandler.Kind"/>.</summary>
public sealed class JobHandlerRegistry
{
    private readonly Dictionary<string, IJobHandler> _handlers;

    public JobHandlerRegistry(IEnumerable<IJobHandler> handlers)
    {
        _handlers = new Dictionary<string, IJobHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (!_handlers.TryAdd(handler.Kind, handler))
            {
                throw new InvalidOperationException($"Job handler kind '{handler.Kind}' is registered more than once.");
            }
        }
    }

    public bool TryGet(string kind, out IJobHandler handler)
        => _handlers.TryGetValue(kind, out handler!);
}
