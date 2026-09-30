using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Hexagonal;

public abstract record DomainEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString();
    /// <summary>When the event happened. The code that raises it supplies the time from its clock (K-TIME-3).</summary>
    public required DateTimeOffset OccurredAt { get; init; }
    public string EventType { get; init; }

    protected DomainEvent()
    {
        EventType = GetType().Name;
    }
}

public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj == null || obj.GetType() != GetType())
        {
            return false;
        }

        var other = (ValueObject)obj;
        using var thisValues = GetEqualityComponents().GetEnumerator();
        using var otherValues = other.GetEqualityComponents().GetEnumerator();

        while (thisValues.MoveNext() && otherValues.MoveNext())
        {
            if (thisValues.Current == null ^ otherValues.Current == null)
            {
                return false;
            }

            if (thisValues.Current != null && !thisValues.Current.Equals(otherValues.Current))
            {
                return false;
            }
        }

        return !thisValues.MoveNext() && !otherValues.MoveNext();
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var obj in GetEqualityComponents())
        {
            hash.Add(obj);
        }
        return hash.ToHashCode();
    }
}

public abstract class BaseEntity<TId> where TId : notnull
{
    public TId Id { get; protected set; }
    public int VersionIndex { get; protected set; }
    public DateTimeOffset CreatedAt { get; protected set; }

    /// <param name="id">The entity identifier.</param>
    /// <param name="createdAt">When the entity was created, from the caller's clock (K-TIME-3).</param>
    /// <param name="versionIndex">The entity version, 1 for a new entity.</param>
    protected BaseEntity(TId id, DateTimeOffset createdAt, int versionIndex = 1)
    {
        Id = id;
        VersionIndex = versionIndex;
        CreatedAt = createdAt;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not BaseEntity<TId> other)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (GetType() != other.GetType())
            return false;

        return Id.Equals(other.Id);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), Id);
    }
}

public abstract class AggregateRoot<TId> : BaseEntity<TId> where TId : notnull
{
    private readonly List<DomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id, DateTimeOffset createdAt, int versionIndex = 1)
        : base(id, createdAt, versionIndex) { }

    public void RecordEvent(DomainEvent @event)
    {
        _domainEvents.Add(@event);
    }

    public IReadOnlyList<DomainEvent> FlushEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }

    public void ClearEvents()
    {
        _domainEvents.Clear();
    }
}
