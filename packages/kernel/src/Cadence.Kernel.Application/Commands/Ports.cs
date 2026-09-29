using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Application.Commands;

/// <summary>
/// Resolves who is acting for the current command (spec §5.1 "Resolve Actor"). Hosts implement it from the
/// authenticated principal or a named service account; returning null rejects the write (K-TX-5).
/// @req:PRD-SYS-053
/// </summary>
public interface ICurrentActor
{
    Actor? Actor { get; }
}

/// <summary>
/// The authorization step of the pipeline (spec §9.3, K-AUTH-1). The authority slice plugs a policy evaluator in
/// here; the kernel refuses to start without one.
/// @req:PRD-SYS-053
/// </summary>
public interface IAuthorizer
{
    ValueTask<AuthorizationDecision> AuthorizeAsync(Actor actor, ICommand command, CancellationToken cancellationToken);
}

/// <summary>
/// An authorization outcome. A denial names what is missing (K-AUTH-6) and is audited (K-AUTH-2).
/// </summary>
public sealed record AuthorizationDecision(bool Permitted, string? Reason = null)
{
    public static AuthorizationDecision Permit(string? reason = null) => new(true, reason);

    public static AuthorizationDecision Deny(string reason) => new(false, reason);
}

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
/// processes are sagas driven by events.
/// @req:PRD-SYS-053
/// </summary>
public interface IKernelUnitOfWork
{
    /// <summary>Loads an aggregate for change, or null when it does not exist.</summary>
    Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase;

    /// <summary>True when an aggregate of this type and id exists (read-only; does not claim the aggregate).</summary>
    Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase;

    /// <summary>Adds a new aggregate.</summary>
    void Add(AggregateRootBase aggregate);

    /// <summary>Raises an event; <paramref name="domainEvent"/>'s type must carry <see cref="KernelEventAttribute"/>.</summary>
    void Raise(object domainEvent, DateTimeOffset? occurredValid = null);
}

/// <summary>Creates one unit of work per command. Implemented by kernel persistence.</summary>
public interface IKernelUnitOfWorkFactory
{
    IKernelCommandTransaction Begin(CommandExecution execution);
}

/// <summary>Everything the pipeline has established about the command before the handler runs.</summary>
public sealed record CommandExecution(
    ICommand Command,
    string CommandType,
    Actor Actor,
    DateTimeOffset RecordedAt,
    Guid ActivityId,
    CommandMetadata Metadata,
    string RequestHash);

/// <summary>The persistence side of one command: idempotency lookup, the handler's unit of work, and the commit.</summary>
public interface IKernelCommandTransaction : IAsyncDisposable
{
    IKernelUnitOfWork Work { get; }

    /// <summary>The stored outcome for this tenant and idempotency key, if the key was used before.</summary>
    Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes state, provenance, events, the audit entry, outbox rows and the idempotency record in one
    /// transaction (K-TX-2). Returns <see cref="CommitOutcome.IdempotencyRace"/> when a concurrent command with the
    /// same key committed first, and <see cref="CommitOutcome.ConcurrencyConflict"/> when the aggregate changed.
    /// </summary>
    Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken);

    /// <summary>Records an authorization denial as its own audit entry and event (K-AUTH-2).</summary>
    Task RecordDenialAsync(string reason, CancellationToken cancellationToken);
}

/// <summary>A result previously stored under the same tenant and idempotency key.</summary>
public sealed record IdempotentOutcome(string RequestHash, string ResultJson);

public enum CommitOutcome
{
    Committed,
    IdempotencyRace,
    ConcurrencyConflict,

    /// <summary>A unique constraint other than the idempotency key rejected the change (for example a duplicate code).</summary>
    UniqueConflict,
}
