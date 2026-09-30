using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence.Simulation;

/// <summary>
/// Simulation's reads (ADR-2480): no-tracking queries under the tenant's Row-Level Security. It never saves, and an
/// architecture test keeps it from reaching the unit of work or <c>SaveChanges</c> (K-SIM-1). Dependents come from the
/// lineage store's dependents read, the same one as the Impact verb and amendment impact (Canvas D2).
/// </summary>
internal sealed class KernelSimulationInputs(KernelTenantDbContextFactory contexts, KernelLineageStore lineage) : ISimulationInputs
{
    public async Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(
        TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.ProtocolVersions.AsNoTracking()
            .Where(p => p.TenantId == tenant && p.Id == id)
            .Select(p => new ProtocolVersionSnapshot(
                p.Id, p.StudyId, p.Number, p.Status, p.ParentVersionId, p.Content, p.ContentHash, p.VersionIndex))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<DependentsReport> DependentsAsync(
        TenantId tenant,
        Actor reader,
        ProtocolVersionId baseVersion,
        ProtocolVersionId? candidate,
        IReadOnlySet<NodeRef> impactedElements,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asOf);
        var excluded = candidate is { } fork ? new HashSet<NodeRef> { NodeRef.For(fork) } : [];
        return lineage.DependentsAsync(
            tenant,
            reader,
            new DependentsQuery(NodeRef.For(baseVersion), ValidAt: asOf.ValidAt, KnownAt: asOf.KnownAt),
            excluded,
            ProtocolElementImpact.FollowFromBase(impactedElements),
            cancellationToken);
    }
}

/// <summary>Registers simulation's reads.</summary>
public static class KernelSimulationPersistenceServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ISimulationInputs"/> over the kernel tables. Requires <c>AddKernelPersistence</c>.</summary>
    public static IServiceCollection AddKernelSimulationInputs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ISimulationInputs, KernelSimulationInputs>();
        return services;
    }
}
