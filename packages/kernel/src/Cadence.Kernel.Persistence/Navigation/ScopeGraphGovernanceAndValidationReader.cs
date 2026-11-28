using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Navigation;

internal sealed record DetectionRow(
    StudyId StudyId,
    string RuleId,
    FindingTargetKind TargetKind,
    string Dataset,
    string TargetKey);

internal static class ScopeGraphGovernanceAndValidationReader
{
    private const int ProposalLabelLength = 80;

    public static string ProposalLabel(string intent)
    {
        var line = intent.ReplaceLineEndings(" ").Trim();
        return line.Length <= ProposalLabelLength ? $"Proposal: {line}" : $"Proposal: {line[..(ProposalLabelLength - 1)]}…";
    }

    public static async Task PopulateGovernanceAndValidationNodesAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        List<NodeRef> distinct,
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
                    parent,
                    FallbackParent: tenantRef);
            }
        }

        var assertionIds = distinct.Where(n => n.Kind == NodeKinds.Assertion).Select(n => AssertionId.FromGuid(n.Id)).ToList();
        if (assertionIds.Count > 0)
        {
            var assertions = await db.Assertions.AsNoTracking()
                .Where(a => a.TenantId == tenant && assertionIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Kind, a.StudyId, a.TargetKind, a.TargetId, a.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var assertion in assertions)
            {
                found[NodeRef.For(assertion.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(assertion.Id, assertion.VersionIndex), $"{assertion.Kind} assertion", Code: null, assertion.Kind.ToString()),
                    new NodeRef(assertion.TargetKind, assertion.TargetId),
                    FallbackParent: NodeRef.For(assertion.StudyId));
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
            foreach (var (avId, scopeNode) in await LoadArtifactVersionsAsync(db, tenant, tenantRef, artifactVersionIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(avId)] = scopeNode;
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
            foreach (var (findingId, scopeNode) in await LoadValidationFindingsAsync(db, tenant, findingIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(findingId)] = scopeNode;
            }
        }
    }

    public static async Task<Dictionary<ArtifactVersionId, ScopeNode>> LoadArtifactVersionsAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        IReadOnlyCollection<ArtifactVersionId> artifactVersionIds,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from v in db.ArtifactVersions.AsNoTracking()
                join a in db.Artifacts.AsNoTracking()
                    on new { v.TenantId, Id = v.ArtifactId } equals new { a.TenantId, a.Id } into artifactGroup
                from a in artifactGroup.DefaultIfEmpty()
                where v.TenantId == tenant && artifactVersionIds.Contains(v.Id)
                select new { v.Id, v.ArtifactId, v.Number, Title = a != null ? a.Title : null, StudyId = a != null ? a.StudyId : null })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<ArtifactVersionId, ScopeNode>(rows.Count);
        foreach (var av in rows)
        {
            var title = av.Title is not null ? $"{av.Title} v{av.Number}" : $"Artifact v{av.Number}";
            var fallback = av.StudyId is { } avStudyId ? NodeRef.For(avStudyId) : tenantRef;
            result[av.Id] = new ScopeNode(
                new NodeSummary(NodeRef.For(av.Id, 1), title, $"v{av.Number}"),
                NodeRef.For(av.ArtifactId),
                FallbackParent: fallback);
        }

        return result;
    }

    public static async Task<Dictionary<ValidationFindingId, ScopeNode>> LoadValidationFindingsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<ValidationFindingId> findingIds,
        CancellationToken cancellationToken)
    {
        var detections = await (
                from d in db.Set<ValidationDetectionRecord>().AsNoTracking()
                join r in db.Set<ValidationRun>().AsNoTracking()
                    on new { d.TenantId, Id = d.RunId } equals new { r.TenantId, r.Id }
                where d.TenantId == tenant && findingIds.Contains(d.FindingId)
                orderby r.VersionIndex descending, r.Id descending
                select new { d.FindingId, d.StudyId, d.RuleId, d.TargetKind, d.Dataset, d.TargetKey })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var detectionsByFinding = new Dictionary<ValidationFindingId, DetectionRow>();
        foreach (var d in detections)
        {
            detectionsByFinding.TryAdd(d.FindingId, new DetectionRow(d.StudyId, d.RuleId, d.TargetKind, d.Dataset, d.TargetKey));
        }

        var decisions = await db.Set<ValidationFinding>().AsNoTracking()
            .Where(f => f.TenantId == tenant && findingIds.Contains(f.Id))
            .Select(f => new { f.Id, f.StudyId, f.Decision, f.VersionIndex })
            .ToDictionaryAsync(f => f.Id, cancellationToken).ConfigureAwait(false);
        var parents = await ResolveValidationFindingParentsAsync(db, tenant, detectionsByFinding, cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<ValidationFindingId, ScopeNode>(findingIds.Count);
        foreach (var findingId in findingIds)
        {
            if (detectionsByFinding.TryGetValue(findingId, out var detection))
            {
                decisions.TryGetValue(findingId, out var decisionRow);
                result[findingId] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(findingId, decisionRow?.VersionIndex),
                        $"Finding {detection.RuleId} on {detection.Dataset} {detection.TargetKey}",
                        detection.RuleId,
                        decisionRow?.Decision?.ToString()),
                    parents[findingId],
                    FallbackParent: NodeRef.For(detection.StudyId));
            }
            else if (decisions.TryGetValue(findingId, out var decisionOnly))
            {
                result[findingId] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(findingId, decisionOnly.VersionIndex),
                        "Validation finding",
                        Code: null,
                        decisionOnly.Decision?.ToString()),
                    NodeRef.For(decisionOnly.StudyId));
            }
        }

        return result;
    }

    private static async Task<Dictionary<ValidationFindingId, NodeRef>> ResolveValidationFindingParentsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyDictionary<ValidationFindingId, DetectionRow> detectionsByFinding,
        CancellationToken cancellationToken)
    {
        var parents = new Dictionary<ValidationFindingId, NodeRef>(detectionsByFinding.Count);
        if (detectionsByFinding.Count == 0)
        {
            return parents;
        }

        var datasetGuids = new List<(ValidationFindingId FindingId, StudyId StudyId, Guid RecordGuid)>();
        var subjectCodeLookups = new List<(ValidationFindingId FindingId, StudyId StudyId, SubjectCode SubjectCode)>();

        foreach (var (findingId, det) in detectionsByFinding)
        {
            if (NodeRef.TryParse(det.TargetKey, out var parsed)
                && parsed!.Unversioned() is
                {
                    Kind: NodeKinds.CollectionInstance
                    or NodeKinds.Observation
                    or NodeKinds.Enrollment
                    or NodeKinds.StudySite
                    or NodeKinds.ProtocolVersion
                    or NodeKinds.ProtocolElement
                } unversioned)
            {
                parents[findingId] = unversioned;
                continue;
            }

            if (det.TargetKind == FindingTargetKind.Observation && Guid.TryParse(det.TargetKey, out var observationGuid))
            {
                parents[findingId] = NodeRef.For(ObservationId.FromGuid(observationGuid));
                continue;
            }

            if (det.TargetKind == FindingTargetKind.DatasetRecord)
            {
                if (Guid.TryParse(det.TargetKey, out var recordGuid))
                {
                    datasetGuids.Add((findingId, det.StudyId, recordGuid));
                    continue;
                }

                if (!string.IsNullOrEmpty(det.TargetKey)
                    && SubjectCode.TryFrom(det.TargetKey.Split(':', 2)[0], out var subjectCode))
                {
                    subjectCodeLookups.Add((findingId, det.StudyId, subjectCode));
                    continue;
                }
            }

            parents[findingId] = NodeRef.For(det.StudyId);
        }

        if (datasetGuids.Count > 0)
        {
            var candidateFormIds = datasetGuids.Select(c => CollectionInstanceId.FromGuid(c.RecordGuid)).Distinct().ToList();
            var existingForms = (await db.CollectionInstances.AsNoTracking()
                    .Where(c => c.TenantId == tenant && candidateFormIds.Contains(c.Id))
                    .Select(c => c.Id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet();

            var remainingForEnrollment = new List<(ValidationFindingId FindingId, StudyId StudyId, Guid RecordGuid)>();
            foreach (var candidate in datasetGuids)
            {
                var formId = CollectionInstanceId.FromGuid(candidate.RecordGuid);
                if (existingForms.Contains(formId))
                {
                    parents[candidate.FindingId] = NodeRef.For(formId);
                }
                else
                {
                    remainingForEnrollment.Add(candidate);
                }
            }

            if (remainingForEnrollment.Count > 0)
            {
                var candidateEnrollmentIds = remainingForEnrollment.Select(c => EnrollmentId.FromGuid(c.RecordGuid)).Distinct().ToList();
                var existingEnrollments = (await db.Enrollments.AsNoTracking()
                        .Where(e => e.TenantId == tenant && candidateEnrollmentIds.Contains(e.Id))
                        .Select(e => new { e.StudyId, e.Id })
                        .ToListAsync(cancellationToken).ConfigureAwait(false))
                    .Select(e => (e.StudyId, e.Id))
                    .ToHashSet();
                foreach (var candidate in remainingForEnrollment)
                {
                    var enrollmentId = EnrollmentId.FromGuid(candidate.RecordGuid);
                    parents[candidate.FindingId] = existingEnrollments.Contains((candidate.StudyId, enrollmentId))
                        ? NodeRef.For(enrollmentId)
                        : NodeRef.For(CollectionInstanceId.FromGuid(candidate.RecordGuid));
                }
            }
        }

        if (subjectCodeLookups.Count > 0)
        {
            var studyIds = subjectCodeLookups.Select(c => c.StudyId).Distinct().ToList();
            var codes = subjectCodeLookups.Select(c => c.SubjectCode).Distinct().ToList();
            var matchedEnrollments = await db.Enrollments.AsNoTracking()
                .Where(e => e.TenantId == tenant && studyIds.Contains(e.StudyId) && codes.Contains(e.SubjectCode))
                .Select(e => new { e.StudyId, e.SubjectCode, e.Id })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var enrollmentByStudyAndCode = new Dictionary<(StudyId, SubjectCode), EnrollmentId>();
            foreach (var m in matchedEnrollments)
            {
                enrollmentByStudyAndCode.TryAdd((m.StudyId, m.SubjectCode), m.Id);
            }

            foreach (var candidate in subjectCodeLookups)
            {
                parents[candidate.FindingId] = enrollmentByStudyAndCode.TryGetValue((candidate.StudyId, candidate.SubjectCode), out var enrollmentId)
                    ? NodeRef.For(enrollmentId)
                    : NodeRef.For(candidate.StudyId);
            }
        }

        return parents;
    }
}
