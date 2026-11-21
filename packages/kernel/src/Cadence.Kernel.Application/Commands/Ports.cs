using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel.Application.Identity;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Commands;

/// <summary>
/// Resolves who is acting for the current command (spec §5.1 "Resolve Actor"). Hosts implement it from the
/// authenticated principal or a named service account; returning null rejects the write (K-TX-5).
/// </summary>
public interface ICurrentActor
{
    Actor? Actor { get; }
}

/// <summary>
/// The service account a host-internal unit of work acts as, set only by host or endpoint code inside a dedicated DI
/// scope. Regular request scopes leave it null so a request's actor comes from the verified caller identity.
/// </summary>
public sealed class ServiceActorScope
{
    /// <summary>The service account acting in this scope, or null when the caller's own identity applies.</summary>
    public Actor? Actor { get; set; }
}

/// <summary>
/// The authorization step of the pipeline (spec §9.3, K-AUTH-1). The authority slice plugs a policy evaluator in
/// here; the kernel refuses to start without one.
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
    /// Records that this command executes the approved proposal at <paramref name="proposal"/> (a proposal NodeRef with
    /// its version), checked by the pipeline (K-AI-2). The commit writes the proposal onto the provenance activity and an
    /// <see cref="LineageEdgeKind.ExecutedUnder"/> edge from it to the object version the command writes.
    /// </summary>
    void RecordProposalExecution(NodeRef proposal);

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

    /// <summary>
    /// Records a refusal the handler decided by a condition and explained (K-EXP-1), such as new capture for a withdrawn
    /// participant, as its own audit entry with the error code, the reason and the evaluated conditions, and discards
    /// whatever the handler staged. The command was authorized, so the provenance activity keeps its permit.
    /// </summary>
    Task RecordRefusalAsync(KernelError refusal, CancellationToken cancellationToken);
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
