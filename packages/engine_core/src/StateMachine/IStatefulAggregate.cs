using System;
using System.Collections.Generic;

namespace Cadence.Core.Domain;

/// <summary>
/// Contract for stateful aggregates holding transition history, domain events, and outbox messages.
/// </summary>
public interface IStatefulAggregate
{
    /// <summary>Raised domain events awaiting dispatch.</summary>
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>Transactional outbox records generated alongside state transitions.</summary>
    IReadOnlyList<TransactionalOutboxMessage> OutboxMessages { get; }

    /// <summary>Immutable 21 CFR Part 11 transition audit records.</summary>
    IReadOnlyList<StateTransitionRecord> TransitionHistory { get; }

    /// <summary>Clears pending domain events and outbox messages after persistence.</summary>
    void ClearDomainEvents();
}
