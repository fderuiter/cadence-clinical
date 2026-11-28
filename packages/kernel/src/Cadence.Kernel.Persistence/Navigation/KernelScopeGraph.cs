using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence.Navigation;

/// <summary>
/// Reads the zoom hierarchy from the kernel tables (ADR-2481 §4.3). Read-only (<c>AsNoTracking</c>), through a context
/// bound to the caller's tenant by Row-Level Security (K-ID-2), with the tenant also in every predicate. Enrollments sit
/// under their study site; collection instances and standalone observations under their enrollment; form-bound
/// observations under their collection instance; assertions under their target; concepts under their tenant; artifacts
/// under their study or tenant; protocol versions and study standards under their study; a protocol element under the version
/// that last governed it (ADR-2474); an attestation under the object it signs (ADR-2485), falling back to the tenant when a
/// lock signature precedes a lock assertion that has not been recorded. Pseudonymous participant and person identities resolve
/// directly under the tenant without direct identifiers (K-IDN-1).
/// Current-state objects are returned at their current version, whatever version the reference names; the returned
/// <see cref="NodeSummary.Ref"/> says which version that is.
/// </summary>
internal sealed class KernelScopeGraph(KernelTenantDbContextFactory contexts) : IKernelScopeGraph
{
    public async Task<ScopeNode?> FindAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        await using var db = contexts.Create(tenant);
        return await ScopeGraphSingleNodeReader.FindNodeAsync(db, tenant, node, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScopeChild>> ChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        await using var db = contexts.Create(tenant);

        switch (node.Kind)
        {
            case NodeKinds.Tenant when node.Id == tenant.Value:
                var studies = await db.Studies.AsNoTracking()
                    .Where(s => s.TenantId == tenant)
                    .OrderBy(s => s.StudyCode)
                    .Select(s => new
                    {
                        s.Id,
                        s.Title,
                        s.StudyCode,
                        s.VersionIndex,
                        Sites = db.StudySites.Count(ss => ss.TenantId == tenant && ss.StudyId == s.Id),
                    })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                var organizations = await db.Organizations.AsNoTracking()
                    .Where(o => o.TenantId == tenant)
                    .OrderBy(o => o.Code)
                    .Select(o => new
                    {
                        o.Id,
                        o.Name,
                        o.Code,
                        o.VersionIndex,
                        Sites = db.Sites.Count(s => s.TenantId == tenant && s.InstitutionId == o.Id),
                    })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                return
                [
                    .. studies.Select(s => new ScopeChild(new NodeSummary(NodeRef.For(s.Id, s.VersionIndex), s.Title, s.StudyCode), s.Sites)),
                    .. organizations.Select(o => new ScopeChild(new NodeSummary(NodeRef.For(o.Id, o.VersionIndex), o.Name, o.Code), o.Sites)),
                ];

            case NodeKinds.Study:
                var studyId = StudyId.FromGuid(node.Id);
                var studySites = await ScopeGraphCoreAndProtocolBatchReader.StudySitesWithSites(db, tenant)
                    .Where(x => x.StudyId == studyId)
                    .OrderBy(x => x.Country).ThenBy(x => x.SiteCode)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var enrollmentCounts = await db.Enrollments.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.StudyId == studyId)
                    .GroupBy(e => e.StudySiteId)
                    .Select(g => new { StudySiteId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.StudySiteId, x => x.Count, cancellationToken).ConfigureAwait(false);
                return [.. studySites.Select(x => new ScopeChild(ScopeGraphCoreAndProtocolBatchReader.StudySiteSummary(x), enrollmentCounts.GetValueOrDefault(x.Id), x.Country))];

            case NodeKinds.StudySite:
                var parentSiteId = StudySiteId.FromGuid(node.Id);
                var enrollments = await db.Enrollments.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.StudySiteId == parentSiteId)
                    .Select(e => new { e.Id, e.SubjectCode, e.Status, e.VersionIndex })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                return
                [
                    .. enrollments
                        .OrderBy(e => e.SubjectCode.Value, StringComparer.Ordinal)
                        .Select(e => new ScopeChild(ScopeGraphCoreAndProtocolBatchReader.EnrollmentSummary(e.Id, e.SubjectCode, e.Status, e.VersionIndex), ChildCount: null)),
                ];

            case NodeKinds.Enrollment:
                return await ScopeGraphObservationAndChildrenReader.EnrollmentChildrenAsync(db, tenant, EnrollmentId.FromGuid(node.Id), cancellationToken)
                    .ConfigureAwait(false);

            case NodeKinds.CollectionInstance:
                return await ScopeGraphObservationAndChildrenReader.CollectionInstanceChildrenAsync(db, tenant, CollectionInstanceId.FromGuid(node.Id), cancellationToken)
                    .ConfigureAwait(false);

            case NodeKinds.Organization:
                var organizationId = OrganizationId.FromGuid(node.Id);
                var sites = await db.Sites.AsNoTracking()
                    .Where(s => s.TenantId == tenant && s.InstitutionId == organizationId)
                    .OrderBy(s => s.SiteCode)
                    .Select(s => new { s.Id, s.Name, s.SiteCode, s.VersionIndex })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                return [.. sites.Select(s => new ScopeChild(new NodeSummary(NodeRef.For(s.Id, s.VersionIndex), s.Name, s.SiteCode), ChildCount: 0))];

            default:
                return [];
        }
    }

    public async Task<IReadOnlyDictionary<NodeRef, ScopeNode>> FindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var firstByUnversioned = new Dictionary<NodeRef, NodeRef>();
        foreach (var node in nodes)
        {
            firstByUnversioned.TryAdd(node.Unversioned(), node);
        }

        if (firstByUnversioned.Count == 0)
        {
            return new Dictionary<NodeRef, ScopeNode>();
        }

        var distinct = firstByUnversioned.Keys.ToList();
        var found = new Dictionary<NodeRef, ScopeNode>(distinct.Count);
        await using var db = contexts.Create(tenant);
        var tenantRef = NodeRef.For(tenant);

        await ScopeGraphCoreAndProtocolBatchReader.PopulateCoreScopeNodesAsync(db, tenant, tenantRef, distinct, found, cancellationToken).ConfigureAwait(false);
        await ScopeGraphCoreAndProtocolBatchReader.PopulateProtocolAndExecutionNodesAsync(db, tenant, distinct, found, cancellationToken).ConfigureAwait(false);
        await ScopeGraphAuthorityAndTerminologyReader.PopulateAuthorityAndTerminologyNodesAsync(db, tenant, tenantRef, distinct, firstByUnversioned, found, cancellationToken).ConfigureAwait(false);
        await ScopeGraphGovernanceAndValidationReader.PopulateGovernanceAndValidationNodesAsync(db, tenant, tenantRef, distinct, found, cancellationToken).ConfigureAwait(false);

        return found;
    }

    public async Task<IReadOnlyList<StudyEnrollmentNode>?> StudyEnrollmentsAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var studySites = ScopeGraphCoreAndProtocolBatchReader.StudySitesWithSites(db, tenant).Where(ss => ss.StudyId == study);
        var rows = await (
                from enrollment in db.Enrollments.AsNoTracking()
                where enrollment.TenantId == tenant && enrollment.StudyId == study
                join studySite in studySites on enrollment.StudySiteId equals studySite.Id into studySiteGroup
                from studySite in studySiteGroup.DefaultIfEmpty()
                select new
                {
                    enrollment.Id,
                    enrollment.SubjectCode,
                    enrollment.StudySiteId,
                    HasSite = studySite != null,
                    SiteCode = studySite != null ? studySite.SiteCode : null,
                    SiteName = studySite != null ? studySite.SiteName : null,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            var exists = await db.Studies.AsNoTracking()
                .AnyAsync(s => s.TenantId == tenant && s.Id == study, cancellationToken)
                .ConfigureAwait(false);
            return exists ? [] : null;
        }

        return
        [
            .. rows
                .OrderBy(r => r.SubjectCode.Value, StringComparer.Ordinal)
                .Select(r => new StudyEnrollmentNode(
                    r.Id,
                    r.SubjectCode.Value,
                    r.HasSite ? r.StudySiteId : null,
                    ScopeGraphCoreAndProtocolBatchReader.SiteLabel(r.HasSite, r.SiteCode, r.SiteName))),
        ];
    }
}

/// <summary>Registers the kernel scope graph behind <see cref="IKernelScopeGraph"/>.</summary>
public static class KernelNavigationPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IKernelScopeGraph"/> over kernel persistence. Call after
    /// <see cref="KernelPersistenceServiceCollectionExtensions.AddKernelPersistence"/>, together with
    /// <c>AddKernelNavigation()</c>.
    /// </summary>
    public static IServiceCollection AddKernelScopeGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IKernelScopeGraph, KernelScopeGraph>();
        return services;
    }
}
