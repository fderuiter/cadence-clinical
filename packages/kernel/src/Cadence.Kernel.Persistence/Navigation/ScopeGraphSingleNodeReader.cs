using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Navigation;

internal static class ScopeGraphSingleNodeReader
{
    public static async Task<ScopeNode?> FindNodeAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef node,
        CancellationToken cancellationToken)
    {
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
                var studySite = await ScopeGraphCoreAndProtocolBatchReader.StudySitesWithSites(db, tenant)
                    .Where(x => x.Id == studySiteId)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return studySite is null
                    ? null
                    : new ScopeNode(ScopeGraphCoreAndProtocolBatchReader.StudySiteSummary(studySite), NodeRef.For(studySite.StudyId), studySite.Country, FallbackParent: tenantRef);

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
                var enrollmentId = EnrollmentId.FromGuid(node.Id);
                var enrollment = await db.Enrollments.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.Id == enrollmentId)
                    .Select(e => new { e.SubjectCode, e.StudyId, e.StudySiteId, e.Status, e.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return enrollment is null
                    ? null
                    : new ScopeNode(
                        ScopeGraphCoreAndProtocolBatchReader.EnrollmentSummary(enrollmentId, enrollment.SubjectCode, enrollment.Status, enrollment.VersionIndex),
                        ZoomRules.EnrollmentParent(enrollment.StudyId, enrollment.StudySiteId),
                        FallbackParent: NodeRef.For(enrollment.StudyId));

            case NodeKinds.CollectionInstance:
                var singleInstanceId = CollectionInstanceId.FromGuid(node.Id);
                return (await ScopeGraphObservationAndChildrenReader.LoadCollectionInstancesAsync(db, tenant, [singleInstanceId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(singleInstanceId);

            case NodeKinds.Observation:
                var singleObservationId = ObservationId.FromGuid(node.Id);
                return (await ScopeGraphObservationAndChildrenReader.LoadObservationsAsync(db, tenant, [singleObservationId], cancellationToken).ConfigureAwait(false))
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
                return (await ScopeGraphCoreAndProtocolBatchReader.LoadProtocolElementsAsync(db, tenant, [node.Id], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(node.Id);

            case NodeKinds.FormQuery:
                return (await ScopeGraphCoreAndProtocolBatchReader.LoadFormQueriesAsync(db, tenant, [node.Id], cancellationToken).ConfigureAwait(false))
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
                return (await ScopeGraphGovernanceAndValidationReader.LoadArtifactVersionsAsync(db, tenant, tenantRef, [artifactVersionId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(artifactVersionId);

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
                return (await ScopeGraphAuthorityAndTerminologyReader.LoadGrantsAsync(db, tenant, [grantId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(grantId);

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
                return (await ScopeGraphAuthorityAndTerminologyReader.LoadQualificationRequirementsAsync(db, tenant, [requirementId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(requirementId);

            case NodeKinds.Effectivity:
                var effectivityId = EffectivityId.FromGuid(node.Id);
                return (await ScopeGraphAuthorityAndTerminologyReader.LoadEffectivitiesAsync(db, tenant, [effectivityId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(effectivityId);

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
                return (await ScopeGraphAuthorityAndTerminologyReader.LoadCodeSystemVersionsAsync(db, tenant, tenantRef, [codeSystemVersionId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(codeSystemVersionId);

            case NodeKinds.Concept:
                return await ScopeGraphConceptReader.ConceptAsync(db, tenant, tenantRef, node, cancellationToken).ConfigureAwait(false);

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
                var runId = ValidationRunId.FromGuid(node.Id);
                var run = await db.Set<ValidationRun>().AsNoTracking()
                    .Where(r => r.TenantId == tenant && r.Id == runId)
                    .Select(r => new { r.StudyId, r.Stage, r.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return run is null
                    ? null
                    : new ScopeNode(new NodeSummary(NodeRef.For(runId, run.VersionIndex), $"{run.Stage} validation run", Code: null), NodeRef.For(run.StudyId));

            case NodeKinds.Proposal:
                var proposalId = ProposalId.FromGuid(node.Id);
                var proposal = await db.Proposals.AsNoTracking()
                    .Where(p => p.TenantId == tenant && p.Id == proposalId)
                    .Select(p => new { p.StudyId, p.Intent, p.Status, p.VersionIndex })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return proposal is null
                    ? null
                    : new ScopeNode(
                        new NodeSummary(
                            NodeRef.For(proposalId, proposal.VersionIndex),
                            ScopeGraphGovernanceAndValidationReader.ProposalLabel(proposal.Intent),
                            Code: null,
                            proposal.Status.ToString()),
                        NodeRef.For(proposal.StudyId));

            case NodeKinds.ValidationFinding:
                var findingId = ValidationFindingId.FromGuid(node.Id);
                return (await ScopeGraphGovernanceAndValidationReader.LoadValidationFindingsAsync(db, tenant, [findingId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(findingId);

            case NodeKinds.Subscription:
                var subscriptionId = SubscriptionId.FromGuid(node.Id);
                return (await ScopeGraphAuthorityAndTerminologyReader.LoadSubscriptionsAsync(db, tenant, tenantRef, [subscriptionId], cancellationToken).ConfigureAwait(false))
                    .GetValueOrDefault(subscriptionId);

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
}
