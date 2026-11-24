using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;

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
/// lock signature precedes a lock assertion that has not been recorded. Participants (identity only) resolve to nothing.
/// Current-state objects are returned at their current version, whatever version the reference names; the returned
/// <see cref="NodeSummary.Ref"/> says which version that is.
/// </summary>
internal sealed class KernelScopeGraph(KernelTenantDbContextFactory contexts) : IKernelScopeGraph
{
    private const int ProposalLabelLength = 80;
    private const int ObservationValueLabelLength = 60;

    public async Task<ScopeNode?> FindAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        await using var db = contexts.Create(tenant);
        var tenantRef = NodeRef.For(tenant);

        switch (node.Kind)
        {
            case NodeKinds.Tenant:
                var tenantRow = await db.Tenants.AsNoTracking()
                    .Where(t => t.Id == tenant && t.Id == TenantId.FromGuid(node.Id))
                    .Select(t => new { t.Name, t.Code, t.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return tenantRow is null
                    ? null
                    : new ScopeNode(new NodeSummary(NodeRef.For(tenant, tenantRow.VersionIndex), tenantRow.Name, tenantRow.Code), ZoomParent: null);

            case NodeKinds.Organization:
                var organizationId = OrganizationId.FromGuid(node.Id);
                var organization = await db.Organizations.AsNoTracking()
                    .Where(o => o.TenantId == tenant && o.Id == organizationId)
                    .Select(o => new { o.Name, o.Code, o.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return organization is null
                    ? null
                    : new ScopeNode(new NodeSummary(NodeRef.For(organizationId, organization.VersionIndex), organization.Name, organization.Code), tenantRef);

            case NodeKinds.Site:
                var siteId = SiteId.FromGuid(node.Id);
                var site = await db.Sites.AsNoTracking()
                    .Where(s => s.TenantId == tenant && s.Id == siteId)
                    .Select(s => new { s.Name, s.SiteCode, s.InstitutionId, s.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return site is null
                    ? null
                    : new ScopeNode(new NodeSummary(NodeRef.For(siteId, site.VersionIndex), site.Name, site.SiteCode), NodeRef.For(site.InstitutionId));

            case NodeKinds.Study:
                var studyId = StudyId.FromGuid(node.Id);
                var study = await db.Studies.AsNoTracking()
                    .Where(s => s.TenantId == tenant && s.Id == studyId)
                    .Select(s => new { s.Title, s.StudyCode, s.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return study is null
                    ? null
                    : new ScopeNode(new NodeSummary(NodeRef.For(studyId, study.VersionIndex), study.Title, study.StudyCode), tenantRef);

            case NodeKinds.StudySite:
                var studySiteId = StudySiteId.FromGuid(node.Id);
                var studySite = await StudySitesWithSites(db, tenant)
                    .Where(x => x.Id == studySiteId)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return studySite is null
                    ? null
                    : new ScopeNode(StudySiteSummary(studySite), NodeRef.For(studySite.StudyId), studySite.Country);

            case NodeKinds.Enrollment:
                // A participant's place in a study is its enrollment at a study site (ADR-2453). The participant itself is
                // identity only and belongs to no single study, so the zoom level below a study site is the enrollment.
                var enrollmentId = EnrollmentId.FromGuid(node.Id);
                var enrollment = await db.Enrollments.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.Id == enrollmentId)
                    .Select(e => new { e.SubjectCode, e.StudyId, e.StudySiteId, e.Status, e.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return enrollment is null
                    ? null
                    : new ScopeNode(
                        EnrollmentSummary(enrollmentId, enrollment.SubjectCode, enrollment.Status, enrollment.VersionIndex),
                        ZoomRules.EnrollmentParent(enrollment.StudyId, enrollment.StudySiteId));

            case NodeKinds.CollectionInstance:
                var singleInstanceId = CollectionInstanceId.FromGuid(node.Id);
                return (await LoadCollectionInstancesAsync(db, tenant, [singleInstanceId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(singleInstanceId);

            case NodeKinds.Observation:
                var singleObservationId = ObservationId.FromGuid(node.Id);
                return (await LoadObservationsAsync(db, tenant, [singleObservationId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(singleObservationId);

            case NodeKinds.ProtocolVersion:
                var protocolVersionId = ProtocolVersionId.FromGuid(node.Id);
                var protocolVersion = await db.ProtocolVersions.AsNoTracking()
                    .Where(p => p.TenantId == tenant && p.Id == protocolVersionId)
                    .Select(p => new { p.StudyId, p.Number, p.Status, p.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return protocolVersion is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(
                            NodeRef.For(protocolVersionId, protocolVersion.VersionIndex),
                            $"Protocol version {protocolVersion.Number}",
                            $"v{protocolVersion.Number}",
                            protocolVersion.Status.ToString()),
                        NodeRef.For(protocolVersion.StudyId));

            case NodeKinds.ProtocolElement:
                return (await LoadProtocolElementsAsync(db, tenant, [node.Id], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(node.Id);

            case NodeKinds.FormQuery:
                return (await LoadFormQueriesAsync(db, tenant, [node.Id], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(node.Id);

            case NodeKinds.StudyStandardsProfile:
                var profileId = StudyStandardsProfileId.FromGuid(node.Id);
                var profile = await db.Set<StudyStandardsProfile>().AsNoTracking()
                    .Where(p => p.TenantId == tenant && p.Id == profileId)
                    .Select(p => new { p.StudyId, p.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return profile is null
                    ? null
                    : new ScopeNode(new NodeSummary(NodeRef.For(profileId, profile.VersionIndex), "Study standards", Code: null), NodeRef.For(profile.StudyId));

            case NodeKinds.Attestation:
                // An attestation sits under the object it signs, so a study's activity includes the signatures on it.
                // A lock signature commits before ApplyLock or ReleaseLock; if that lock command has not committed, fall
                // back to the tenant so zoom paths and activity projection remain valid.
                var attestationId = AttestationId.FromGuid(node.Id);
                var attestation = await db.Attestations.AsNoTracking()
                    .Where(a => a.TenantId == tenant && a.Id == attestationId)
                    .Select(a => new { a.Meaning, a.TargetKind, a.TargetId, a.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (attestation is null)
                {
                    return null;
                }

                var attestationParent = new NodeRef(attestation.TargetKind, attestation.TargetId);
                if (attestation.TargetKind == NodeKinds.Assertion)
                {
                    var recordedAssertion = await db.Assertions.AsNoTracking()
                        .AnyAsync(a => a.TenantId == tenant && a.Id == AssertionId.FromGuid(attestation.TargetId), cancellationToken)
                        .ConfigureAwait(false);
                    if (!recordedAssertion)
                    {
                        attestationParent = tenantRef;
                    }
                }

                return new ScopeNode(
                    new NodeSummary(NodeRef.For(attestationId, attestation.VersionIndex), $"{attestation.Meaning} attestation", Code: null),
                    attestationParent);

            case NodeKinds.Assertion:
                var assertionId = AssertionId.FromGuid(node.Id);
                var assertion = await db.Assertions.AsNoTracking()
                    .Where(a => a.TenantId == tenant && a.Id == assertionId)
                    .Select(a => new { a.Kind, a.TargetKind, a.TargetId, a.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return assertion is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(assertionId, assertion.VersionIndex), $"{assertion.Kind} assertion", Code: null, assertion.Kind.ToString()),
                        new NodeRef(assertion.TargetKind, assertion.TargetId));

            case NodeKinds.Artifact:
                var artifactId = ArtifactId.FromGuid(node.Id);
                var artifact = await db.Artifacts.AsNoTracking()
                    .Where(a => a.TenantId == tenant && a.Id == artifactId)
                    .Select(a => new { a.Title, a.Kind, a.StudyId, a.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (artifact is null)
                {
                    return null;
                }

                var artifactParent = artifact.StudyId is { } artifactStudyId ? NodeRef.For(artifactStudyId) : tenantRef;
                return new ScopeNode(
                    new NodeSummary(NodeRef.For(artifactId, artifact.VersionIndex), artifact.Title, Code: null, artifact.Kind.ToString()),
                    artifactParent);

            case NodeKinds.ArtifactVersion:
                var artifactVersionId = ArtifactVersionId.FromGuid(node.Id);
                var artifactVersion = await (
                        from v in db.ArtifactVersions.AsNoTracking()
                        join a in db.Artifacts.AsNoTracking()
                            on new { v.TenantId, Id = v.ArtifactId } equals new { a.TenantId, a.Id }
                        where v.TenantId == tenant && v.Id == artifactVersionId
                        select new { v.ArtifactId, v.Number, a.Title })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return artifactVersion is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(artifactVersionId, 1), $"{artifactVersion.Title} v{artifactVersion.Number}", $"v{artifactVersion.Number}"),
                        NodeRef.For(artifactVersion.ArtifactId));

            case NodeKinds.LibraryItem:
                var libraryItemId = LibraryItemId.FromGuid(node.Id);
                var libraryItem = await db.Set<LibraryItem>().AsNoTracking()
                    .Where(i => i.TenantId == tenant && i.Id == libraryItemId)
                    .Select(i => new { i.Title, i.Key, i.Status, i.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return libraryItem is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(libraryItemId, libraryItem.VersionIndex), libraryItem.Title, libraryItem.Key, libraryItem.Status.ToString()),
                        tenantRef);

            case NodeKinds.StandardVersion:
                var standardVersionId = StandardVersionId.FromGuid(node.Id);
                var standardVersion = await db.Set<StandardVersion>().AsNoTracking()
                    .Where(s => s.TenantId == tenant && s.Id == standardVersionId)
                    .Select(s => new { s.Name, s.StandardCode, s.Version, s.Status, s.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return standardVersion is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(
                            NodeRef.For(standardVersionId, standardVersion.VersionIndex),
                            $"{standardVersion.Name} {standardVersion.Version}",
                            $"{standardVersion.StandardCode} {standardVersion.Version}",
                            standardVersion.Status.ToString()),
                        tenantRef);

            case NodeKinds.TaskConfiguration:
                var taskConfigurationId = TaskConfigurationId.FromGuid(node.Id);
                var attestedTaskConfiguration = await db.Attestations.AsNoTracking()
                    .AnyAsync(a => a.TenantId == tenant && a.TargetKind == NodeKinds.TaskConfiguration && a.TargetId == node.Id, cancellationToken)
                    .ConfigureAwait(false);
                return !attestedTaskConfiguration
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(taskConfigurationId, 1), "Task configuration", Code: null),
                        tenantRef);

            case NodeKinds.ValidationRun:
                // A validation run and its findings sit under their study (ADR-2462), so a study's activity shows them.
                var runId = ValidationRunId.FromGuid(node.Id);
                var run = await db.Set<ValidationRun>().AsNoTracking()
                    .Where(r => r.TenantId == tenant && r.Id == runId)
                    .Select(r => new { r.StudyId, r.Stage, r.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return run is null
                    ? null
                    : new ScopeNode(new NodeSummary(NodeRef.For(runId, run.VersionIndex), $"{run.Stage} validation run", Code: null), NodeRef.For(run.StudyId));

            case NodeKinds.Proposal:
                // A proposal sits under its study (ADR-2489 §4.2), so the study's activity and navigation include it. The status
                // shown is the stored one: expiry is derived by readers that hold a clock.
                var proposalId = ProposalId.FromGuid(node.Id);
                var proposal = await db.Proposals.AsNoTracking()
                    .Where(p => p.TenantId == tenant && p.Id == proposalId)
                    .Select(p => new { p.StudyId, p.Intent, p.Status, p.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return proposal is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(proposalId, proposal.VersionIndex), ProposalLabel(proposal.Intent), Code: null, proposal.Status.ToString()),
                        NodeRef.For(proposal.StudyId));

            case NodeKinds.ValidationFinding:
                var findingId = ValidationFindingId.FromGuid(node.Id);
                var detection = await db.Set<ValidationDetectionRecord>().AsNoTracking()
                    .Where(d => d.TenantId == tenant && d.FindingId == findingId)
                    .Select(d => new { d.StudyId, d.RuleId, d.Dataset, d.TargetKey })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (detection is null)
                {
                    return null;
                }

                var decisions = await db.Set<ValidationFinding>().AsNoTracking()
                    .Where(f => f.TenantId == tenant && f.Id == findingId)
                    .Select(f => (long?)f.VersionIndex)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return new ScopeNode(
                    new NodeSummary(NodeRef.For(findingId, decisions), $"Finding {detection.RuleId} on {detection.Dataset} {detection.TargetKey}", detection.RuleId),
                    NodeRef.For(detection.StudyId));

            case NodeKinds.Concept:
                return await ConceptAsync(db, tenant, tenantRef, node, cancellationToken).ConfigureAwait(false);

            default:
                return null;
        }
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
                var studySites = await StudySitesWithSites(db, tenant)
                    .Where(x => x.StudyId == studyId)
                    .OrderBy(x => x.Country).ThenBy(x => x.SiteCode)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var enrollmentCounts = await db.Enrollments.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.StudyId == studyId)
                    .GroupBy(e => e.StudySiteId)
                    .Select(g => new { StudySiteId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.StudySiteId, x => x.Count, cancellationToken).ConfigureAwait(false);
                return [.. studySites.Select(x => new ScopeChild(StudySiteSummary(x), enrollmentCounts.GetValueOrDefault(x.Id), x.Country))];

            case NodeKinds.StudySite:
                var parentSiteId = StudySiteId.FromGuid(node.Id);
                var enrollments = await db.Enrollments.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.StudySiteId == parentSiteId)
                    .Select(e => new { e.Id, e.SubjectCode, e.Status, e.VersionIndex })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                // Visits are not mapped in the kernel yet (#4727), so an enrollment's child count is unknown.
                return
                [
                    .. enrollments
                        .OrderBy(e => e.SubjectCode.Value, StringComparer.Ordinal)
                        .Select(e => new ScopeChild(EnrollmentSummary(e.Id, e.SubjectCode, e.Status, e.VersionIndex), ChildCount: null)),
                ];

            case NodeKinds.Enrollment:
                return await EnrollmentChildrenAsync(db, tenant, EnrollmentId.FromGuid(node.Id), cancellationToken)
                    .ConfigureAwait(false);

            case NodeKinds.CollectionInstance:
                return await CollectionInstanceChildrenAsync(db, tenant, CollectionInstanceId.FromGuid(node.Id), cancellationToken)
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
                    NodeRef.For(site.InstitutionId));
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
                    studySite.Country);
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
                    ZoomRules.EnrollmentParent(enrollment.StudyId, enrollment.StudySiteId));
            }
        }

        var collectionInstanceIds = distinct.Where(n => n.Kind == NodeKinds.CollectionInstance).Select(n => CollectionInstanceId.FromGuid(n.Id)).ToList();
        if (collectionInstanceIds.Count > 0)
        {
            foreach (var (instanceId, scopeNode) in await LoadCollectionInstancesAsync(db, tenant, collectionInstanceIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(instanceId)] = scopeNode;
            }
        }

        var observationIds = distinct.Where(n => n.Kind == NodeKinds.Observation).Select(n => ObservationId.FromGuid(n.Id)).ToList();
        if (observationIds.Count > 0)
        {
            foreach (var (observationId, scopeNode) in await LoadObservationsAsync(db, tenant, observationIds, cancellationToken).ConfigureAwait(false))
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

        await PopulateGovernanceAndValidationNodesAsync(db, tenant, tenantRef, distinct, firstByUnversioned, found, cancellationToken)
            .ConfigureAwait(false);

        return found;
    }

    private static async Task PopulateGovernanceAndValidationNodesAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        List<NodeRef> distinct,
        Dictionary<NodeRef, NodeRef> firstByUnversioned,
        Dictionary<NodeRef, ScopeNode> found,
        CancellationToken cancellationToken)
    {
        var attestationIds = distinct.Where(n => n.Kind == NodeKinds.Attestation).Select(n => AttestationId.FromGuid(n.Id)).ToList();
        if (attestationIds.Count > 0)
        {
            var attestations = await db.Attestations.AsNoTracking()
                .Where(a => a.TenantId == tenant && attestationIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Meaning, a.TargetKind, a.TargetId, a.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var assertionTargetIds = attestations
                .Where(a => a.TargetKind == NodeKinds.Assertion)
                .Select(a => AssertionId.FromGuid(a.TargetId))
                .Distinct()
                .ToList();
            var recordedAssertions = assertionTargetIds.Count == 0
                ? []
                : (await db.Assertions.AsNoTracking()
                    .Where(a => a.TenantId == tenant && assertionTargetIds.Contains(a.Id))
                    .Select(a => a.Id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
            foreach (var attestation in attestations)
            {
                var parent = attestation.TargetKind == NodeKinds.Assertion
                    && !recordedAssertions.Contains(AssertionId.FromGuid(attestation.TargetId))
                    ? tenantRef
                    : new NodeRef(attestation.TargetKind, attestation.TargetId);
                found[NodeRef.For(attestation.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(attestation.Id, attestation.VersionIndex), $"{attestation.Meaning} attestation", Code: null),
                    parent);
            }
        }

        var assertionIds = distinct.Where(n => n.Kind == NodeKinds.Assertion).Select(n => AssertionId.FromGuid(n.Id)).ToList();
        if (assertionIds.Count > 0)
        {
            var assertions = await db.Assertions.AsNoTracking()
                .Where(a => a.TenantId == tenant && assertionIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Kind, a.TargetKind, a.TargetId, a.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var assertion in assertions)
            {
                found[NodeRef.For(assertion.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(assertion.Id, assertion.VersionIndex), $"{assertion.Kind} assertion", Code: null, assertion.Kind.ToString()),
                    new NodeRef(assertion.TargetKind, assertion.TargetId));
            }
        }

        var artifactIds = distinct.Where(n => n.Kind == NodeKinds.Artifact).Select(n => ArtifactId.FromGuid(n.Id)).ToList();
        if (artifactIds.Count > 0)
        {
            var artifacts = await db.Artifacts.AsNoTracking()
                .Where(a => a.TenantId == tenant && artifactIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Title, a.Kind, a.StudyId, a.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var artifact in artifacts)
            {
                var parent = artifact.StudyId is { } artifactStudyId ? NodeRef.For(artifactStudyId) : tenantRef;
                found[NodeRef.For(artifact.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(artifact.Id, artifact.VersionIndex), artifact.Title, Code: null, artifact.Kind.ToString()),
                    parent);
            }
        }

        var artifactVersionIds = distinct.Where(n => n.Kind == NodeKinds.ArtifactVersion).Select(n => ArtifactVersionId.FromGuid(n.Id)).ToList();
        if (artifactVersionIds.Count > 0)
        {
            var artifactVersions = await (
                    from v in db.ArtifactVersions.AsNoTracking()
                    join a in db.Artifacts.AsNoTracking()
                        on new { v.TenantId, Id = v.ArtifactId } equals new { a.TenantId, a.Id }
                    where v.TenantId == tenant && artifactVersionIds.Contains(v.Id)
                    select new { v.Id, v.ArtifactId, v.Number, a.Title })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var av in artifactVersions)
            {
                found[NodeRef.For(av.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(av.Id, 1), $"{av.Title} v{av.Number}", $"v{av.Number}"),
                    NodeRef.For(av.ArtifactId));
            }
        }

        var libraryItemIds = distinct.Where(n => n.Kind == NodeKinds.LibraryItem).Select(n => LibraryItemId.FromGuid(n.Id)).ToList();
        if (libraryItemIds.Count > 0)
        {
            var libraryItems = await db.Set<LibraryItem>().AsNoTracking()
                .Where(i => i.TenantId == tenant && libraryItemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Title, i.Key, i.Status, i.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in libraryItems)
            {
                found[NodeRef.For(item.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(item.Id, item.VersionIndex), item.Title, item.Key, item.Status.ToString()),
                    tenantRef);
            }
        }

        var standardVersionIds = distinct.Where(n => n.Kind == NodeKinds.StandardVersion).Select(n => StandardVersionId.FromGuid(n.Id)).ToList();
        if (standardVersionIds.Count > 0)
        {
            var standardVersions = await db.Set<StandardVersion>().AsNoTracking()
                .Where(s => s.TenantId == tenant && standardVersionIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name, s.StandardCode, s.Version, s.Status, s.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var sv in standardVersions)
            {
                found[NodeRef.For(sv.Id)] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(sv.Id, sv.VersionIndex),
                        $"{sv.Name} {sv.Version}",
                        $"{sv.StandardCode} {sv.Version}",
                        sv.Status.ToString()),
                    tenantRef);
            }
        }

        var taskConfigGuids = distinct.Where(n => n.Kind == NodeKinds.TaskConfiguration).Select(n => n.Id).ToList();
        if (taskConfigGuids.Count > 0)
        {
            var attestedTaskConfigs = await db.Attestations.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.TargetKind == NodeKinds.TaskConfiguration && taskConfigGuids.Contains(a.TargetId))
                .Select(a => a.TargetId)
                .Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var configGuid in attestedTaskConfigs)
            {
                var configId = TaskConfigurationId.FromGuid(configGuid);
                found[NodeRef.For(configId)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(configId, 1), "Task configuration", Code: null),
                    tenantRef);
            }
        }

        var runIds = distinct.Where(n => n.Kind == NodeKinds.ValidationRun).Select(n => ValidationRunId.FromGuid(n.Id)).ToList();
        if (runIds.Count > 0)
        {
            var runs = await db.Set<ValidationRun>().AsNoTracking()
                .Where(r => r.TenantId == tenant && runIds.Contains(r.Id))
                .Select(r => new { r.Id, r.StudyId, r.Stage, r.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var run in runs)
            {
                found[NodeRef.For(run.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(run.Id, run.VersionIndex), $"{run.Stage} validation run", Code: null),
                    NodeRef.For(run.StudyId));
            }
        }

        var proposalIds = distinct.Where(n => n.Kind == NodeKinds.Proposal).Select(n => ProposalId.FromGuid(n.Id)).ToList();
        if (proposalIds.Count > 0)
        {
            var proposals = await db.Proposals.AsNoTracking()
                .Where(p => p.TenantId == tenant && proposalIds.Contains(p.Id))
                .Select(p => new { p.Id, p.StudyId, p.Intent, p.Status, p.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var proposal in proposals)
            {
                found[NodeRef.For(proposal.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(proposal.Id, proposal.VersionIndex), ProposalLabel(proposal.Intent), Code: null, proposal.Status.ToString()),
                    NodeRef.For(proposal.StudyId));
            }
        }

        var findingIds = distinct.Where(n => n.Kind == NodeKinds.ValidationFinding).Select(n => ValidationFindingId.FromGuid(n.Id)).ToList();
        if (findingIds.Count > 0)
        {
            var detections = await db.Set<ValidationDetectionRecord>().AsNoTracking()
                .Where(d => d.TenantId == tenant && findingIds.Contains(d.FindingId))
                .Select(d => new { d.FindingId, d.StudyId, d.RuleId, d.Dataset, d.TargetKey })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var detectionsByFinding = detections
                .GroupBy(d => d.FindingId)
                .ToDictionary(g => g.Key, g => g.First());
            if (detectionsByFinding.Count > 0)
            {
                var matchedFindingIds = detectionsByFinding.Keys.ToList();
                var decisions = await db.Set<ValidationFinding>().AsNoTracking()
                    .Where(f => f.TenantId == tenant && matchedFindingIds.Contains(f.Id))
                    .Select(f => new { f.Id, f.VersionIndex })
                    .ToDictionaryAsync(f => f.Id, f => (long?)f.VersionIndex, cancellationToken).ConfigureAwait(false);
                foreach (var (findingId, detection) in detectionsByFinding)
                {
                    decisions.TryGetValue(findingId, out var versionIndex);
                    found[NodeRef.For(findingId)] = new ScopeNode(
                        new NodeSummary(NodeRef.For(findingId, versionIndex), $"Finding {detection.RuleId} on {detection.Dataset} {detection.TargetKey}", detection.RuleId),
                        NodeRef.For(detection.StudyId));
                }
            }
        }

        var conceptKeys = distinct.Where(n => n.Kind == NodeKinds.Concept).ToList();
        if (conceptKeys.Count > 0)
        {
            var requestedConcepts = conceptKeys.Select(k => firstByUnversioned[k]).ToList();
            foreach (var (conceptId, scopeNode) in await LoadConceptsAsync(db, tenant, tenantRef, requestedConcepts, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(conceptId)] = scopeNode;
            }
        }
    }

    public async Task<IReadOnlyList<StudyEnrollmentNode>?> StudyEnrollmentsAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var studySites = StudySitesWithSites(db, tenant).Where(ss => ss.StudyId == study);
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
                    SiteLabel(r.HasSite, r.SiteCode, r.SiteName))),
        ];
    }

    private static string? SiteLabel(bool hasSite, string? siteCode, string? siteName)
    {
        if (!hasSite)
        {
            return null;
        }

        return string.IsNullOrEmpty(siteCode) ? siteName : siteCode;
    }

    /// <summary>
    /// Protocol elements (ADR-2474) sit under the protocol version that last governed them, at their latest lineage
    /// version, labelled with their element id from that version's content.
    /// </summary>
    private static async Task<Dictionary<Guid, ScopeNode>> LoadProtocolElementsAsync(
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
                NodeRef.For(versionId));
        }

        return result;
    }

    /// <summary>
    /// Discrepancy queries (<c>FormQuery</c>, ADR-2625) sit under the enrollment that governs them in lineage, at their
    /// latest recorded lineage version, so enrollment, study-site, and study activity streams include query workflow events.
    /// </summary>
    private static async Task<Dictionary<Guid, ScopeNode>> LoadFormQueriesAsync(
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

    /// <summary>A proposal's label: the start of its intent, on one line.</summary>
    private static string ProposalLabel(string intent)
    {
        var line = intent.ReplaceLineEndings(" ").Trim();
        return line.Length <= ProposalLabelLength ? $"Proposal: {line}" : $"Proposal: {line[..(ProposalLabelLength - 1)]}…";
    }

    private static async Task<ScopeNode?> ConceptAsync(
        KernelDbContext db, TenantId tenant, NodeRef tenantRef, NodeRef node, CancellationToken cancellationToken) =>
        (await LoadConceptsAsync(db, tenant, tenantRef, [node], cancellationToken).ConfigureAwait(false))
            .GetValueOrDefault(ConceptId.FromGuid(node.Id));

    private static async Task<Dictionary<ConceptId, ScopeNode>> LoadConceptsAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        IReadOnlyCollection<NodeRef> requestedNodes,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ConceptId, ScopeNode>(requestedNodes.Count);
        var requestedByConcept = new Dictionary<ConceptId, long?>();
        foreach (var node in requestedNodes)
        {
            requestedByConcept.TryAdd(ConceptId.FromGuid(node.Id), node.Version);
        }

        var conceptIds = requestedByConcept.Keys.ToList();
        var concepts = await db.Concepts.AsNoTracking()
            .Where(c => c.TenantId == tenant && conceptIds.Contains(c.Id))
            .Select(c => new { c.Id, c.PreferredLabel, c.ExternalCode, c.Status, c.Version })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (concepts.Count == 0)
        {
            return result;
        }

        var revisionConceptIds = concepts
            .Where(c => requestedByConcept.TryGetValue(c.Id, out var reqVer)
                && reqVer is { } rv
                && rv != c.Version
                && rv <= int.MaxValue)
            .Select(c => c.Id)
            .ToList();
        var revisionLabels = revisionConceptIds.Count == 0
            ? []
            : (await db.ConceptRevisions.AsNoTracking()
                .Where(r => r.TenantId == tenant && revisionConceptIds.Contains(r.ConceptId))
                .Select(r => new { r.ConceptId, r.Version, r.PreferredLabel })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(r => (r.ConceptId, r.Version), r => r.PreferredLabel);

        foreach (var concept in concepts)
        {
            var label = concept.PreferredLabel;
            long resolvedVersion = concept.Version;
            if (requestedByConcept.TryGetValue(concept.Id, out var reqVer)
                && reqVer is { } requestedVersion
                && requestedVersion != concept.Version
                && requestedVersion <= int.MaxValue
                && revisionLabels.TryGetValue((concept.Id, (int)requestedVersion), out var revisionLabel)
                && !string.IsNullOrWhiteSpace(revisionLabel))
            {
                label = revisionLabel;
                resolvedVersion = requestedVersion;
            }

            result[concept.Id] = new ScopeNode(
                new NodeSummary(NodeRef.For(concept.Id, resolvedVersion), label, concept.ExternalCode, concept.Status.ToString()),
                tenantRef);
        }

        return result;
    }

    private static string ObservationLabel(string? conceptLabel, ObservationValue value)
    {
        var cleanConcept = string.IsNullOrWhiteSpace(conceptLabel) ? null : conceptLabel.Trim();
        string? valueText = null;
        if (value is not MissingValue { Reason: NullFlavor.NotAsked })
        {
            var singleLine = value.ToDisplayString().ReplaceLineEndings(" ").Trim();
            if (singleLine.Length > 0)
            {
                valueText = singleLine.Length <= ObservationValueLabelLength
                    ? singleLine
                    : $"{singleLine[..(ObservationValueLabelLength - 1)]}…";
            }
        }

        if (cleanConcept is not null && valueText is not null)
        {
            return $"{cleanConcept}: {valueText}";
        }

        return cleanConcept ?? valueText ?? "Observation";
    }

    private static IQueryable<StudySiteRow> StudySitesWithSites(KernelDbContext db, TenantId tenant) =>
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

    private static NodeSummary EnrollmentSummary(EnrollmentId id, SubjectCode code, EnrollmentStatus status, long versionIndex) =>
        new(NodeRef.For(id, versionIndex), code.Value, code.Value, status.ToString());

    private static NodeSummary StudySiteSummary(StudySiteRow row) =>
        new(NodeRef.For(row.Id, row.VersionIndex), row.SiteName, row.SiteCode, row.Status);

    private static async Task<Dictionary<ProtocolVersionId, ProtocolDesign?>> LoadDesignsByVersionAsync(
        KernelDbContext db,
        TenantId tenant,
        IEnumerable<ProtocolVersionId> protocolVersionIds,
        CancellationToken cancellationToken)
    {
        var versionIds = protocolVersionIds.Distinct().ToList();
        if (versionIds.Count == 0)
        {
            return [];
        }

        return (await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Content })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(
                v => v.Id,
                v => ProtocolDesignDocument.Read(v.Content).Design);
    }

    private static async Task<Dictionary<CollectionInstanceId, ScopeNode>> LoadCollectionInstancesAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instanceIds,
        CancellationToken cancellationToken)
    {
        var instances = await db.CollectionInstances.AsNoTracking()
            .Where(c => c.TenantId == tenant && instanceIds.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return [];
        }

        var designsByVersion = await LoadDesignsByVersionAsync(
                db,
                tenant,
                instances.Select(i => i.ProtocolVersionId),
                cancellationToken)
            .ConfigureAwait(false);
        var result = new Dictionary<CollectionInstanceId, ScopeNode>(instances.Count);
        foreach (var instance in instances)
        {
            var design = designsByVersion.GetValueOrDefault(instance.ProtocolVersionId);
            result[instance.Id] = new ScopeNode(CollectionInstanceSummary(instance, design), NodeRef.For(instance.EnrollmentId));
        }

        return result;
    }

    private static async Task<Dictionary<ObservationId, ScopeNode>> LoadObservationsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observationIds,
        CancellationToken cancellationToken)
    {
        var observations = await db.Observations.AsNoTracking()
            .Where(o => o.TenantId == tenant && observationIds.Contains(o.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (observations.Count == 0)
        {
            return [];
        }

        var enrollmentIds = observations.Select(o => o.EnrollmentId).Distinct().ToList();
        var parentInstances = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.TenantId == tenant
                && enrollmentIds.Contains(c.EnrollmentId)
                && c.Members.Any(m => observationIds.Contains(m.ObservationId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var parentByObservation = new Dictionary<ObservationId, (CollectionInstance Instance, string ItemId)>();
        foreach (var instance in parentInstances)
        {
            foreach (var member in instance.Members)
            {
                if (observationIds.Contains(member.ObservationId))
                {
                    parentByObservation.TryAdd(member.ObservationId, (instance, member.ItemId));
                }
            }
        }

        var designsByVersion = await LoadDesignsByVersionAsync(
                db,
                tenant,
                parentInstances.Select(p => p.ProtocolVersionId),
                cancellationToken)
            .ConfigureAwait(false);
        var concepts = await ResolveConceptMetadataAsync(db, tenant, observations, cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<ObservationId, ScopeNode>(observations.Count);
        foreach (var observation in observations)
        {
            CollectionItem? item = null;
            string? itemId = null;
            CollectionInstance? parentInstance = null;
            if (parentByObservation.TryGetValue(observation.Id, out var parentInfo))
            {
                parentInstance = parentInfo.Instance;
                itemId = parentInfo.ItemId;
                var spec = designsByVersion.GetValueOrDefault(parentInstance.ProtocolVersionId)?.CollectionSpecs
                    .FirstOrDefault(s => string.Equals(s.Id, parentInstance.CollectionSpecId, StringComparison.Ordinal));
                item = spec?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
            }

            var (label, externalCode) = concepts.GetValueOrDefault((observation.ConceptId, observation.ConceptVersion));
            var parentRef = parentInstance is not null
                ? NodeRef.For(parentInstance.Id)
                : NodeRef.For(observation.EnrollmentId);
            result[observation.Id] = new ScopeNode(ObservationSummary(observation, item, itemId, label, externalCode), parentRef);
        }

        return result;
    }

    private static async Task<IReadOnlyList<ScopeChild>> EnrollmentChildrenAsync(
        KernelDbContext db,
        TenantId tenant,
        EnrollmentId enrollmentId,
        CancellationToken cancellationToken)
    {
        var instances = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.TenantId == tenant && c.EnrollmentId == enrollmentId)
            .OrderBy(c => c.OpenedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var designsByVersion = await LoadDesignsByVersionAsync(
                db,
                tenant,
                instances.Select(c => c.ProtocolVersionId),
                cancellationToken)
            .ConfigureAwait(false);

        var children = new List<ScopeChild>(instances.Count);
        var collectedObservationIds = new HashSet<ObservationId>();
        foreach (var instance in instances)
        {
            foreach (var member in instance.Members)
            {
                collectedObservationIds.Add(member.ObservationId);
            }

            var design = designsByVersion.GetValueOrDefault(instance.ProtocolVersionId);
            children.Add(new ScopeChild(CollectionInstanceSummary(instance, design), instance.Members.Count, instance.PlannedActivityId));
        }

        var standaloneObservations = (await db.Observations.AsNoTracking()
                .Where(o => o.TenantId == tenant && o.EnrollmentId == enrollmentId)
                .OrderBy(o => o.ObservedAt).ThenBy(o => o.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(o => !collectedObservationIds.Contains(o.Id))
            .ToList();
        var concepts = await ResolveConceptMetadataAsync(db, tenant, standaloneObservations, cancellationToken).ConfigureAwait(false);
        foreach (var observation in standaloneObservations)
        {
            var (Label, ExternalCode) = concepts.GetValueOrDefault((observation.ConceptId, observation.ConceptVersion));
            children.Add(new ScopeChild(
                ObservationSummary(observation, item: null, itemId: null, Label, ExternalCode),
                ChildCount: 0));
        }

        return children;
    }

    private static async Task<IReadOnlyList<ScopeChild>> CollectionInstanceChildrenAsync(
        KernelDbContext db,
        TenantId tenant,
        CollectionInstanceId instanceId,
        CancellationToken cancellationToken)
    {
        var instance = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .SingleOrDefaultAsync(c => c.TenantId == tenant && c.Id == instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null || instance.Members.Count == 0)
        {
            return [];
        }

        var memberIds = instance.Members.Select(m => m.ObservationId).ToList();
        var observations = await db.Observations.AsNoTracking()
            .Where(o => o.TenantId == tenant && memberIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken)
            .ConfigureAwait(false);
        var concepts = await ResolveConceptMetadataAsync(db, tenant, observations.Values, cancellationToken).ConfigureAwait(false);

        var content = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.Id == instance.ProtocolVersionId)
            .Select(v => v.Content)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var spec = content is null
            ? null
            : ProtocolDesignDocument.Read(content).Design?.CollectionSpecs
                .FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
        var itemOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        if (spec is not null)
        {
            for (var index = 0; index < spec.Items.Count; index++)
            {
                itemOrder[spec.Items[index].Id] = index;
            }
        }

        var itemsById = spec?.Items.ToDictionary(i => i.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, CollectionItem>(StringComparer.Ordinal);

        return
        [
            .. instance.Members
                .Where(m => observations.ContainsKey(m.ObservationId))
                .OrderBy(m => itemOrder.TryGetValue(m.ItemId, out var rank) ? rank : int.MaxValue)
                .ThenBy(m => m.ItemId, StringComparer.Ordinal)
                .Select(m =>
                {
                    var observation = observations[m.ObservationId];
                    var (Label, ExternalCode) = concepts.GetValueOrDefault((observation.ConceptId, observation.ConceptVersion));
                    return new ScopeChild(
                        ObservationSummary(observation, itemsById.GetValueOrDefault(m.ItemId), m.ItemId, Label, ExternalCode),
                        ChildCount: 0);
                }),
        ];
    }

    private static async Task<Dictionary<(ConceptId ConceptId, int Version), (string? Label, string? ExternalCode)>> ResolveConceptMetadataAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<Observation> observations,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(ConceptId, int), (string?, string?)>();
        if (observations.Count == 0)
        {
            return result;
        }

        var conceptIds = observations.Select(o => o.ConceptId).Distinct().ToList();
        var concepts = await db.Concepts.AsNoTracking()
            .Where(c => c.TenantId == tenant && conceptIds.Contains(c.Id))
            .Select(c => new { c.Id, c.PreferredLabel, c.ExternalCode })
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);
        var revisions = await db.ConceptRevisions.AsNoTracking()
            .Where(r => r.TenantId == tenant && conceptIds.Contains(r.ConceptId))
            .Select(r => new { r.ConceptId, r.Version, r.PreferredLabel })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var revisionLabels = revisions.ToDictionary(r => (r.ConceptId, r.Version), r => r.PreferredLabel);

        foreach (var key in observations.Select(o => (o.ConceptId, o.ConceptVersion)).Distinct())
        {
            concepts.TryGetValue(key.ConceptId, out var current);
            var label = revisionLabels.TryGetValue(key, out var revLabel) && !string.IsNullOrWhiteSpace(revLabel)
                ? revLabel
                : current?.PreferredLabel;
            result[key] = (label, current?.ExternalCode);
        }

        return result;
    }

    private static NodeSummary CollectionInstanceSummary(CollectionInstance instance, ProtocolDesign? design)
    {
        var specName = design?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal))?.Name;
        var formTitle = string.IsNullOrWhiteSpace(specName) ? instance.CollectionSpecId : specName.Trim();

        string? visitTitle = null;
        if (!string.IsNullOrWhiteSpace(instance.PlannedActivityId))
        {
            var activityName = design?.PlannedActivities
                .FirstOrDefault(a => string.Equals(a.Id, instance.PlannedActivityId, StringComparison.Ordinal))?.Name;
            visitTitle = string.IsNullOrWhiteSpace(activityName) ? instance.PlannedActivityId.Trim() : activityName.Trim();
        }

        var title = string.IsNullOrWhiteSpace(visitTitle) ? formTitle : $"{formTitle} ({visitTitle})";
        return new NodeSummary(NodeRef.For(instance.Id, instance.VersionIndex), title, instance.CollectionSpecId);
    }

    private static NodeSummary ObservationSummary(
        Observation observation,
        CollectionItem? item,
        string? itemId,
        string? conceptLabel,
        string? conceptExternalCode)
    {
        var conceptCode = observation.Concept.ToString();
        string label;
        if (!string.IsNullOrWhiteSpace(item?.Prompt))
        {
            label = item.Prompt.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(conceptLabel))
        {
            label = conceptLabel.Trim();
        }
        else
        {
            label = itemId ?? conceptExternalCode ?? conceptCode;
        }

        var code = itemId ?? conceptExternalCode ?? conceptCode;
        return new NodeSummary(
            NodeRef.For(observation.Id, observation.Revision),
            ObservationLabel(label, observation.Value),
            code,
            $"r{observation.Revision}");
    }

    private sealed class StudySiteRow
    {
        public StudySiteId Id { get; init; }

        public StudyId StudyId { get; init; }

        public long VersionIndex { get; init; }

        public string Status { get; init; } = string.Empty;

        public string SiteName { get; init; } = string.Empty;

        public string SiteCode { get; init; } = string.Empty;

        public string Country { get; init; } = string.Empty;
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
