using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Handles one command type. It loads at most one aggregate through <see cref="ICommandContext.Work"/>, decides,
/// and raises events; it never saves (the pipeline commits).
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<CommandResult<TResult>> HandleAsync(TCommand command, ICommandContext context, CancellationToken cancellationToken);
}

/// <summary>What a handler sees of the command being executed.</summary>
public interface ICommandContext
{
    Actor Actor { get; }

    TenantId TenantId { get; }

    /// <summary>Recorded time for this command, from the kernel clock (K-TIME-3).</summary>
    DateTimeOffset RecordedAt { get; }

    IKernelUnitOfWork Work { get; }
}

/// <summary>
/// The command's single aggregate and its events. Touching a second aggregate throws (K-TX-1): multi-aggregate
/// processes are sagas driven by events. Kernel and module handlers see the same unit of work; the pipeline decides
/// which store holds an aggregate type (ADR-2600).
/// </summary>
public interface IKernelUnitOfWork
{
    /// <summary>Loads an aggregate for change, or null when it does not exist.</summary>
    Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase;

    /// <summary>True when an aggregate of this type and id exists (read-only; does not claim the aggregate).</summary>
    Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase;

    /// <summary>
    /// Reads another aggregate without changing it: not tracked and not claimed, so the command may still change one
    /// aggregate of its own (K-TX-1). Use it for facts a decision depends on, such as an attestation or a parent version.
    /// </summary>
    Task<TAggregate?> ReadAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase;

    /// <summary>Reads every aggregate of a type that matches <paramref name="filter"/>, without tracking or claiming it.</summary>
    Task<IReadOnlyList<TAggregate>> ReadAllAsync<TAggregate>(
        Expression<Func<TAggregate, bool>> filter, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase;

    /// <summary>
    /// Takes the advisory lock of the aggregate <typeparamref name="TAggregate"/> <paramref name="id"/> and holds it until
    /// the command has committed or failed (K-TX-4). Commands that decide from the same aggregate's state, such as
    /// numbering within a study, take its lock first and then read, so they run one after another. The lock neither
    /// claims nor changes the aggregate. A command takes at most one exclusive lock; it may then share the scope locks that
    /// cover what it writes (<see cref="ShareScopesAsync"/>), never take this lock after them.
    /// </summary>
    Task LockAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase;

    /// <summary>
    /// Takes the exclusive advisory lock of form instance <paramref name="instance"/> and lets this command change that
    /// instance and the observations it holds, the one exception to "one aggregate per command" (K-TX-1 as amended,
    /// ADR-2622). Call it before reading or adding anything; the command may then share the scope locks that cover the
    /// form (<see cref="ShareScopesAsync"/>), and the commit refuses an observation that is not a member.
    /// </summary>
    Task LockCollectionAsync(CollectionInstanceId instance, CancellationToken cancellationToken);

    /// <summary>
    /// Takes shared advisory locks on the lock scopes that cover what the command writes, in the order given (widest
    /// first, <c>LockScopes.Covering</c>), and holds them until the command has committed or failed (ADR-2621). Writes
    /// share them; a lock command takes one exclusively (<see cref="LockScopeAsync"/>), so either the write commits
    /// before the lock or the lock commits first and the write, reading it, is refused. Then read the locks in force.
    /// </summary>
    Task ShareScopesAsync(IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the exclusive advisory lock on one lock scope (ADR-2621), for a command that applies or releases a lock
    /// there. It is the command's only lock: a command that holds it shares no scope lock.
    /// </summary>
    Task LockScopeAsync(NodeRef scope, CancellationToken cancellationToken);

    /// <summary>Adds a new aggregate.</summary>
    void Add(AggregateRootBase aggregate);

    /// <summary>Raises an event; <paramref name="domainEvent"/>'s type must carry <see cref="KernelEventAttribute"/>.</summary>
    void Raise(object domainEvent, DateTimeOffset? occurredValid = null);

    /// <summary>
    /// Declares that the aggregate this command writes, at the version the commit stamps, was produced from
    /// <paramref name="input"/> (spec §9.6). The edge <c>input → aggregate</c> is written in the command's transaction
    /// (K-LIN-2). <paramref name="input"/> must name a version. The pipeline adds the <see cref="LineageEdgeKind.Supersedes"/>
    /// edge from an updated aggregate's previous version itself.
    /// </summary>
    /// <param name="input">The upstream object version.</param>
    /// <param name="kind">How the aggregate depends on it.</param>
    /// <param name="validFrom">When the dependency takes effect in valid time; null when it always holds.</param>
    void LinkInput(NodeRef input, LineageEdgeKind kind, DateTimeOffset? validFrom = null);

    /// <summary>
    /// Declares <paramref name="part"/> as part of the aggregate version this command writes, such as an element of a
    /// protocol version's design (ADR-2474). The pipeline writes the edge <c>aggregate → part</c> as
    /// <see cref="LineageEdgeKind.GovernedBy"/>, and, when <paramref name="newVersion"/> is true, the part's own lineage
    /// node, which must not exist yet. <paramref name="part"/> must name a version.
    /// </summary>
    void IncludePart(NodeRef part, bool newVersion);

    /// <summary>
    /// Declares the edge <paramref name="from"/> → <paramref name="to"/> between object versions, at least one of which is
    /// a part this command writes as a new version (<see cref="IncludePart"/>). It is written in the command's transaction
    /// with the part (K-LIN-2).
    /// </summary>
    void LinkPart(NodeRef from, NodeRef to, LineageEdgeKind kind);
}
