using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Lineage;

/// <summary>
/// Amendment impact (K-PROT-2) as one caller of the dependents read (Canvas D2): the dependents of every version of
/// the parent, walked without passing through the amended fork itself. From the parent, the walk enters only the design
/// elements (ADR-2474) the amendment changes or removes; an element it leaves alone is not impacted unless it depends on
/// one that changed.
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
        string? parentContent = null;
        await using (var db = contexts.Create(tenant))
        {
            version = await db.ProtocolVersions.AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == amended, cancellationToken)
                .ConfigureAwait(false);
            if (version?.ParentVersionId is { } parentId)
            {
                parentContent = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => v.Id == parentId)
                    .Select(v => v.Content)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (version is null)
        {
            return null;
        }

        if (version.ParentVersionId is not { } parent || version.Amendment is not { } amendment)
        {
            throw new InvalidOperationException($"Protocol version {version.Number} is a first version and amends nothing.");
        }

        var baseContent = parentContent
            ?? throw new InvalidOperationException($"Protocol version {version.Number} names a parent the tenant does not have.");
        var diff = ProtocolStructuralDiff.Compare(baseContent, version.Content);
        var changes = diff.IsSuccess
            ? diff.Value!.Where(c => c.ElementId != ProtocolStructuralDiff.RootId).ToList()
            : throw new InvalidOperationException($"Protocol version {version.Number} cannot be compared with its parent: {diff.Error!.Message}");

        var dependents = await lineage.DependentsAsync(
            tenant,
            reader,
            new DependentsQuery(NodeRef.For(parent), depth, ValidAt: validAt, KnownAt: knownAt),
            excluded: new HashSet<NodeRef> { NodeRef.For(amended) },
            followFromStart: ProtocolElementImpact.FollowFromBase(ProtocolElementImpact.Impacted(version.StudyId, changes)),
            cancellationToken).ConfigureAwait(false);
        return new AmendmentImpact(amended, parent, amendment.ImpactedElements, dependents, changes, Schedule(baseContent, version.Content));
    }

    /// <summary>The Schedule of Activities directives (ADR-2475), when the amended content is a typed design.</summary>
    private static SoaAmendmentDiff? Schedule(string parentContent, string content) =>
        ProtocolDesignDocument.Read(content).Design is { } design
            ? ScheduleOfActivitiesDiff.Compare(ProtocolDesignDocument.Read(parentContent).Design, design)
            : null;
}
