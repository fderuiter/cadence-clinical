using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Navigation;

internal sealed class StudySiteRow
{
    public StudySiteId Id { get; init; }

    public StudyId StudyId { get; init; }

    public long VersionIndex { get; init; }

    public string Status { get; init; } = string.Empty;

    public string SiteName { get; init; } = string.Empty;

    public string SiteCode { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;
}

internal static class ScopeGraphCoreAndProtocolBatchReader
{
    public static IQueryable<StudySiteRow> StudySitesWithSites(KernelDbContext db, TenantId tenant) =>
        from studySite in db.StudySites.AsNoTracking()
        join site in db.Sites.AsNoTracking() on new { studySite.TenantId, Id = studySite.SiteId } equals new { site.TenantId, site.Id }
        where studySite.TenantId == tenant
        select new StudySiteRow
        {
            Id = studySite.Id,
            StudyId = studySite.StudyId,
            VersionIndex = studySite.VersionIndex,
            Status = studySite.Status,
            SiteName = site.Name,
            SiteCode = site.SiteCode,
            Country = site.Country,
        };

    public static NodeSummary EnrollmentSummary(EnrollmentId id, SubjectCode code, EnrollmentStatus status, long versionIndex) =>
        new(NodeRef.For(id, versionIndex), code.Value, code.Value, status.ToString());

    public static NodeSummary StudySiteSummary(StudySiteRow row) =>
        new(NodeRef.For(row.Id, row.VersionIndex), row.SiteName, row.SiteCode, row.Status);

    public static string? SiteLabel(bool hasSite, string? siteCode, string? siteName)
    {
        if (!hasSite)
        {
            return null;
        }

        return string.IsNullOrEmpty(siteCode) ? siteName : siteCode;
    }

    public static async Task PopulateCoreScopeNodesAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        List<NodeRef> distinct,
        Dictionary<NodeRef, ScopeNode> found,
        CancellationToken cancellationToken)
    {
        if (distinct.Any(n => n.Kind == NodeKinds.Tenant && n.Id == tenant.Value))
        {
            var tenantRow = await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenant)
                .Select(t => new { t.Name, t.Code, t.VersionIndex })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (tenantRow is not null)
            {
                found[tenantRef] = new ScopeNode(
                    new NodeSummary(NodeRef.For(tenant, tenantRow.VersionIndex), tenantRow.Name, tenantRow.Code),
                    ZoomParent: null);
            }
        }

