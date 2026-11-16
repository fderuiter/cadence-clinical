using System;

namespace Cadence.Core.Domain;

/// <summary>
/// Domain event published when an aggregate transitions between lifecycle states.
/// </summary>
public sealed record DomainStateTransitionEvent(
    Guid EventId,
    string AggregateType,
    string AggregateId,
    string FromState,
    string ToState,
    string Trigger,
    DateTimeOffset OccurredAt,
    string ActorId,
    string ReasonForChange) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => $"{AggregateType}StateTransitioned";
}
