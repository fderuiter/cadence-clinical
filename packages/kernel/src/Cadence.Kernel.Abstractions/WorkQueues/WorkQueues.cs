using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.References;

namespace Cadence.Kernel.WorkQueues;

/// <summary>
/// The current actor's work queues (Canvas D10, PX-15, ADR-2486). A queue is a versioned definition keyed by a
/// capability: its items are what a registered source reports as due, kept only where the actor may exercise that
/// capability on the item now, decided by the same evaluator as <c>CanI</c> and every command (ADR-2483). A person's
/// queue therefore follows their grants and delegations, never a role name, and revoking a grant removes its items.
/// </summary>
public interface IWorkQueues
{
    /// <summary>
    /// The current actor's queues in <paramref name="tenant"/>, one per current definition, in code order. Fails when
    /// no accountable actor is signed in or the query is invalid.
    /// </summary>
    Task<CommandResult<IReadOnlyList<WorkQueue>>> ReadAsync(TenantId tenant, WorkQueueQuery query, CancellationToken cancellationToken);
}

/// <summary>Which queues and items to read.</summary>
/// <param name="Within">
/// Only items under this object's zoom path, for example a study; null for the whole tenant, which is the portfolio
/// view of someone who works across many studies.
/// </param>
/// <param name="Codes">Only these queue codes; null or empty for all.</param>
/// <param name="Limit">Items per queue, from 1 to <see cref="MaxLimit"/>.</param>
public sealed record WorkQueueQuery(NodeRef? Within = null, IReadOnlyCollection<string>? Codes = null, int Limit = 50)
{
    /// <summary>The most items one queue returns.</summary>
    public const int MaxLimit = 200;
}

/// <summary>One queue for the actor.</summary>
/// <param name="Definition">The definition version the queue was computed from, pinned.</param>
/// <param name="Code">The queue's code.</param>
/// <param name="Version">The definition version.</param>
/// <param name="Title">The queue's title.</param>
/// <param name="Capability">The capability every item needs.</param>
/// <param name="Items">The items, oldest first.</param>
/// <param name="Truncated">
/// True when there may be more items than returned: the actor has more than the limit, or the source had more
/// candidates than one read examines (<see cref="WorkQueueQuery.MaxLimit"/>).
/// </param>
public sealed record WorkQueue(
    NodeRef Definition,
    string Code,
    int Version,
    string Title,
    string Capability,
    IReadOnlyList<WorkItem> Items,
    bool Truncated);

/// <summary>
/// One item due. An item whose object the actor may not see stays in place with <see cref="Subject"/> null and the
/// reason in <see cref="Masked"/> (D1, ADR-2471).
/// </summary>
/// <param name="Subject">The object the work is on, pinned to the version read; null when masked.</param>
/// <param name="SubjectKind">The object's kind, shown even when masked.</param>
/// <param name="Title">What is due, for example "Protocol version 2"; null when masked.</param>
/// <param name="Since">When the item became due.</param>
/// <param name="Masked">Why the object is hidden, or null.</param>
public sealed record WorkItem(NodeRef? Subject, string SubjectKind, string? Title, DateTimeOffset Since, MaskReason? Masked);
