using System;

namespace Cadence.Core.Domain;

/// <summary>
/// Core contract representing a domain event raised within aggregate boundaries.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique identifier for the event.</summary>
    Guid EventId { get; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>Logical event type name.</summary>
    string EventType { get; }
}
