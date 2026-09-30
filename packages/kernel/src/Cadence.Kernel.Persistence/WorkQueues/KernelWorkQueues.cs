using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.WorkQueues;

/// <summary>The current work-queue definitions (ADR-2486), read without tracking under the tenant's Row-Level Security.</summary>
internal sealed class KernelWorkQueueDefinitions(KernelTenantDbContextFactory contexts) : IWorkQueueDefinitions
{
    public async Task<IReadOnlyList<WorkQueueDefinitionSnapshot>> CurrentAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var rows = await db.WorkQueueDefinitions.AsNoTracking()
            .Where(d => d.TenantId == tenant)
            .Where(d => !db.WorkQueueDefinitions.Any(later => later.TenantId == d.TenantId && later.Code == d.Code && later.Version > d.Version))
            .OrderBy(d => d.Code)
            .Select(d => new WorkQueueDefinitionSnapshot(d.Id, d.Code, d.Version, d.Title, d.Capability, d.VerbClass, d.Source, d.VersionIndex))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows;
    }
}

/// <summary>
/// The <see cref="KernelWorkItemSources.ProtocolVersionsInReview"/> source: protocol versions in review, due since
/// they were submitted, which is when their current row version was recorded (a version in review does not change
/// until it is approved, returned or withdrawn). <c>within</c> may name the tenant, a sponsor organization, a study or
/// one protocol version; any other object has no protocol versions under it.
/// </summary>
internal sealed class ProtocolVersionsInReviewSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.ProtocolVersionsInReview;

    public async Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        await using var db = contexts.Create(tenant);
        var versions = db.ProtocolVersions.AsNoTracking()
            .Where(p => p.TenantId == tenant && p.Status == ProtocolVersionStatus.InReview);
        switch (within)
        {
            case null:
                break;
            case { Kind: NodeKinds.Tenant } when within.Id == tenant.Value:
                break;
            case { Kind: NodeKinds.Organization }:
                var sponsor = OrganizationId.FromGuid(within.Id);
                versions = versions.Where(p => db.Studies.Any(s => s.Id == p.StudyId && s.SponsorId == sponsor));
                break;
            case { Kind: NodeKinds.Study }:
                var study = StudyId.FromGuid(within.Id);
                versions = versions.Where(p => p.StudyId == study);
                break;
            case { Kind: NodeKinds.ProtocolVersion }:
                var version = ProtocolVersionId.FromGuid(within.Id);
                versions = versions.Where(p => p.Id == version);
                break;
            default:
                return [];
        }

        var inReview = await versions
            .Join(db.Studies, p => p.StudyId, s => s.Id, (p, s) => new { p.Id, p.Number, p.VersionIndex, p.CreatedAt, s.StudyCode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (inReview.Count == 0)
        {
            return [];
        }

        // The lineage node of each version's current row records when that row was written: the submission. A row
        // written before lineage was recorded falls back to when the version was created.
        var ids = inReview.Select(v => v.Id.Value).ToList();
        var recorded = await db.LineageNodes.AsNoTracking()
            .Where(n => n.TenantId == tenant && n.Kind == NodeKinds.ProtocolVersion && ids.Contains(n.NodeId))
            .Select(n => new { n.NodeId, n.Version, n.RecordedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var submittedAt = recorded.ToDictionary(n => (n.NodeId, n.Version), n => n.RecordedAt);

        return
        [
            .. inReview
                .Select(v => new WorkItemCandidate(
                    NodeRef.For(v.Id, v.VersionIndex),
                    string.Create(CultureInfo.InvariantCulture, $"Protocol version {v.Number} of {v.StudyCode}"),
                    submittedAt.TryGetValue((v.Id.Value, v.VersionIndex), out var at)
                        ? at
                        : new DateTimeOffset(DateTime.SpecifyKind(v.CreatedAt, DateTimeKind.Utc))))
                .OrderBy(c => c.Since)
                .ThenBy(c => c.Subject.Id)
                .Take(take),
        ];
    }
}
