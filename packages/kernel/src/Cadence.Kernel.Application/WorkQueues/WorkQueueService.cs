using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.WorkQueues;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Application.WorkQueues;

/// <summary>The current version of a work-queue definition, as the queue read uses it.</summary>
/// <param name="Id">The definition version.</param>
/// <param name="Code">The queue's code.</param>
/// <param name="Version">The definition version number.</param>
/// <param name="Title">The queue's title.</param>
/// <param name="Capability">The capability token an item needs.</param>
/// <param name="VerbClass">The capability's verb class.</param>
/// <param name="Source">The registered item source.</param>
/// <param name="VersionIndex">The row's audit version, which pins references to it.</param>
public sealed record WorkQueueDefinitionSnapshot(
    WorkQueueDefinitionId Id,
    string Code,
    int Version,
    string Title,
    string Capability,
    VerbClass VerbClass,
    string Source,
    long VersionIndex);

/// <summary>Reads work-queue definitions. Kernel persistence implements it under the tenant's Row-Level Security.</summary>
public interface IWorkQueueDefinitions
{
    /// <summary>The highest version of every code defined in <paramref name="tenant"/>.</summary>
    Task<IReadOnlyList<WorkQueueDefinitionSnapshot>> CurrentAsync(TenantId tenant, CancellationToken cancellationToken);
}

/// <summary>An object a source reports as due, before authorization and masking.</summary>
/// <param name="Subject">The object the work is on, pinned to the version read.</param>
/// <param name="Title">What is due, for example "Protocol version 2".</param>
/// <param name="Since">When the item became due.</param>
public sealed record WorkItemCandidate(NodeRef Subject, string Title, DateTimeOffset Since);

/// <summary>
/// A named producer of work items (ADR-2486). A source knows what is due, never who should do it: the queue read
/// keeps an item only for an actor the evaluator permits. The kernel registers its own sources; an application
/// registers more for the objects it owns.
/// </summary>
public interface IWorkItemSource
{
    /// <summary>The name definitions refer to, for example <c>protocol.versions_in_review</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Up to <paramref name="take"/> items due in <paramref name="tenant"/>, oldest first, restricted to objects under
    /// <paramref name="within"/> when it is set.
    /// </summary>
    Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken);
}

/// <summary>
/// Masks work items whose subject or underlying clinical observation records blinded treatment allocation unless the
/// reader holds an unblinding capability on the covered enrollment or study (K-AUTH-4, ADR-2471).
/// </summary>
public interface IWorkItemBlindingMask
{
    /// <summary>Returns <see cref="MaskReason.Blinded"/> for any node in <paramref name="nodes"/> that is blinded to the current caller.</summary>
    ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant,
        IAuthorizationProbe probe,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken);
}

/// <summary>Navigation context resolved for an unmasked work item so clinical workspaces can deep-link directly to the target screen.</summary>
/// <param name="StudyRef">The owning study's unversioned NodeRef, when the item belongs to a study.</param>
/// <param name="StudyId">The owning study's raw identifier, when the item belongs to a study.</param>
/// <param name="EnrollmentId">The participant enrollment identifier, when the item belongs to an enrollment.</param>
/// <param name="FormId">The form instance identifier, when the item targets a form or an observation collected on a form.</param>
/// <param name="ObservationRef">The observation's NodeRef, when the item targets an observation or a query/finding on one.</param>
/// <param name="FindingRef">The validation finding's NodeRef, when the item targets a validation finding.</param>
/// <param name="ProtocolVersionRef">The protocol version's NodeRef, when the item targets a protocol version.</param>
/// <param name="ProposalRef">The proposal's NodeRef, when the item targets a proposal.</param>
public sealed record WorkItemTargetContext(
    string? StudyRef = null,
    Guid? StudyId = null,
    Guid? EnrollmentId = null,
    Guid? FormId = null,
    string? ObservationRef = null,
    string? FindingRef = null,
    string? ProtocolVersionRef = null,
    string? ProposalRef = null);

/// <summary>Resolves navigation context for unmasked work items.</summary>
public interface IWorkItemTargetResolver
{
    /// <summary>Resolves navigation context keyed by unmasked subject <see cref="NodeRef"/>.</summary>
    Task<IReadOnlyDictionary<NodeRef, WorkItemTargetContext>> ResolveAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> subjects,
        CancellationToken cancellationToken);
}