        var organizationIds = distinct.Where(n => n.Kind == NodeKinds.Organization).Select(n => OrganizationId.FromGuid(n.Id)).ToList();
        if (organizationIds.Count > 0)
        {
            var organizations = await db.Organizations.AsNoTracking()
                .Where(o => o.TenantId == tenant && organizationIds.Contains(o.Id))
                .Select(o => new { o.Id, o.Name, o.Code, o.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var organization in organizations)
            {
                found[NodeRef.For(organization.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(organization.Id, organization.VersionIndex), organization.Name, organization.Code),
                    tenantRef);
            }
        }

        var personIds = distinct.Where(n => n.Kind == NodeKinds.Person).Select(n => PersonId.FromGuid(n.Id)).ToList();
        if (personIds.Count > 0)
        {
            var people = await (
                    from p in db.People.AsNoTracking()
                    where personIds.Contains(p.Id)
                        && db.TenantMemberships.Any(m => m.TenantId == tenant && m.PersonId == p.Id && m.IsActive)
                    select new { p.Id, p.IsActive, p.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var person in people)
            {
                found[NodeRef.For(person.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(person.Id, person.VersionIndex), "Person", Code: null, person.IsActive ? "Active" : "Inactive"),
                    tenantRef);
            }
        }

        var membershipIds = distinct.Where(n => n.Kind == NodeKinds.TenantMembership).Select(n => TenantMembershipId.FromGuid(n.Id)).ToList();
        if (membershipIds.Count > 0)
        {
            var memberships = await db.TenantMemberships.AsNoTracking()
                .Where(m => m.TenantId == tenant && membershipIds.Contains(m.Id))
                .Select(m => new { m.Id, m.IsActive, m.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var membership in memberships)
            {
                found[NodeRef.For(membership.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(membership.Id, membership.VersionIndex), "Tenant membership", Code: null, membership.IsActive ? "Active" : "Inactive"),
                    tenantRef);
            }
        }

        var siteIds = distinct.Where(n => n.Kind == NodeKinds.Site).Select(n => SiteId.FromGuid(n.Id)).ToList();
        if (siteIds.Count > 0)
        {
            var sites = await db.Sites.AsNoTracking()
                .Where(s => s.TenantId == tenant && siteIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name, s.SiteCode, s.InstitutionId, s.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var site in sites)
            {
                found[NodeRef.For(site.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(site.Id, site.VersionIndex), site.Name, site.SiteCode),
                    NodeRef.For(site.InstitutionId),
                    FallbackParent: tenantRef);
            }
        }

        var studyIds = distinct.Where(n => n.Kind == NodeKinds.Study).Select(n => StudyId.FromGuid(n.Id)).ToList();
        if (studyIds.Count > 0)
        {
            var studies = await db.Studies.AsNoTracking()
                .Where(s => s.TenantId == tenant && studyIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Title, s.StudyCode, s.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var study in studies)
            {
                found[NodeRef.For(study.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(study.Id, study.VersionIndex), study.Title, study.StudyCode),
                    tenantRef);
            }
        }

        var studySiteIds = distinct.Where(n => n.Kind == NodeKinds.StudySite).Select(n => StudySiteId.FromGuid(n.Id)).ToList();
        if (studySiteIds.Count > 0)
        {
            var studySites = await StudySitesWithSites(db, tenant)
                .Where(x => studySiteIds.Contains(x.Id))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var studySite in studySites)
            {
                found[NodeRef.For(studySite.Id)] = new ScopeNode(
                    StudySiteSummary(studySite),
                    NodeRef.For(studySite.StudyId),
                    studySite.Country,
                    FallbackParent: tenantRef);
            }
        }

        var participantIds = distinct.Where(n => n.Kind == NodeKinds.Participant).Select(n => ParticipantId.FromGuid(n.Id)).ToList();
        if (participantIds.Count > 0)
        {
            var participants = await db.Participants.AsNoTracking()
                .Where(p => p.TenantId == tenant && participantIds.Contains(p.Id))
                .Select(p => new { p.Id, p.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var participant in participants)
            {
                found[NodeRef.For(participant.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(participant.Id, participant.VersionIndex), "Participant", Code: null),
                    tenantRef);
            }
        }

        var enrollmentIds = distinct.Where(n => n.Kind == NodeKinds.Enrollment).Select(n => EnrollmentId.FromGuid(n.Id)).ToList();
        if (enrollmentIds.Count > 0)
        {
            var enrollments = await db.Enrollments.AsNoTracking()
                .Where(e => e.TenantId == tenant && enrollmentIds.Contains(e.Id))
                .Select(e => new { e.Id, e.SubjectCode, e.StudyId, e.StudySiteId, e.Status, e.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var enrollment in enrollments)
            {
                found[NodeRef.For(enrollment.Id)] = new ScopeNode(
                    EnrollmentSummary(enrollment.Id, enrollment.SubjectCode, enrollment.Status, enrollment.VersionIndex),
                    ZoomRules.EnrollmentParent(enrollment.StudyId, enrollment.StudySiteId),
                    FallbackParent: NodeRef.For(enrollment.StudyId));
            }
        }
    }

    public static async Task PopulateProtocolAndExecutionNodesAsync(
        KernelDbContext db,
        TenantId tenant,
        List<NodeRef> distinct,
        Dictionary<NodeRef, ScopeNode> found,
        CancellationToken cancellationToken)
    {
        var collectionInstanceIds = distinct.Where(n => n.Kind == NodeKinds.CollectionInstance).Select(n => CollectionInstanceId.FromGuid(n.Id)).ToList();
        if (collectionInstanceIds.Count > 0)
        {
            foreach (var (instanceId, scopeNode) in await ScopeGraphObservationAndChildrenReader.LoadCollectionInstancesAsync(db, tenant, collectionInstanceIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(instanceId)] = scopeNode;
            }
        }

        var observationIds = distinct.Where(n => n.Kind == NodeKinds.Observation).Select(n => ObservationId.FromGuid(n.Id)).ToList();
        if (observationIds.Count > 0)
        {
            foreach (var (observationId, scopeNode) in await ScopeGraphObservationAndChildrenReader.LoadObservationsAsync(db, tenant, observationIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(observationId)] = scopeNode;
            }
        }

        var protocolVersionIds = distinct.Where(n => n.Kind == NodeKinds.ProtocolVersion).Select(n => ProtocolVersionId.FromGuid(n.Id)).ToList();
        if (protocolVersionIds.Count > 0)
        {
            var protocolVersions = await db.ProtocolVersions.AsNoTracking()
                .Where(p => p.TenantId == tenant && protocolVersionIds.Contains(p.Id))
                .Select(p => new { p.Id, p.StudyId, p.Number, p.Status, p.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var pv in protocolVersions)
            {
                found[NodeRef.For(pv.Id)] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(pv.Id, pv.VersionIndex),
                        $"Protocol version {pv.Number}",
                        $"v{pv.Number}",
                        pv.Status.ToString()),
                    NodeRef.For(pv.StudyId));
            }
        }

        var snapshotIds = distinct.Where(n => n.Kind == NodeKinds.FormVersionSnapshot).Select(n => FormVersionSnapshotId.FromGuid(n.Id)).ToList();
        if (snapshotIds.Count > 0)
        {
            var snapshots = await db.FormVersionSnapshots.AsNoTracking()
                .Where(s => s.TenantId == tenant && snapshotIds.Contains(s.Id))
                .Select(s => new { s.Id, s.StudyId, s.ProtocolVersionId, s.FormId, s.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var snapshot in snapshots)
            {
                found[NodeRef.For(snapshot.Id)] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(snapshot.Id, snapshot.VersionIndex),
                        $"Form version snapshot {snapshot.FormId}",
                        snapshot.FormId),
                    NodeRef.For(snapshot.ProtocolVersionId),
                    FallbackParent: NodeRef.For(snapshot.StudyId));
            }
        }

        var protocolElementGuids = distinct.Where(n => n.Kind == NodeKinds.ProtocolElement).Select(n => n.Id).ToList();
        if (protocolElementGuids.Count > 0)
        {
            foreach (var (elementGuid, scopeNode) in await LoadProtocolElementsAsync(db, tenant, protocolElementGuids, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(ProtocolElementId.FromGuid(elementGuid))] = scopeNode;
            }
        }

        var formQueryGuids = distinct.Where(n => n.Kind == NodeKinds.FormQuery).Select(n => n.Id).ToList();
        if (formQueryGuids.Count > 0)
        {
            foreach (var (queryGuid, scopeNode) in await LoadFormQueriesAsync(db, tenant, formQueryGuids, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(FormQueryId.FromGuid(queryGuid))] = scopeNode;
            }
        }

        var profileIds = distinct.Where(n => n.Kind == NodeKinds.StudyStandardsProfile).Select(n => StudyStandardsProfileId.FromGuid(n.Id)).ToList();
        if (profileIds.Count > 0)
        {
            var profiles = await db.Set<StudyStandardsProfile>().AsNoTracking()
                .Where(p => p.TenantId == tenant && profileIds.Contains(p.Id))
                .Select(p => new { p.Id, p.StudyId, p.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var profile in profiles)
            {
                found[NodeRef.For(profile.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(profile.Id, profile.VersionIndex), "Study standards", Code: null),
                    NodeRef.For(profile.StudyId));
            }
        }
    }

    public static async Task<Dictionary<Guid, ScopeNode>> LoadProtocolElementsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<Guid> elementGuids,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ScopeNode>(elementGuids.Count);
        var edges = await db.LineageEdges.AsNoTracking()
            .Where(e => e.TenantId == tenant
                && e.ToKind == NodeKinds.ProtocolElement
                && elementGuids.Contains(e.ToId)
                && e.FromKind == NodeKinds.ProtocolVersion
                && e.Kind == LineageEdgeKind.GovernedBy)
            .OrderByDescending(e => e.RecordedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.ToId, e.FromId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (edges.Count == 0)
        {
            return result;
        }

        var governingByElement = new Dictionary<Guid, ProtocolVersionId>();
        foreach (var edge in edges)
        {
            governingByElement.TryAdd(edge.ToId, ProtocolVersionId.FromGuid(edge.FromId));
        }

        var versionIds = governingByElement.Values.Distinct().ToList();
        var owners = await db.ProtocolVersions.AsNoTracking()
            .Where(p => p.TenantId == tenant && versionIds.Contains(p.Id))
            .Select(p => new { p.Id, p.StudyId, p.Content })
            .ToDictionaryAsync(p => p.Id, cancellationToken).ConfigureAwait(false);
        var governedGuids = governingByElement.Keys.ToList();
        var latestVersions = await db.LineageNodes.AsNoTracking()
            .Where(n => n.TenantId == tenant && n.Kind == NodeKinds.ProtocolElement && governedGuids.Contains(n.NodeId))
            .GroupBy(n => n.NodeId)
            .Select(g => new { ElementGuid = g.Key, Latest = g.Max(n => (long?)n.Version) })
            .ToDictionaryAsync(x => x.ElementGuid, x => x.Latest, cancellationToken).ConfigureAwait(false);

        var elementsByVersion = new Dictionary<ProtocolVersionId, IReadOnlyList<string>?>();
        foreach (var (versionId, owner) in owners)
        {
            var diff = ProtocolStructuralDiff.Compare(null, owner.Content);
            elementsByVersion[versionId] = diff.IsSuccess
                ? [.. diff.Value!.Select(c => c.ElementId).Where(e => e != ProtocolStructuralDiff.RootId)]
                : null;
        }

        foreach (var (elementGuid, versionId) in governingByElement)
        {
            if (!owners.TryGetValue(versionId, out var owner)
                || !latestVersions.TryGetValue(elementGuid, out var latest)
                || latest is null)
            {
                continue;
            }

            var id = ProtocolElementId.FromGuid(elementGuid);
            var elementId = elementsByVersion.GetValueOrDefault(versionId)?
                .FirstOrDefault(e => ProtocolElementId.For(owner.StudyId, e) == id);
            result[elementGuid] = new ScopeNode(
                new NodeSummary(NodeRef.For(id, latest.Value), elementId is null ? "Protocol element" : $"Protocol element {elementId}", elementId),
                NodeRef.For(versionId),
                FallbackParent: NodeRef.For(owner.StudyId));
        }

        return result;
    }

    public static async Task<Dictionary<Guid, ScopeNode>> LoadFormQueriesAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<Guid> queryGuids,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ScopeNode>(queryGuids.Count);
        var edges = await db.LineageEdges.AsNoTracking()
            .Where(e => e.TenantId == tenant
                && e.ToKind == NodeKinds.FormQuery
                && queryGuids.Contains(e.ToId)
                && e.FromKind == NodeKinds.Enrollment
                && e.Kind == LineageEdgeKind.GovernedBy)
            .OrderByDescending(e => e.RecordedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.ToId, e.FromId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (edges.Count == 0)
        {
            return result;
        }

        var governingByQuery = new Dictionary<Guid, Guid>();
        foreach (var edge in edges)
        {
            governingByQuery.TryAdd(edge.ToId, edge.FromId);
        }

        var governedGuids = governingByQuery.Keys.ToList();
        var latestVersions = await db.LineageNodes.AsNoTracking()
            .Where(n => n.TenantId == tenant && n.Kind == NodeKinds.FormQuery && governedGuids.Contains(n.NodeId))
            .GroupBy(n => n.NodeId)
            .Select(g => new { QueryGuid = g.Key, Latest = g.Max(n => (long?)n.Version) })
            .ToDictionaryAsync(x => x.QueryGuid, x => x.Latest, cancellationToken).ConfigureAwait(false);

        foreach (var (queryGuid, enrollmentGuid) in governingByQuery)
        {
            if (!latestVersions.TryGetValue(queryGuid, out var latest) || latest is null)
            {
                continue;
            }

            var id = FormQueryId.FromGuid(queryGuid);
            result[queryGuid] = new ScopeNode(
                new NodeSummary(NodeRef.For(id, latest.Value), $"Form query {queryGuid:D}", Code: null),
                NodeRef.For(EnrollmentId.FromGuid(enrollmentGuid)));
        }

        return result;
    }
}
