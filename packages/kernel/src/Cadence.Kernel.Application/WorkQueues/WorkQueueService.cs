using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
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
/// The work-queue read (Canvas D10, ADR-2486). For each current definition it takes the source's oldest candidates
/// and keeps those the current actor may act on, asking <see cref="IAuthorizationProbe"/> for each one, so a queue
/// holds exactly what the corresponding command would permit now. Objects the actor may not see are masked in place
/// (ADR-2471).
/// </summary>
public sealed partial class WorkQueueService(
    IWorkQueueDefinitions definitions,
    IEnumerable<IWorkItemSource> sources,
    IAuthorizationProbe probe,
    INodeVisibility visibility,
    ICurrentActor currentActor,
    ILogger<WorkQueueService> logger) : IWorkQueues
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

            var decision = await probe.CanIAsync(tenant, capability, candidate.Subject, cancellationToken).ConfigureAwait(false);
            if (decision.Permitted)
            {
                permitted.Add(candidate);
            }
        }

        if (permitted.Count == 0)
        {
            return ([], truncated);
        }

        var hidden = await visibility.MaskAsync(tenant, actor, [.. permitted.Select(c => c.Subject)], cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WorkItem> items =
        [
            .. permitted.Select(c => hidden.TryGetValue(c.Subject, out var reason)
                ? new WorkItem(null, c.Subject.Kind, null, c.Since, reason)
                : new WorkItem(c.Subject, c.Subject.Kind, c.Title, c.Since, null)),
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
