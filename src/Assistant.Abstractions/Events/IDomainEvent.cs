using System.Diagnostics.CodeAnalysis;

namespace Assistant.Abstractions.Events;

/// <summary>
/// Domain-fact marker. Deliberately empty: timestamp and correlation are added by
/// the bus, so an event only carries domain data.
/// </summary>
public interface IDomainEvent;

/// <summary>
/// Seam 5: behavior that reacts to what happens enters here. In stage 1a subscribers
/// are audit and client fan-out; in V7, proactivity.
/// </summary>
/// <remarks>
/// The Handler suffix is intentional: it is not a .NET Framework EventHandler; it is
/// the typed domain-bus subscriber (PT-03 D14).
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Design name: typed domain-bus handler, not a WinForms EventHandler.")]
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    public ValueTask HandleAsync(TEvent domainEvent, CancellationToken ct);
}
