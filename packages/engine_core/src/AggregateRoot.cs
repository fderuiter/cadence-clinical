using System;

namespace Cadence.Core.Domain;

/// <summary>
/// Abstract base class for DDD aggregate roots in the Cadence platform with built-in 21 CFR Part 11 audit fields.
/// @req:PRD-SYS-053
/// </summary>
public abstract class AggregateRoot<TId> : IGxPAuditable
{
    public TId Id { get; protected set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public long VersionIndex { get; set; } = 1;

    protected AggregateRoot() { }

    protected AggregateRoot(TId id)
    {
        Id = id;
    }
}