/// <summary>
/// The work-queue read (Canvas D10, ADR-2486). For each current definition it takes the source's oldest candidates
/// and keeps those the current actor may act on, asking <see cref="IAuthorizationProbe"/> for each one, so a queue
/// holds exactly what the corresponding command would permit now. Objects the actor may not see or that are blinded
/// to the actor are masked in place (ADR-2471, K-AUTH-4).
/// </summary>
public sealed partial class WorkQueueService(
    IWorkQueueDefinitions definitions,
    IEnumerable<IWorkItemSource> sources,
    IAuthorizationProbe probe,
    INodeVisibility visibility,
    ICurrentActor currentActor,
    ILogger<WorkQueueService> logger,
    IWorkItemBlindingMask? blinding = null) : IWorkQueues
{
    /// <summary>The most candidates one queue read examines per queue.</summary>
    public const int MaxExamined = WorkQueueQuery.MaxLimit;

    public async Task<CommandResult<IReadOnlyList<WorkQueue>>> ReadAsync(
        TenantId tenant, WorkQueueQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("Work queues are read within a tenant.", nameof(tenant));
        }

        if (query.Limit is < 1 or > WorkQueueQuery.MaxLimit)
        {
            return WorkQueueErrors.QueryInvalid;
        }

        if (currentActor.Actor is not { } actor)
        {
            return KernelErrors.ActorUnresolved;
        }

        var bySource = new Dictionary<string, IWorkItemSource>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            bySource.TryAdd(source.Name, source);
        }

        var codes = query.Codes is { Count: > 0 } requested ? new HashSet<string>(requested, StringComparer.Ordinal) : null;
        var current = await definitions.CurrentAsync(tenant, cancellationToken).ConfigureAwait(false);
        var queues = new List<WorkQueue>();
        foreach (var definition in current.Where(d => codes is null || codes.Contains(d.Code)).OrderBy(d => d.Code, StringComparer.Ordinal))
        {
            var reference = NodeRef.For(definition.Id, definition.VersionIndex);
            if (!bySource.TryGetValue(definition.Source, out var source))
            {
                LogSourceMissing(logger, tenant, definition.Code, definition.Source);
                queues.Add(new WorkQueue(reference, definition.Code, definition.Version, definition.Title, definition.Capability, [], Truncated: false));
                continue;
            }

            var (items, truncated) = await ItemsAsync(tenant, actor, definition, source, query, cancellationToken).ConfigureAwait(false);
            queues.Add(new WorkQueue(reference, definition.Code, definition.Version, definition.Title, definition.Capability, items, truncated));
        }

        return CommandResult<IReadOnlyList<WorkQueue>>.Success(queues);
    }

    private async Task<(IReadOnlyList<WorkItem> Items, bool Truncated)> ItemsAsync(
        TenantId tenant,
        Actor actor,
        WorkQueueDefinitionSnapshot definition,
        IWorkItemSource source,
        WorkQueueQuery query,
        CancellationToken cancellationToken)
    {
        var candidates = await source.CandidatesAsync(tenant, query.Within, MaxExamined + 1, cancellationToken).ConfigureAwait(false);
        var truncated = candidates.Count > MaxExamined;
        var capability = new Capability(definition.Capability, definition.VerbClass);
        var permitted = new List<WorkItemCandidate>();
        foreach (var candidate in candidates.Take(MaxExamined))
        {
            if (permitted.Count == query.Limit)
            {
                truncated = true;
                break;
            }

            var decision = await probe.CanIWorkQueueCandidateAsync(tenant, capability, candidate.Subject, cancellationToken).ConfigureAwait(false);
            if (decision.Permitted)
            {
                permitted.Add(candidate);
            }
        }

        if (permitted.Count == 0)
        {
            return ([], truncated);
        }

        var subjects = permitted.Select(c => c.Subject).ToList();
        var hidden = await visibility.MaskAsync(tenant, actor, subjects, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<NodeRef, MaskReason>? blinded = null;
        if (blinding is not null)
        {
            var visibleSubjects = subjects.Where(s => !hidden.ContainsKey(s)).Distinct().ToList();
            if (visibleSubjects.Count > 0)
            {
                blinded = await blinding.MaskAsync(tenant, probe, visibleSubjects, cancellationToken).ConfigureAwait(false);
            }
        }

        IReadOnlyList<WorkItem> items =
        [
            .. permitted.Select(c =>
            {
                if (hidden.TryGetValue(c.Subject, out var reason))
                {
                    return new WorkItem(null, c.Subject.Kind, null, c.Since, reason);
                }

                if (blinded is not null && blinded.TryGetValue(c.Subject, out var blindReason))
                {
                    return new WorkItem(null, c.Subject.Kind, null, c.Since, blindReason);
                }

                return new WorkItem(c.Subject, c.Subject.Kind, c.Title, c.Since, null);
            }),
        ];
        return (items, truncated);
    }

    [LoggerMessage(EventId = 2486, EventName = "WorkItemSourceMissing", Level = LogLevel.Warning,
        Message = "Work queue {Code} in tenant {Tenant} names source {Source}, which this host does not register; the queue is empty")]
    private static partial void LogSourceMissing(ILogger logger, TenantId tenant, string code, string source);
}

/// <summary>Registers the work-queue read.</summary>
public static class WorkQueueServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IWorkQueues"/>. The host also registers kernel persistence's definitions and sources
    /// (<c>AddKernelWorkQueueStore()</c>), <see cref="IAuthorizationProbe"/> (<c>AddKernelAuthorization()</c>) and an
    /// <see cref="INodeVisibility"/>.
    /// </summary>
    public static IServiceCollection AddKernelWorkQueues(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IWorkQueues, WorkQueueService>();
        return services;
    }

    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelWorkQueueHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<DefineWorkQueue, WorkQueueDefinitionState>, DefineWorkQueueHandler>();
        return services;
    }
}
