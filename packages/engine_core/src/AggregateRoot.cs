namespace Cadence.Core.Domain;

/// <summary>
/// Abstract base class for DDD aggregate roots in the Cadence platform with built-in 21 CFR Part 11 audit fields.
/// </summary>
public abstract class AggregateRoot<TId> : AggregateRootBase
{
    public TId Id { get; protected set; } = default!;

    protected AggregateRoot() { }

    protected AggregateRoot(TId id)
    {
        Id = id;
    }
}
