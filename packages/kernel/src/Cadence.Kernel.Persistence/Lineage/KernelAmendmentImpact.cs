using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Lineage;

/// <summary>
/// Amendment impact (K-PROT-2) as one caller of the dependents read (Canvas D2): the dependents of every version of
/// the parent, walked without passing through the amended fork itself.
/// </summary>
internal sealed class KernelAmendmentImpact(KernelTenantDbContextFactory contexts, KernelLineageStore lineage) : IAmendmentImpact
{
    public async Task<AmendmentImpact?> ComputeAsync(
        TenantId tenant,
        Actor reader,
        ProtocolVersionId amended,
        int depth = LineageQuery.DefaultDepth,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        ProtocolVersion? version;
        await using (var db = contexts.Create(tenant))
        {
            version = await db.ProtocolVersions.AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == amended, cancellationToken)
                .ConfigureAwait(false);
        }

        if (version is null)
        {
            return null;
        }

        if (version.ParentVersionId is not { } parent || version.Amendment is not { } amendment)
        {
            throw new InvalidOperationException($"Protocol version {version.Number} is a first version and amends nothing.");
        }

        var dependents = await lineage.DependentsAsync(
            tenant,
            reader,
            new DependentsQuery(NodeRef.For(parent), depth, ValidAt: validAt, KnownAt: knownAt),
            excluded: new HashSet<NodeRef> { NodeRef.For(amended) },
            cancellationToken).ConfigureAwait(false);
        return new AmendmentImpact(amended, parent, amendment.ImpactedElements, dependents);
    }
}
