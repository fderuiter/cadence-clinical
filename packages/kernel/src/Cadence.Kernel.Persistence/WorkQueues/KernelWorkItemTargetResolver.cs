using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.WorkQueues;

/// <summary>Resolves study, enrollment, form, observation, finding, and protocol navigation context for unmasked work items.</summary>
internal sealed class KernelWorkItemTargetResolver(KernelTenantDbContextFactory contexts) : IWorkItemTargetResolver
{
    public async Task<IReadOnlyDictionary<NodeRef, WorkItemTargetContext>> ResolveAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> subjects,
        CancellationToken cancellationToken)
    {
        if (subjects.Count == 0)
        {
            return new Dictionary<NodeRef, WorkItemTargetContext>();
        }

        await using var db = contexts.Create(tenant);
        var result = new Dictionary<NodeRef, WorkItemTargetContext>();

        var protocolNodes = subjects.Where(s => s.Kind == NodeKinds.ProtocolVersion).ToList();
        if (protocolNodes.Count > 0)
        {
            var pvIds = protocolNodes.Select(s => ProtocolVersionId.FromGuid(s.Id)).Distinct().ToList();
            var pvRows = await db.ProtocolVersions.AsNoTracking()
                .Where(p => pvIds.Contains(p.Id))
                .Select(p => new { p.Id, p.StudyId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var pvs = pvRows.ToDictionary(p => p.Id.Value, p => p.StudyId);
            foreach (var node in protocolNodes)
            {
                if (pvs.TryGetValue(node.Id, out var studyId))
                {
                    result[node] = new WorkItemTargetContext(
                        StudyRef: NodeRef.For(studyId).ToString(),
                        StudyId: studyId.Value,
                        ProtocolVersionRef: node.ToString());
                }
            }
        }

        var proposalNodes = subjects.Where(s => s.Kind == NodeKinds.Proposal).ToList();
        if (proposalNodes.Count > 0)
        {
            var propIds = proposalNodes.Select(s => ProposalId.FromGuid(s.Id)).Distinct().ToList();
            var proposalRows = await db.Proposals.AsNoTracking()
                .Where(p => propIds.Contains(p.Id))
                .Select(p => new { p.Id, p.StudyId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var proposals = proposalRows.ToDictionary(p => p.Id.Value, p => p.StudyId);
            foreach (var node in proposalNodes)
            {
                if (proposals.TryGetValue(node.Id, out var studyId))
                {
                    result[node] = new WorkItemTargetContext(
                        StudyRef: NodeRef.For(studyId).ToString(),
                        StudyId: studyId.Value,
                        ProposalRef: node.ToString());
                }
            }
        }

        var formNodes = subjects.Where(s => s.Kind == NodeKinds.CollectionInstance).ToList();
        if (formNodes.Count > 0)
        {
            var formIds = formNodes.Select(s => CollectionInstanceId.FromGuid(s.Id)).Distinct().ToList();
            var formRows = await db.CollectionInstances.AsNoTracking()
                .Where(c => formIds.Contains(c.Id))
                .Select(c => new { c.Id, c.StudyId, c.EnrollmentId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var forms = formRows.ToDictionary(c => c.Id.Value, c => (c.StudyId, c.EnrollmentId));
            foreach (var node in formNodes)
            {
                if (forms.TryGetValue(node.Id, out var f))
                {
                    result[node] = new WorkItemTargetContext(
                        StudyRef: NodeRef.For(f.StudyId).ToString(),
                        StudyId: f.StudyId.Value,
                        EnrollmentId: f.EnrollmentId.Value,
                        FormId: node.Id);
                }
            }
        }

        var obsNodes = subjects.Where(s => s.Kind == NodeKinds.Observation).ToList();
        var assertionNodes = subjects.Where(s => s.Kind == NodeKinds.Assertion).ToList();
        var findingNodes = subjects.Where(s => s.Kind == NodeKinds.ValidationFinding).ToList();

        var assertionTargets = new Dictionary<Guid, (StudyId StudyId, EnrollmentId? EnrollmentId, ObservationId? ObservationId)>();
        if (assertionNodes.Count > 0)
        {
            var aIds = assertionNodes.Select(s => AssertionId.FromGuid(s.Id)).Distinct().ToList();
            var rows = await db.Assertions.AsNoTracking()
                .Where(a => aIds.Contains(a.Id))
                .Select(a => new { a.Id, a.StudyId, a.EnrollmentId, a.TargetKind, a.TargetId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
            {
                var obsId = row.TargetKind == NodeKinds.Observation ? ObservationId.FromGuid(row.TargetId) : (ObservationId?)null;
                assertionTargets[row.Id.Value] = (row.StudyId, row.EnrollmentId, obsId);
            }
        }

        var findingTargets = new Dictionary<Guid, (StudyId StudyId, CollectionInstanceId? FormId, ObservationId? ObservationId)>();
        if (findingNodes.Count > 0)
        {
            var fIds = findingNodes.Select(s => ValidationFindingId.FromGuid(s.Id)).Distinct().ToList();
            var dets = await db.Set<ValidationDetectionRecord>().AsNoTracking()
                .Where(d => fIds.Contains(d.FindingId))
                .Select(d => new { d.FindingId, d.StudyId, d.Stage, d.TargetKind, d.TargetKey })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var det in dets.GroupBy(d => d.FindingId).Select(g => g.First()))
            {
                CollectionInstanceId? fId = det.Stage == ValidationStage.Collection
                    && det.TargetKind == FindingTargetKind.DatasetRecord
                    && Guid.TryParse(det.TargetKey, out var fg)
                    ? CollectionInstanceId.FromGuid(fg)
                    : null;
                ObservationId? oId = det.TargetKind == FindingTargetKind.Observation
                    && NodeRef.TryParse(det.TargetKey, out var on)
                    && on.Kind == NodeKinds.Observation
                    ? ObservationId.FromGuid(on.Id)
                    : null;
                findingTargets[det.FindingId.Value] = (det.StudyId, fId, oId);
            }
        }

        var referencedObsIds = obsNodes.Select(s => ObservationId.FromGuid(s.Id))
            .Concat(assertionTargets.Values.Where(v => v.ObservationId is not null).Select(v => v.ObservationId!.Value))
            .Concat(findingTargets.Values.Where(v => v.ObservationId is not null).Select(v => v.ObservationId!.Value))
            .Distinct()
            .ToList();

        Dictionary<ObservationId, (StudyId StudyId, EnrollmentId EnrollmentId, long VersionIndex)> obsDetails;
        if (referencedObsIds.Count == 0)
        {
            obsDetails = new Dictionary<ObservationId, (StudyId StudyId, EnrollmentId EnrollmentId, long VersionIndex)>();
        }
        else
        {
            var obsRows = await db.Observations.AsNoTracking()
                .Where(o => referencedObsIds.Contains(o.Id))
                .Select(o => new { o.Id, o.StudyId, o.EnrollmentId, o.VersionIndex })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            obsDetails = obsRows.ToDictionary(o => o.Id, o => (o.StudyId, o.EnrollmentId, o.VersionIndex));
        }

        var referencedFormIds = findingTargets.Values
            .Where(v => v.FormId is not null)
            .Select(v => v.FormId!.Value)
            .Distinct()
            .ToList();

        var memberForms = (referencedObsIds.Count == 0 && referencedFormIds.Count == 0)
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Where(c => referencedFormIds.Contains(c.Id) || c.Members.Any(m => referencedObsIds.Contains(m.ObservationId)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var formByObs = new Dictionary<ObservationId, CollectionInstance>();
        var formById = new Dictionary<CollectionInstanceId, CollectionInstance>();
        foreach (var inst in memberForms)
        {
            formById[inst.Id] = inst;
            foreach (var member in inst.Members)
            {
                formByObs[member.ObservationId] = inst;
            }
        }

        foreach (var node in obsNodes)
        {
            var obsId = ObservationId.FromGuid(node.Id);
            if (obsDetails.TryGetValue(obsId, out var detail))
            {
                formByObs.TryGetValue(obsId, out var inst);
                result[node] = new WorkItemTargetContext(
                    StudyRef: NodeRef.For(detail.StudyId).ToString(),
                    StudyId: detail.StudyId.Value,
                    EnrollmentId: detail.EnrollmentId.Value,
                    FormId: inst?.Id.Value,
                    ObservationRef: node.ToString());
            }
        }

        foreach (var node in assertionNodes)
        {
            if (assertionTargets.TryGetValue(node.Id, out var target))
            {
                CollectionInstance? inst = null;
                string? obsRef = null;
                var enrollmentId = target.EnrollmentId;
                if (target.ObservationId is { } oId)
                {
                    formByObs.TryGetValue(oId, out inst);
                    if (obsDetails.TryGetValue(oId, out var od))
                    {
                        enrollmentId ??= od.EnrollmentId;
                        obsRef = NodeRef.For(oId, od.VersionIndex).ToString();
                    }
                    else
                    {
                        obsRef = NodeRef.For(oId).ToString();
                    }
                }

                result[node] = new WorkItemTargetContext(
                    StudyRef: NodeRef.For(target.StudyId).ToString(),
                    StudyId: target.StudyId.Value,
                    EnrollmentId: enrollmentId?.Value ?? inst?.EnrollmentId.Value,
                    FormId: inst?.Id.Value,
                    ObservationRef: obsRef);
            }
        }

        foreach (var node in findingNodes)
        {
            if (findingTargets.TryGetValue(node.Id, out var target))
            {
                CollectionInstance? inst = null;
                Guid? enrollmentGuid = null;
                string? obsRef = null;
                if (target.FormId is { } fId && formById.TryGetValue(fId, out inst))
                {
                    enrollmentGuid = inst.EnrollmentId.Value;
                }
                else if (target.ObservationId is { } oId)
                {
                    formByObs.TryGetValue(oId, out inst);
                    if (obsDetails.TryGetValue(oId, out var od))
                    {
                        enrollmentGuid = od.EnrollmentId.Value;
                        obsRef = NodeRef.For(oId, od.VersionIndex).ToString();
                    }
                }

                result[node] = new WorkItemTargetContext(
                    StudyRef: NodeRef.For(target.StudyId).ToString(),
                    StudyId: target.StudyId.Value,
                    EnrollmentId: enrollmentGuid ?? inst?.EnrollmentId.Value,
                    FormId: inst?.Id.Value,
                    ObservationRef: obsRef,
                    FindingRef: node.Unversioned().ToString());
            }
        }

        return result;
    }
}
