using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;

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
/// An authorization outcome. A denial names what is missing (K-AUTH-6) and is audited with the conditions it was
/// decided on (K-AUTH-2). <see cref="Conditions"/> lists every condition the evaluator checked, met and unmet, each
/// with its evidence, so the decision can be explained without re-deriving it (ADR-2482).
/// </summary>
public sealed record AuthorizationDecision(bool Permitted, string? Reason = null)
{
    /// <summary>Producer name on explanations of authorization decisions.</summary>
    public const string ExplanationProducer = "kernel.authorization";

    /// <summary>Every condition the evaluator checked, met and unmet. Empty when it reported none.</summary>
    public IReadOnlyList<EvaluatedCondition> Conditions { get; init; } = [];

    public static AuthorizationDecision Permit(string? reason = null, params EvaluatedCondition[] conditions) =>
        new(true, reason) { Conditions = conditions };

    public static AuthorizationDecision Deny(string reason, params EvaluatedCondition[] conditions) =>
        new(false, reason) { Conditions = conditions };

    /// <summary>
    /// The decision as an <see cref="Explanation"/> (ADR-2482 §4.3): the claim says whether <paramref name="action"/>
    /// is permitted and why, the conditions are the evaluator's, and each unmet condition becomes a next action.
    /// </summary>
    public Explanation Explain(string action, NodeRef? subject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var claim = Permitted ? $"{action} is permitted." : $"{action} is not permitted.";
        var steps = Reason is null ? [] : new[] { new ExplanationStep(Reason, [.. Conditions.SelectMany(c => c.Evidence).Distinct()]) };
        var next = Conditions.Where(c => !c.Met).Select(c => new NextAction(c.Description)).ToArray();
        return new Explanation(subject, claim, ExplanationProducer, steps, Conditions, next);
    }
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
    /// claims nor changes the aggregate. A command takes at most one lock.
    /// </summary>
    Task LockAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase;

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
    /// Keeps the permit this command was authorized by, so the commit records it on the provenance activity with the
    /// grants and evidence that justified it (K-AUTH-2).
    /// </summary>
    void RecordPermit(AuthorizationDecision permit);

    /// <summary>
    /// Writes state, provenance, events, the audit entry, outbox rows and the idempotency record in one
    /// transaction (K-TX-2). Returns <see cref="CommitOutcome.IdempotencyRace"/> when a concurrent command with the
    /// same key committed first, and <see cref="CommitOutcome.ConcurrencyConflict"/> when the aggregate changed.
    /// </summary>
    Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken);

    /// <summary>
    /// Records an authorization denial as its own audit entry and event, with the reason and the conditions it was
    /// decided on (K-AUTH-2).
    /// </summary>
    Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken);
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
