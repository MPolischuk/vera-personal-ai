using Assistant.Abstractions.Events;
using Assistant.Core.Events;
using Assistant.Core.Memory;

namespace Assistant.Extensions.Memory;

/// <summary>
/// Drops an inferred memory when the forgotten row was its last live evidence.
/// Does not publish another event: REST forgets with an empty turn id.
/// </summary>
internal sealed class InferredEvidenceForgottenHandler(IMemoryStore store)
    : IDomainEventHandler<MemoryForgotten>
{
    public async ValueTask HandleAsync(MemoryForgotten domainEvent, CancellationToken ct)
    {
        var citing = await store.ListCitingAsync(domainEvent.UserId, domainEvent.MemoryId, ct);
        foreach (var item in citing)
        {
            if (item.WriteProvenance != WriteProvenance.Inferred)
            {
                continue;
            }

            if (await AnyEvidenceAliveAsync(domainEvent.UserId, item.DerivedFrom, ct))
            {
                continue;
            }

            await store.ForgetAsync(item.Id, domainEvent.UserId, ct);
        }
    }

    private async ValueTask<bool> AnyEvidenceAliveAsync(
        Guid userId, IReadOnlyList<Guid>? evidenceIds, CancellationToken ct)
    {
        if (evidenceIds is null)
        {
            return false;
        }

        foreach (var evidenceId in evidenceIds)
        {
            if (await store.FindAsync(evidenceId, userId, ct) is not null)
            {
                return true;
            }
        }

        return false;
    }
}
