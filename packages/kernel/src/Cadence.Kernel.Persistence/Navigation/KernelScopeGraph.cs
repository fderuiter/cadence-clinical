using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Authorization;
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
/// lock signature precedes a lock assertion that has not been recorded. Pseudonymous participant and person identities resolve
/// directly under the tenant without direct identifiers (K-IDN-1).
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

            case NodeKinds.Person:
                var personId = PersonId.FromGuid(node.Id);
                var person = await (
                        from p in db.People.AsNoTracking()
                        where p.Id == personId
                            && db.TenantMemberships.Any(m => m.TenantId == tenant && m.PersonId == p.Id && m.IsActive)
                        select new { p.IsActive, p.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (person is null)
                {
                    return null;
                }

                var personStatus = person.IsActive ? "Active" : "Inactive";
                return new ScopeNode(
                    new NodeSummary(NodeRef.For(personId, person.VersionIndex), "Person", Code: null, personStatus),
                    tenantRef);

            case NodeKinds.TenantMembership:
                var membershipId = TenantMembershipId.FromGuid(node.Id);
                var membership = await db.TenantMemberships.AsNoTracking()
                    .Where(m => m.TenantId == tenant && m.Id == membershipId)
                    .Select(m => new { m.IsActive, m.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (membership is null)
                {
                    return null;
                }

                var membershipStatus = membership.IsActive ? "Active" : "Inactive";
                return new ScopeNode(
                    new NodeSummary(NodeRef.For(membershipId, membership.VersionIndex), "Tenant membership", Code: null, membershipStatus),
                    tenantRef);

            case NodeKinds.Site:
                var siteId = SiteId.FromGuid(node.Id);
                var site = await db.Sites.AsNoTracking()
                    .Where(s => s.TenantId == tenant && s.Id == siteId)
                    .Select(s => new { s.Name, s.SiteCode, s.InstitutionId, s.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return site is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(siteId, site.VersionIndex), site.Name, site.SiteCode),
                        NodeRef.For(site.InstitutionId),
                        FallbackParent: tenantRef);

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
                    : new ScopeNode(StudySiteSummary(studySite), NodeRef.For(studySite.StudyId), studySite.Country, FallbackParent: tenantRef);

            case NodeKinds.Participant:
                var participantId = ParticipantId.FromGuid(node.Id);
                var participant = await db.Participants.AsNoTracking()
                    .Where(p => p.TenantId == tenant && p.Id == participantId)
                    .Select(p => (long?)p.VersionIndex)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return participant is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(participantId, participant.Value), "Participant", Code: null),
                        tenantRef);

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
                        ZoomRules.EnrollmentParent(enrollment.StudyId, enrollment.StudySiteId),
                        FallbackParent: NodeRef.For(enrollment.StudyId));

            case NodeKinds.CollectionInstance:
                return await CollectionInstanceNodeAsync(db, tenant, CollectionInstanceId.FromGuid(node.Id), cancellationToken)
                    .ConfigureAwait(false);

            case NodeKinds.Observation:
                return await ObservationNodeAsync(db, tenant, ObservationId.FromGuid(node.Id), cancellationToken)
                    .ConfigureAwait(false);

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

            case NodeKinds.FormVersionSnapshot:
                var snapshotId = FormVersionSnapshotId.FromGuid(node.Id);
                var snapshot = await db.FormVersionSnapshots.AsNoTracking()
                    .Where(s => s.TenantId == tenant && s.Id == snapshotId)
                    .Select(s => new { s.StudyId, s.ProtocolVersionId, s.ProtocolVersion, s.FormId, s.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return snapshot is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(
                            NodeRef.For(snapshotId, snapshot.VersionIndex),
                            $"Form version snapshot {snapshot.FormId}",
                            snapshot.FormId),
                        NodeRef.For(snapshot.ProtocolVersionId),
                        FallbackParent: NodeRef.For(snapshot.StudyId));

            case NodeKinds.ProtocolElement:
                return await ProtocolElementAsync(db, tenant, node.Id, cancellationToken).ConfigureAwait(false);

            case NodeKinds.FormQuery:
                return await FormQueryAsync(db, tenant, node.Id, cancellationToken).ConfigureAwait(false);

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
                    attestationParent,
                    FallbackParent: tenantRef);

            case NodeKinds.Assertion:
                var assertionId = AssertionId.FromGuid(node.Id);
                var assertion = await db.Assertions.AsNoTracking()
                    .Where(a => a.TenantId == tenant && a.Id == assertionId)
                    .Select(a => new { a.Kind, a.StudyId, a.TargetKind, a.TargetId, a.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return assertion is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(assertionId, assertion.VersionIndex), $"{assertion.Kind} assertion", Code: null, assertion.Kind.ToString()),
                        new NodeRef(assertion.TargetKind, assertion.TargetId),
                        FallbackParent: NodeRef.For(assertion.StudyId));

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
                            on new { v.TenantId, Id = v.ArtifactId } equals new { a.TenantId, a.Id } into artifactGroup
                        from a in artifactGroup.DefaultIfEmpty()
                        where v.TenantId == tenant && v.Id == artifactVersionId
                        select new { v.ArtifactId, v.Number, Title = a != null ? a.Title : null, StudyId = a != null ? a.StudyId : null })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (artifactVersion is null)
                {
                    return null;
                }

                var artifactVersionTitle = artifactVersion.Title is not null
                    ? $"{artifactVersion.Title} v{artifactVersion.Number}"
                    : $"Artifact v{artifactVersion.Number}";
                var artifactVersionFallback = artifactVersion.StudyId is { } avStudyId ? NodeRef.For(avStudyId) : tenantRef;
                return new ScopeNode(
                    new NodeSummary(NodeRef.For(artifactVersionId, 1), artifactVersionTitle, $"v{artifactVersion.Number}"),
                    NodeRef.For(artifactVersion.ArtifactId),
                    FallbackParent: artifactVersionFallback);

            case NodeKinds.Role:
                var roleId = RoleId.FromGuid(node.Id);
                var role = await db.Roles.AsNoTracking()
                    .Where(r => r.TenantId == tenant && r.Id == roleId)
                    .Select(r => new { r.Name, r.Code, r.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return role is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(roleId, role.VersionIndex), role.Name, role.Code),
                        tenantRef);

            case NodeKinds.Grant:
                var grantId = GrantId.FromGuid(node.Id);
                var grant = await (
                        from g in db.Grants.AsNoTracking()
                        join r in db.Roles.AsNoTracking()
                            on new { g.TenantId, Id = g.RoleId } equals new { r.TenantId, Id = (RoleId?)r.Id } into roleGroup
                        from r in roleGroup.DefaultIfEmpty()
                        where g.TenantId == tenant && g.Id == grantId
                        select new
                        {
                            g.Capability,
                            RoleCode = r != null ? r.Code : null,
                            g.ScopeLevel,
                            g.ScopeTargetId,
                            g.ScopeCountry,
                            g.VersionIndex,
                        })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (grant is null)
                {
                    return null;
                }

                var grantLabel = grant.Capability ?? grant.RoleCode;
                string grantTitle;
                if (grant.Capability is not null)
                {
                    grantTitle = $"{grant.Capability} grant";
                }
                else if (grant.RoleCode is not null)
                {
                    grantTitle = $"{grant.RoleCode} role grant";
                }
                else
                {
                    grantTitle = "Role grant";
                }
                var (grantParent, grantGroup, grantFallback) = ScopedAuthorityParent(tenant, grant.ScopeLevel, grant.ScopeTargetId, grant.ScopeCountry);
                return new ScopeNode(
                    new NodeSummary(NodeRef.For(grantId, grant.VersionIndex), grantTitle, grantLabel, grant.ScopeLevel.ToString()),
                    grantParent,
                    grantGroup,
                    grantFallback);

            case NodeKinds.Qualification:
                var qualificationId = QualificationId.FromGuid(node.Id);
                var qualification = await db.Qualifications.AsNoTracking()
                    .Where(q => q.TenantId == tenant && q.Id == qualificationId)
                    .Select(q => new { q.Kind, q.Code, q.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return qualification is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(qualificationId, qualification.VersionIndex), $"{qualification.Code} qualification", qualification.Code, qualification.Kind.ToString()),
                        tenantRef);

            case NodeKinds.QualificationRequirement:
                var requirementId = QualificationRequirementId.FromGuid(node.Id);
                var requirement = await db.QualificationRequirements.AsNoTracking()
                    .Where(r => r.TenantId == tenant && r.Id == requirementId)
                    .Select(r => new { r.Capability, r.QualificationCode, r.ScopeLevel, r.ScopeTargetId, r.ScopeCountry, r.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (requirement is null)
                {
                    return null;
                }

                var (reqParent, reqGroup, reqFallback) = ScopedAuthorityParent(tenant, requirement.ScopeLevel, requirement.ScopeTargetId, requirement.ScopeCountry);
                return new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(requirementId, requirement.VersionIndex),
                        $"Requires {requirement.QualificationCode} for {requirement.Capability}",
                        requirement.QualificationCode,
                        requirement.ScopeLevel.ToString()),
                    reqParent,
                    reqGroup,
                    reqFallback);

            case NodeKinds.Effectivity:
                var effectivityId = EffectivityId.FromGuid(node.Id);
                var effectivity = await db.Effectivities.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.Id == effectivityId)
                    .Select(e => new { e.StudyId, e.ScopeLevel, e.ScopeCountry, e.ScopeStudySiteId, e.Status, e.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (effectivity is null)
                {
                    return null;
                }

                var effectivityParent = effectivity.ScopeLevel == EffectivityLevel.StudySite && effectivity.ScopeStudySiteId is { IsEmpty: false } effSiteId
                    ? NodeRef.For(effSiteId)
                    : NodeRef.For(effectivity.StudyId);
                var effectivityGroup = effectivity.ScopeLevel == EffectivityLevel.Country ? effectivity.ScopeCountry : null;
                return new ScopeNode(
                    new NodeSummary(NodeRef.For(effectivityId, effectivity.VersionIndex), $"{effectivity.ScopeLevel} effectivity", Code: null, effectivity.Status.ToString()),
                    effectivityParent,
                    effectivityGroup,
                    FallbackParent: NodeRef.For(effectivity.StudyId));

            case NodeKinds.CodeSystem:
                var codeSystemId = CodeSystemId.FromGuid(node.Id);
                var codeSystem = await db.CodeSystems.AsNoTracking()
                    .Where(cs => cs.TenantId == tenant && cs.Id == codeSystemId)
                    .Select(cs => new { cs.Name, cs.Uri, cs.License, cs.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return codeSystem is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(codeSystemId, codeSystem.VersionIndex), codeSystem.Name, codeSystem.Uri, codeSystem.License.ToString()),
                        tenantRef);

            case NodeKinds.CodeSystemVersion:
                var codeSystemVersionId = CodeSystemVersionId.FromGuid(node.Id);
                var codeSystemVersion = await (
                        from v in db.CodeSystemVersions.AsNoTracking()
                        join cs in db.CodeSystems.AsNoTracking()
                            on new { v.TenantId, Id = v.CodeSystemId } equals new { cs.TenantId, cs.Id } into csGroup
                        from cs in csGroup.DefaultIfEmpty()
                        where v.TenantId == tenant && v.Id == codeSystemVersionId
                        select new { v.CodeSystemId, v.Version, v.VersionIndex, SystemName = cs != null ? cs.Name : null })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (codeSystemVersion is null)
                {
                    return null;
                }

                var codeSystemVersionTitle = codeSystemVersion.SystemName is not null
                    ? $"{codeSystemVersion.SystemName} {codeSystemVersion.Version}"
                    : codeSystemVersion.Version;
                return new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(codeSystemVersionId, codeSystemVersion.VersionIndex),
                        codeSystemVersionTitle,
                        codeSystemVersion.Version),
                    NodeRef.For(codeSystemVersion.CodeSystemId),
                    FallbackParent: tenantRef);

            case NodeKinds.Concept:
                return await ConceptAsync(db, tenant, tenantRef, node, cancellationToken).ConfigureAwait(false);

            case NodeKinds.ConceptMapping:
                var conceptMappingId = ConceptMappingId.FromGuid(node.Id);
                var conceptMapping = await db.ConceptMappings.AsNoTracking()
                    .Where(m => m.TenantId == tenant && m.Id == conceptMappingId)
                    .Select(m => new { m.ConceptId, m.Code, m.Relation, m.Status, m.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return conceptMapping is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(
                            NodeRef.For(conceptMappingId, conceptMapping.VersionIndex),
                            $"{conceptMapping.Relation} mapping to {conceptMapping.Code}",
                            conceptMapping.Code,
                            conceptMapping.Status.ToString()),
                        NodeRef.For(conceptMapping.ConceptId),
                        FallbackParent: tenantRef);

            case NodeKinds.WorkQueueDefinition:
                var workQueueId = WorkQueueDefinitionId.FromGuid(node.Id);
                var workQueue = await db.WorkQueueDefinitions.AsNoTracking()
                    .Where(q => q.TenantId == tenant && q.Id == workQueueId)
                    .Select(q => new { q.Title, q.Code, q.Version, q.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return workQueue is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(NodeRef.For(workQueueId, workQueue.VersionIndex), workQueue.Title, workQueue.Code, $"v{workQueue.Version}"),
                        tenantRef);

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
                var detection = await (
                        from d in db.Set<ValidationDetectionRecord>().AsNoTracking()
                        join r in db.Set<ValidationRun>().AsNoTracking()
                            on new { d.TenantId, Id = d.RunId } equals new { r.TenantId, r.Id }
                        where d.TenantId == tenant && d.FindingId == findingId
                        orderby r.VersionIndex descending, r.Id descending
                        select new { d.StudyId, d.RuleId, d.TargetKind, d.Dataset, d.TargetKey })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                var decisionRow = await db.Set<ValidationFinding>().AsNoTracking()
                    .Where(f => f.TenantId == tenant && f.Id == findingId)
                    .Select(f => new { f.StudyId, f.Decision, f.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (detection is null)
                {
                    return decisionRow is null
                        ? null
                        : new ScopeNode(
                            new NodeSummary(
                                NodeRef.For(findingId, decisionRow.VersionIndex),
                                "Validation finding",
                                Code: null,
                                decisionRow.Decision?.ToString()),
                            NodeRef.For(decisionRow.StudyId));
                }

                var findingStudyRef = NodeRef.For(detection.StudyId);
                var findingParent = await ValidationFindingParentAsync(
                        db,
                        tenant,
                        detection.StudyId,
                        detection.TargetKind,
                        detection.TargetKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(findingId, decisionRow?.VersionIndex),
                        $"Finding {detection.RuleId} on {detection.Dataset} {detection.TargetKey}",
                        detection.RuleId,
                        decisionRow?.Decision?.ToString()),
                    findingParent,
                    FallbackParent: findingStudyRef);

            case NodeKinds.Subscription:
                var subscriptionId = SubscriptionId.FromGuid(node.Id);
                var subscriptionVersion = await db.Subscriptions.AsNoTracking()
                    .Where(s => s.TenantId == tenant && s.Id == subscriptionId)
                    .Select(s => (long?)s.VersionIndex)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (subscriptionVersion is null)
                {
                    return null;
                }

                var currentPeriod = await db.SubscriptionPeriods.AsNoTracking()
                    .Where(p => p.TenantId == tenant && p.SubscriptionId == subscriptionId && p.Period.Recorded.IsOpen)
                    .OrderByDescending(p => p.Period.Valid.Start)
                    .Select(p => new { p.Tier, p.Status })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(subscriptionId, subscriptionVersion.Value),
                        currentPeriod is null ? "Subscription" : $"{currentPeriod.Tier} subscription",
                        currentPeriod?.Tier.ToString(),
                        currentPeriod?.Status.ToString()),
                    tenantRef);

            case NodeKinds.ProductEntitlement:
                var entitlementId = ProductEntitlementId.FromGuid(node.Id);
                var entitlement = await db.ProductEntitlements.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.Id == entitlementId)
                    .Select(e => new { e.SubscriptionId, e.Family, e.IsEnabled, e.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (entitlement is null)
                {
                    return null;
                }

                var entitlementStatus = entitlement.IsEnabled ? "Enabled" : "Disabled";
                return new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(entitlementId, entitlement.VersionIndex),
                        $"{entitlement.Family} entitlement",
                        entitlement.Family.ToString(),
                        entitlementStatus),
                    NodeRef.For(entitlement.SubscriptionId),
                    FallbackParent: tenantRef);

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
        var distinct = nodes.Select(n => n.Unversioned()).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<NodeRef, ScopeNode>();
        }

        var found = new Dictionary<NodeRef, ScopeNode>(distinct.Count);
        await using var db = contexts.Create(tenant);
        var tenantRef = NodeRef.For(tenant);

        var tenantIds = distinct.Where(n => n.Kind == NodeKinds.Tenant && n.Id == tenant.Value).ToList();
        if (tenantIds.Count > 0)
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

        var protocolVersionIds = distinct.Where(n => n.Kind == NodeKinds.ProtocolVersion).Select(n => ProtocolVersionId.FromGuid(n.Id)).ToList();
        if (protocolVersionIds.Count > 0)
        {
            var versions = await db.ProtocolVersions.AsNoTracking()
                .Where(p => p.TenantId == tenant && protocolVersionIds.Contains(p.Id))
                .Select(p => new { p.Id, p.StudyId, p.Number, p.Status, p.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var version in versions)
            {
                found[NodeRef.For(version.Id)] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(version.Id, version.VersionIndex),
                        $"Protocol version {version.Number}",
                        $"v{version.Number}",
                        version.Status.ToString()),
                    NodeRef.For(version.StudyId));
            }
        }

        var roleIds = distinct.Where(n => n.Kind == NodeKinds.Role).Select(n => RoleId.FromGuid(n.Id)).ToList();
        if (roleIds.Count > 0)
        {
            var roles = await db.Roles.AsNoTracking()
                .Where(r => r.TenantId == tenant && roleIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name, r.Code, r.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var role in roles)
            {
                found[NodeRef.For(role.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(role.Id, role.VersionIndex), role.Name, role.Code),
                    tenantRef);
            }
        }

        var conceptIds = distinct.Where(n => n.Kind == NodeKinds.Concept).Select(n => ConceptId.FromGuid(n.Id)).ToList();
        if (conceptIds.Count > 0)
        {
            var concepts = await db.Concepts.AsNoTracking()
                .Where(c => c.TenantId == tenant && conceptIds.Contains(c.Id))
                .Select(c => new { c.Id, c.PreferredLabel, c.ExternalCode, c.Status, c.Version })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var concept in concepts)
            {
                found[NodeRef.For(concept.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(concept.Id, concept.Version), concept.PreferredLabel, concept.ExternalCode, concept.Status.ToString()),
                    tenantRef);
            }
        }

        foreach (var remaining in distinct.Where(n => !IsBatchedKind(n.Kind)))
        {
            if (await FindAsync(tenant, remaining, cancellationToken).ConfigureAwait(false) is { } scopeNode)
            {
                found[remaining] = scopeNode;
            }
        }

        return found;
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
    /// A protocol element (ADR-2474) sits under the protocol version that last governed it, at its latest lineage version,
    /// labelled with its element id from that version's content.
    /// </summary>
    private static async Task<ScopeNode?> ProtocolElementAsync(KernelDbContext db, TenantId tenant, Guid element, CancellationToken cancellationToken)
    {
        if (await ProtocolElementOwner.GoverningVersionAsync(db, tenant, element, cancellationToken).ConfigureAwait(false) is not { } versionId)
        {
            return null;
        }

        var owner = await db.ProtocolVersions.AsNoTracking()
            .Where(p => p.TenantId == tenant && p.Id == versionId)
            .Select(p => new { p.StudyId, p.Content })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var latest = await db.LineageNodes.AsNoTracking()
            .Where(n => n.TenantId == tenant && n.Kind == NodeKinds.ProtocolElement && n.NodeId == element)
            .MaxAsync(n => (long?)n.Version, cancellationToken).ConfigureAwait(false);
        if (owner is null || latest is null)
        {
            return null;
        }

        var id = ProtocolElementId.FromGuid(element);
        var elements = ProtocolStructuralDiff.Compare(null, owner.Content);
        var elementId = elements.IsSuccess
            ? elements.Value!.Select(c => c.ElementId).FirstOrDefault(e => e != ProtocolStructuralDiff.RootId && ProtocolElementId.For(owner.StudyId, e) == id)
            : null;
        return new ScopeNode(
            new NodeSummary(NodeRef.For(id, latest.Value), elementId is null ? "Protocol element" : $"Protocol element {elementId}", elementId),
            NodeRef.For(versionId),
            FallbackParent: NodeRef.For(owner.StudyId));
    }

    private static bool IsBatchedKind(string kind) =>
        kind is NodeKinds.Tenant
            or NodeKinds.Organization
            or NodeKinds.Site
            or NodeKinds.Study
            or NodeKinds.StudySite
            or NodeKinds.Participant
            or NodeKinds.Person
            or NodeKinds.Enrollment
            or NodeKinds.ProtocolVersion
            or NodeKinds.Role
            or NodeKinds.Concept;

    private static (NodeRef Parent, string? Group, NodeRef? FallbackParent) ScopedAuthorityParent(
        TenantId tenant,
        ScopeLevel scopeLevel,
        Guid? scopeTargetId,
        string? scopeCountry)
    {
        var tenantRef = NodeRef.For(tenant);
        return (scopeLevel, scopeTargetId) switch
        {
            (ScopeLevel.StudySite, { } siteGuid) => (NodeRef.For(StudySiteId.FromGuid(siteGuid)), null, tenantRef),
            (ScopeLevel.Study, { } studyGuid) => (NodeRef.For(StudyId.FromGuid(studyGuid)), null, tenantRef),
            (ScopeLevel.StudyCountry, { } countryStudyGuid) => (NodeRef.For(StudyId.FromGuid(countryStudyGuid)), scopeCountry, tenantRef),
            (ScopeLevel.Organization, { } orgGuid) => (NodeRef.For(OrganizationId.FromGuid(orgGuid)), null, tenantRef),
            _ => (tenantRef, null, null),
        };
    }

    private static async Task<NodeRef> ValidationFindingParentAsync(
        KernelDbContext db,
        TenantId tenant,
        StudyId studyId,
        FindingTargetKind targetKind,
        string targetKey,
        CancellationToken cancellationToken)
    {
        if (NodeRef.TryParse(targetKey, out var parsed))
        {
            var unversioned = parsed!.Unversioned();
            if (unversioned.Kind is NodeKinds.CollectionInstance
                or NodeKinds.Observation
                or NodeKinds.Enrollment
                or NodeKinds.StudySite
                or NodeKinds.ProtocolVersion
                or NodeKinds.ProtocolElement)
            {
                return unversioned;
            }
        }

        if (targetKind == FindingTargetKind.Observation && Guid.TryParse(targetKey, out var observationGuid))
        {
            return NodeRef.For(ObservationId.FromGuid(observationGuid));
        }

        if (targetKind == FindingTargetKind.DatasetRecord)
        {
            if (Guid.TryParse(targetKey, out var recordGuid))
            {
                var formId = CollectionInstanceId.FromGuid(recordGuid);
                var hasForm = await db.CollectionInstances.AsNoTracking()
                    .AnyAsync(c => c.TenantId == tenant && c.Id == formId, cancellationToken)
                    .ConfigureAwait(false);
                if (hasForm)
                {
                    return NodeRef.For(formId);
                }

                var enrollmentId = EnrollmentId.FromGuid(recordGuid);
                var hasEnrollment = await db.Enrollments.AsNoTracking()
                    .AnyAsync(e => e.TenantId == tenant && e.StudyId == studyId && e.Id == enrollmentId, cancellationToken)
                    .ConfigureAwait(false);
                return hasEnrollment ? NodeRef.For(enrollmentId) : NodeRef.For(formId);
            }

            if (!string.IsNullOrEmpty(targetKey))
            {
                var candidatePrefix = targetKey.Split(':', 2)[0];
                if (SubjectCode.TryFrom(candidatePrefix, out var subjectCode))
                {
                    var matchingEnrollment = await db.Enrollments.AsNoTracking()
                        .Where(e => e.TenantId == tenant && e.StudyId == studyId && e.SubjectCode == subjectCode)
                        .Select(e => (EnrollmentId?)e.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (matchingEnrollment is { } enrollmentId)
                    {
                        return NodeRef.For(enrollmentId);
                    }
                }
            }
        }

        return NodeRef.For(studyId);
    }

    /// <summary>
    /// A discrepancy query (<c>FormQuery</c>, ADR-2625) sits under the enrollment that governs it in lineage, at its
    /// latest recorded lineage version, so enrollment, study-site, and study activity streams include query workflow events.
    /// </summary>
    private static async Task<ScopeNode?> FormQueryAsync(KernelDbContext db, TenantId tenant, Guid queryGuid, CancellationToken cancellationToken)
    {
        var enrollmentGuid = await db.LineageEdges.AsNoTracking()
            .Where(e => e.TenantId == tenant
                && e.ToKind == NodeKinds.FormQuery
                && e.ToId == queryGuid
                && e.FromKind == NodeKinds.Enrollment
                && e.Kind == LineageEdgeKind.GovernedBy)
            .OrderByDescending(e => e.RecordedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => (Guid?)e.FromId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (enrollmentGuid is null)
        {
            return null;
        }

        var latest = await db.LineageNodes.AsNoTracking()
            .Where(n => n.TenantId == tenant && n.Kind == NodeKinds.FormQuery && n.NodeId == queryGuid)
            .MaxAsync(n => (long?)n.Version, cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            return null;
        }

        var id = FormQueryId.FromGuid(queryGuid);
        return new ScopeNode(
            new NodeSummary(NodeRef.For(id, latest.Value), $"Form query {queryGuid:D}", Code: null),
            NodeRef.For(EnrollmentId.FromGuid(enrollmentGuid.Value)));
    }

    /// <summary>A proposal's label: the start of its intent, on one line.</summary>
    private static string ProposalLabel(string intent)
    {
        var line = intent.ReplaceLineEndings(" ").Trim();
        return line.Length <= ProposalLabelLength ? $"Proposal: {line}" : $"Proposal: {line[..(ProposalLabelLength - 1)]}…";
    }

    private static async Task<ScopeNode?> ConceptAsync(
        KernelDbContext db, TenantId tenant, NodeRef tenantRef, NodeRef node, CancellationToken cancellationToken)
    {
        var conceptId = ConceptId.FromGuid(node.Id);
        var concept = await db.Concepts.AsNoTracking()
            .Where(c => c.TenantId == tenant && c.Id == conceptId)
            .Select(c => new { c.PreferredLabel, c.ExternalCode, c.Status, c.Version })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (concept is null)
        {
            return null;
        }

        var label = concept.PreferredLabel;
        long resolvedVersion = concept.Version;
        if (node.Version is { } requestedVersion && requestedVersion != concept.Version && requestedVersion <= int.MaxValue)
        {
            var revisionLabel = await db.ConceptRevisions.AsNoTracking()
                .Where(r => r.TenantId == tenant && r.ConceptId == conceptId && r.Version == (int)requestedVersion)
                .Select(r => r.PreferredLabel)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(revisionLabel))
            {
                label = revisionLabel;
                resolvedVersion = requestedVersion;
            }
        }

        return new ScopeNode(
            new NodeSummary(NodeRef.For(conceptId, resolvedVersion), label, concept.ExternalCode, concept.Status.ToString()),
            tenantRef);
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

    private static async Task<ScopeNode?> CollectionInstanceNodeAsync(
        KernelDbContext db,
        TenantId tenant,
        CollectionInstanceId instanceId,
        CancellationToken cancellationToken)
    {
        var instance = await db.CollectionInstances.AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenant && c.Id == instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
        {
            return null;
        }

        var content = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.Id == instance.ProtocolVersionId)
            .Select(v => v.Content)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var design = content is null ? null : ProtocolDesignDocument.Read(content).Design;
        return new ScopeNode(
            CollectionInstanceSummary(instance, design),
            NodeRef.For(instance.EnrollmentId),
            FallbackParent: NodeRef.For(instance.StudyId));
    }

    private static async Task<ScopeNode?> ObservationNodeAsync(
        KernelDbContext db,
        TenantId tenant,
        ObservationId observationId,
        CancellationToken cancellationToken)
    {
        var observation = await db.Observations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.TenantId == tenant && o.Id == observationId, cancellationToken)
            .ConfigureAwait(false);
        if (observation is null)
        {
            return null;
        }

        var parentInstance = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(
                c => c.TenantId == tenant
                    && c.EnrollmentId == observation.EnrollmentId
                    && c.Members.Any(m => m.ObservationId == observationId),
                cancellationToken)
            .ConfigureAwait(false);

        CollectionItem? item = null;
        string? itemId = null;
        if (parentInstance is not null)
        {
            itemId = parentInstance.Members.FirstOrDefault(m => m.ObservationId == observationId)?.ItemId;
            var content = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.TenantId == tenant && v.Id == parentInstance.ProtocolVersionId)
                .Select(v => v.Content)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var spec = content is null
                ? null
                : ProtocolDesignDocument.Read(content).Design?.CollectionSpecs
                    .FirstOrDefault(s => string.Equals(s.Id, parentInstance.CollectionSpecId, StringComparison.Ordinal));
            item = itemId is null
                ? null
                : spec?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
        }

        var concepts = await ResolveConceptMetadataAsync(db, tenant, [observation], cancellationToken).ConfigureAwait(false);
        var (Label, ExternalCode) = concepts.GetValueOrDefault((observation.ConceptId, observation.ConceptVersion));

        var parentRef = parentInstance is not null
            ? NodeRef.For(parentInstance.Id)
            : NodeRef.For(observation.EnrollmentId);
        return new ScopeNode(
            ObservationSummary(observation, item, itemId, Label, ExternalCode),
            parentRef,
            FallbackParent: NodeRef.For(observation.StudyId));
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

        var versionIds = instances.Select(c => c.ProtocolVersionId).Distinct().ToList();
        var designsByVersion = versionIds.Count == 0
            ? []
            : (await db.ProtocolVersions.AsNoTracking()
                    .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
                    .Select(v => new { v.Id, v.Content })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(
                    v => v.Id,
                    v => ProtocolDesignDocument.Read(v.Content).Design);

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
