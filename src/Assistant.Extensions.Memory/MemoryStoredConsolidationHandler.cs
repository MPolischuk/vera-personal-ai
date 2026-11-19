using Assistant.Abstractions.Events;
using Assistant.Core.Events;

namespace Assistant.Extensions.Memory;

/// <summary>
/// Counts stored memories per user. The model runs later, in <see cref="MemoryConsolidationService"/>.
/// </summary>
internal sealed class MemoryStoredConsolidationHandler(MemoryConsolidationSchedule schedule)
    : IDomainEventHandler<MemoryStored>
{
    public ValueTask HandleAsync(MemoryStored domainEvent, CancellationToken ct)
    {
        schedule.NoteStored(domainEvent.UserId);
        return ValueTask.CompletedTask;
    }
}
