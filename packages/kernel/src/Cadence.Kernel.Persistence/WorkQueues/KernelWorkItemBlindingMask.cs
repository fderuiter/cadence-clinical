using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.WorkQueues;

/// <summary>
/// Masks work items whose subject or underlying observation records blinded treatment allocation unless the reader
/// holds <c>enrollment.unblind</c> on the covered enrollment or study (K-AUTH-4, ADR-2471).
/// </summary>
internal sealed class KernelWorkItemBlindingMask(KernelTenantDbContextFactory contexts) : IWorkItemBlindingMask
{
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    public async ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant,
        IAuthorizationProbe probe,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        if (nodes.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        await using var db = contexts.Create(tenant);
        var obsByNode = new Dictionary<NodeRef, List<ObservationId>>();

        var directObsNodes = nodes.Where(n => n.Kind == NodeKinds.Observation).ToList();
        foreach (var node in directObsNodes)
        {
            obsByNode[node] = [ObservationId.FromGuid(node.Id)];
        }

        var formNodes = nodes.Where(n => n.Kind == NodeKinds.CollectionInstance).ToList();
        if (formNodes.Count > 0)
        {
            var formIds = formNodes.Select(n => CollectionInstanceId.FromGuid(n.Id)).Distinct().ToList();
            var formRows = await db.CollectionInstances.AsNoTracking()
                .Where(c => formIds.Contains(c.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var forms = formRows.ToDictionary(c => c.Id.Value, c => c.Members.Select(m => m.ObservationId).ToList());
            foreach (var node in formNodes)
            {
                if (forms.TryGetValue(node.Id, out var memberObs) && memberObs.Count > 0)
                {
                    obsByNode[node] = memberObs;
                }
            }
        }

        var assertionNodes = nodes.Where(n => n.Kind == NodeKinds.Assertion).ToList();
        if (assertionNodes.Count > 0)
        {
            var assertionIds = assertionNodes.Select(n => AssertionId.FromGuid(n.Id)).Distinct().ToList();
            var assertionRows = await db.Assertions.AsNoTracking()
                .Where(a => assertionIds.Contains(a.Id) && a.TargetKind == NodeKinds.Observation)
                .Select(a => new { a.Id, a.TargetId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var assertions = assertionRows.ToDictionary(a => a.Id.Value, a => ObservationId.FromGuid(a.TargetId));
            foreach (var node in assertionNodes)
            {
                if (assertions.TryGetValue(node.Id, out var obsId))
                {
                    obsByNode[node] = [obsId];
                }
            }
        }

        var findingNodes = nodes.Where(n => n.Kind == NodeKinds.ValidationFinding).ToList();
        if (findingNodes.Count > 0)
        {
            var findingIds = findingNodes.Select(n => ValidationFindingId.FromGuid(n.Id)).Distinct().ToList();
            var dets = await db.Set<ValidationDetectionRecord>().AsNoTracking()
                .Where(d => findingIds.Contains(d.FindingId))
                .Select(d => new { d.FindingId, d.Stage, d.TargetKind, d.TargetKey })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var targetForms = new Dictionary<Guid, CollectionInstanceId>();
            foreach (var det in dets.GroupBy(d => d.FindingId).Select(g => g.First()))
            {
                if (det.TargetKind == FindingTargetKind.Observation
                    && NodeRef.TryParse(det.TargetKey, out var obsNode)
                    && obsNode.Kind == NodeKinds.Observation)
                {
                    foreach (var node in findingNodes.Where(n => n.Id == det.FindingId.Value))
                    {
                        obsByNode[node] = [ObservationId.FromGuid(obsNode.Id)];
                    }
                }
                else if (det.Stage == ValidationStage.Collection
                    && det.TargetKind == FindingTargetKind.DatasetRecord
                    && Guid.TryParse(det.TargetKey, out var fg))
                {
                    targetForms[det.FindingId.Value] = CollectionInstanceId.FromGuid(fg);
                }
            }

            if (targetForms.Count > 0)
            {
                var fIds = targetForms.Values.Distinct().ToList();
                var formRows = await db.CollectionInstances.AsNoTracking()
                    .Where(c => fIds.Contains(c.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var forms = formRows.ToDictionary(c => c.Id, c => c.Members.Select(m => m.ObservationId).ToList());
                foreach (var (findingGuid, fId) in targetForms)
                {
                    if (forms.TryGetValue(fId, out var memberObs) && memberObs.Count > 0)
                    {
                        foreach (var node in findingNodes.Where(n => n.Id == findingGuid))
                        {
                            obsByNode[node] = memberObs;
                        }
                    }
                }
            }
        }

        var allObsIds = obsByNode.Values.SelectMany(x => x).Distinct().ToList();
        if (allObsIds.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var observations = await db.Observations.AsNoTracking()
            .Where(o => allObsIds.Contains(o.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (observations.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var obsLookup = observations.ToDictionary(o => o.Id);
        var blindedIds = new HashSet<ObservationId>();
        foreach (var byStudy in observations.GroupBy(o => o.StudyId))
        {
            var studyBlinded = await KernelLockRecords.ResolveBlindedObservationsAsync(db, byStudy.Key, byStudy, cancellationToken)
                .ConfigureAwait(false);
            blindedIds.UnionWith(studyBlinded);
        }

        if (blindedIds.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var unblindCache = new Dictionary<NodeRef, bool>();
        var result = new Dictionary<NodeRef, MaskReason>();
        foreach (var (node, mappedObsIds) in obsByNode)
        {
            var blindedObs = mappedObsIds.Where(blindedIds.Contains).Select(id => obsLookup[id]).ToList();
            if (blindedObs.Count == 0)
            {
                continue;
            }

            var blocked = false;
            foreach (var obs in blindedObs)
            {
                var unblindTarget = NodeRef.For(obs.EnrollmentId);
                if (!unblindCache.TryGetValue(unblindTarget, out var mayUnblind))
                {
                    var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, unblindTarget, cancellationToken)
                        .ConfigureAwait(false);
                    mayUnblind = decision.Permitted;
                    unblindCache[unblindTarget] = mayUnblind;
                }

                if (!mayUnblind)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                result[node] = MaskReason.Blinded;
            }
        }

        return result;
    }
}
