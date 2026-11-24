using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Audit;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Audit;

/// <summary>
/// Reads object history timelines and single audit events on demand directly from <c>kernel.audit_entries</c>,
/// <c>kernel.provenance_activities</c>, <c>kernel.attestations</c>, <c>kernel.lineage_nodes</c>,
/// <c>kernel.observation_revisions</c>, <c>kernel.assertions</c>, and <c>kernel.validation_detections</c> without new
/// audit write projections, enforcing <c>K-AUTH-4</c> blinding and scope isolation via <see cref="INodeVisibility"/>.
/// </summary>
internal sealed class KernelHistoryReader(
    KernelTenantDbContextFactory contexts,
    IKernelScopeGraph scopeGraph,
    INodeVisibility visibility) : IKernelHistoryReader
{
    public const int MaxHistoryLimit = 500;

    private static readonly HashSet<string> IgnoredDiffKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "TenantId",
        "tenant_id",
        "CreatedAt",
        "created_at",
        "CreatedBy",
        "created_by",
        "UpdatedAt",
        "updated_at",
        "UpdatedBy",
        "updated_by",
        "ReasonForChange",
        "reason_for_change",
    };

    public async Task<KernelNodeHistory?> ReadNodeHistoryAsync(
        TenantId tenant,
        Actor? reader,
        NodeRef node,
        NodeHistoryWindow window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(window);

        var limit = Math.Clamp(window.Limit, 1, MaxHistoryLimit);
        var unversionedNode = node.Unversioned();

        await using var db = contexts.Create(tenant);

        var scopeNode = await scopeGraph.FindAsync(tenant, unversionedNode, cancellationToken).ConfigureAwait(false);
        var rawCandidates = await CollectHistoryCandidatesAsync(db, tenant, node, cancellationToken).ConfigureAwait(false);

        if (scopeNode is null && rawCandidates.Count == 0)
        {
            var existsWithoutAudit = await ExistsInDomainTableAsync(db, tenant, unversionedNode, cancellationToken)
                .ConfigureAwait(false);
            if (!existsWithoutAudit)
            {
                return null;
            }
        }

        var filtered = FilterAndOrderCandidates(rawCandidates, window, limit);
        var personIds = CollectCandidatePersonIds(filtered.Select(c => c.ActorId));
        var knownPeople = await ResolveKnownPeopleAsync(db, tenant, personIds, cancellationToken).ConfigureAwait(false);

        var unmaskedEntries = filtered
            .Select(c => MaterializeEntry(c, knownPeople))
            .ToList();

        var masks = await ComputeVisibilityMasksAsync(tenant, reader, unversionedNode, unmaskedEntries, cancellationToken)
            .ConfigureAwait(false);
        MaskReason? nodeMask = masks.TryGetValue(unversionedNode, out var foundNodeMask) ? foundNodeMask : null;

        var finalEntries = new List<KernelHistoryEntry>(unmaskedEntries.Count);
        for (var i = 0; i < unmaskedEntries.Count; i++)
        {
            var entry = unmaskedEntries[i];
            var entryMask = ResolveEntryMask(entry, nodeMask, masks);
            finalEntries.Add(entryMask is { } reason ? MaskHistoryEntry(entry, reason, i + 1) : entry);
        }

        return new KernelNodeHistory(
            nodeMask is null ? (scopeNode?.Summary.Ref ?? node) : unversionedNode,
            nodeMask,
            window.ValidAt,
            window.KnownAt,
            finalEntries);
    }

    public async Task<KernelAuditEntryDetail?> FindAuditEntryAsync(
        TenantId tenant,
        Actor? reader,
        AuditEntryId auditEntryId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var record = await db.AuditEntries.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenant && a.Id == auditEntryId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        ProvenanceActivityRecord? provenance = null;
        if (record.ActivityId != Guid.Empty)
        {
            provenance = await db.ProvenanceActivities.AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == tenant && p.Id == record.ActivityId, cancellationToken)
                .ConfigureAwait(false);
        }

        var targetRef = await ResolveAuditTargetRefAsync(db, tenant, record, cancellationToken).ConfigureAwait(false);
        var targetKind = targetRef?.Kind
            ?? KernelAggregateKinds.TryMapAggregateTypeToKind(record.AggregateType)
            ?? NodeKinds.AuditEntry;

        var attestation = await FindAttestationForAuditEntryAsync(db, tenant, record, targetRef, cancellationToken)
            .ConfigureAwait(false);

        var fieldChanges = ParseChangesJson(record.Changes);
        DateTimeOffset? validAt = attestation?.AuthenticatedAt;
        if (targetRef?.Kind == NodeKinds.Observation && record.AggregateVersion is { } obsVersion)
        {
            var obsId = ObservationId.FromGuid(targetRef.Id);
            var obsObservedAt = await db.Observations.AsNoTracking()
                .Where(o => o.TenantId == tenant && o.Id == obsId)
                .Select(o => (DateTimeOffset?)o.ObservedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var obsRev = await db.ObservationRevisions.AsNoTracking()
                .Where(r => r.TenantId == tenant
                    && r.ObservationId == obsId
                    && (r.Number == (int)obsVersion || r.Number == (int)obsVersion - 1))
                .OrderBy(r => r.Number)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var currentRev = obsRev.FirstOrDefault(r => r.Number == (int)obsVersion);
            if (currentRev is not null)
            {
                validAt = obsObservedAt ?? currentRev.RecordedAt;
                var prevRev = obsRev.FirstOrDefault(r => r.Number == (int)obsVersion - 1);
                fieldChanges = BuildObservationFieldChanges(prevRev, currentRev, fieldChanges);
            }
        }

        var personIds = CollectCandidatePersonIds([record.ActorId, attestation?.ActorId]);
        var knownPeople = await ResolveKnownPeopleAsync(db, tenant, personIds, cancellationToken).ConfigureAwait(false);

        var auditNode = NodeRef.For(auditEntryId);
        var checkNodes = new List<NodeRef> { auditNode };
        if (targetRef is not null)
        {
            checkNodes.Add(targetRef.Unversioned());
        }

        if (attestation is not null)
        {
            checkNodes.Add(attestation.AttestationRef.Unversioned());
        }

        IReadOnlyDictionary<NodeRef, MaskReason> masks;
        if (reader is null)
        {
            masks = checkNodes.Distinct().ToDictionary(n => n, _ => MaskReason.NotPermitted);
        }
        else
        {
            masks = await visibility.MaskAsync(tenant, reader, checkNodes.Distinct().ToList(), cancellationToken)
                .ConfigureAwait(false);
        }

        MaskReason? mask = masks.TryGetValue(auditNode, out var directAuditMask) ? directAuditMask : null;
        if (mask is null && targetRef is not null && masks.TryGetValue(targetRef.Unversioned(), out var targetMask))
        {
            mask = targetMask;
        }
        else if (mask is null && attestation is not null && masks.TryGetValue(attestation.AttestationRef.Unversioned(), out var attMask))
        {
            mask = attMask;
        }

        if (mask is { } reason)
        {
            return new KernelAuditEntryDetail(
                AuditEntryRef: null,
                TargetKind: targetKind,
                TargetRef: null,
                Action: "Restricted",
                CommandType: null,
                Outcome: null,
                RecordedAt: record.RecordedAt,
                ValidAt: null,
                ActorId: null,
                ActorKind: null,
                ActorRef: null,
                Version: null,
                ReasonForChange: null,
                Summary: null,
                FieldChanges: [],
                Attestation: null,
                ActivityId: null,
                CorrelationId: null,
                Sequence: null,
                Hash: null,
                PredecessorHash: null,
                Mask: reason);
        }

        var reasonText = FirstNonBlank(record.ReasonForChange, provenance?.ReasonForChange, attestation?.Reason);
        var kindLabel = KernelAggregateKinds.HumanizeKind(targetKind, record.AggregateType);
        var summary = DescribeActionSummary(record.Action, provenance?.CommandType, kindLabel, record.AggregateVersion);

        KernelHistoryAttestation? mappedAttestation = null;
        if (attestation is not null)
        {
            mappedAttestation = attestation with
            {
                ActorRef = ResolveActorNodeRef(attestation.ActorId, knownPeople),
            };
        }

        NodeRef? versionedTargetRef = targetRef;
        if (targetRef is not null && record.AggregateVersion is { } ver)
        {
            versionedTargetRef = new NodeRef(targetRef.Kind, targetRef.Id, ver);
        }

        return new KernelAuditEntryDetail(
            AuditEntryRef: auditNode,
            TargetKind: targetKind,
            TargetRef: versionedTargetRef,
            Action: record.Action,
            CommandType: provenance?.CommandType,
            Outcome: provenance?.Outcome ?? "Committed",
            RecordedAt: record.RecordedAt,
            ValidAt: validAt,
            ActorId: record.ActorId,
            ActorKind: provenance?.ActorKind.ToString(),
            ActorRef: ResolveActorNodeRef(record.ActorId, knownPeople),
            Version: record.AggregateVersion,
            ReasonForChange: reasonText,
            Summary: summary,
            FieldChanges: fieldChanges,
            Attestation: mappedAttestation,
            ActivityId: record.ActivityId == Guid.Empty ? null : record.ActivityId,
            CorrelationId: provenance?.CorrelationId,
            Sequence: record.Sequence,
            Hash: record.Hash,
            PredecessorHash: record.PredecessorHash,
            Mask: null);
    }

    private static async Task<List<RawHistoryCandidate>> CollectHistoryCandidatesAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef node,
        CancellationToken ct)
    {
        var unversioned = node.Unversioned();
        if (node.Kind == NodeKinds.AuditEntry)
        {
            var single = await db.AuditEntries.AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == tenant && a.Id == node.Id, ct)
                .ConfigureAwait(false);
            if (single is null)
            {
                return [];
            }

            return await BuildAuditCandidatesAsync(db, tenant, node, [single], [], [], ct).ConfigureAwait(false);
        }

        var idText = node.Id.ToString();
        var aggregateTypes = KernelAggregateKinds.AggregateTypesForKind(node.Kind);

        var lineageNodes = await db.LineageNodes.AsNoTracking()
            .Where(l => l.TenantId == tenant
                && l.Kind == node.Kind
                && l.NodeId == node.Id
                && (node.Version == null || l.Version <= node.Version.Value))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var lineageActivityIds = lineageNodes.Select(l => l.ActivityId).Distinct().ToList();

        var relatedAssertions = node.Kind == NodeKinds.Assertion
            ? await db.Assertions.AsNoTracking()
                .Where(a => a.TenantId == tenant
                    && (a.Id == AssertionId.FromGuid(node.Id) || a.RetractsId == AssertionId.FromGuid(node.Id)))
                .ToListAsync(ct)
                .ConfigureAwait(false)
            : await db.Assertions.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.TargetKind == node.Kind && a.TargetId == node.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

        if (relatedAssertions.Count > 0 && node.Kind != NodeKinds.Assertion)
        {
            var directAssertionIds = relatedAssertions.Select(a => (AssertionId?)a.Id).ToList();
            var retractions = await db.Assertions.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.RetractsId != null && directAssertionIds.Contains(a.RetractsId))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var r in retractions)
            {
                if (relatedAssertions.All(existing => existing.Id != r.Id))
                {
                    relatedAssertions.Add(r);
                }
            }
        }

        var assertionIdStrings = relatedAssertions.Select(a => a.Id.ToString()).Distinct().ToList();

        var directAudits = aggregateTypes.Length == 0
            ? []
            : await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == tenant
                    && a.AggregateId == idText
                    && aggregateTypes.Contains(a.AggregateType)
                    && (node.Version == null || a.AggregateVersion == null || a.AggregateVersion <= node.Version.Value))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var lineageAudits = lineageActivityIds.Count == 0
            ? []
            : await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == tenant
                    && a.ActivityId != Guid.Empty
                    && lineageActivityIds.Contains(a.ActivityId))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var assertionAudits = assertionIdStrings.Count == 0
            ? []
            : await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == tenant
                    && a.AggregateType == "Assertion"
                    && a.AggregateId != null
                    && assertionIdStrings.Contains(a.AggregateId))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var mergedAudits = directAudits
            .Concat(lineageAudits.Where(a => KernelAggregateKinds.TryMapAggregateTypeToKind(a.AggregateType) != NodeKinds.Attestation))
            .Concat(assertionAudits)
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.Sequence)
            .ToList();

        var directAttestations = await db.Attestations.AsNoTracking()
            .Where(a => a.TenantId == tenant
                && ((a.TargetKind == node.Kind && a.TargetId == node.Id)
                    || (node.Kind == NodeKinds.Attestation && a.Id == AttestationId.FromGuid(node.Id))))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var candidates = await BuildAuditCandidatesAsync(
                db,
                tenant,
                unversioned,
                mergedAudits,
                relatedAssertions,
                directAttestations,
                ct)
            .ConfigureAwait(false);

        if (node.Kind == NodeKinds.ValidationFinding)
        {
            var findingId = ValidationFindingId.FromGuid(node.Id);
            var detections = await (
                    from d in db.Set<ValidationDetectionRecord>().AsNoTracking()
                    join r in db.Set<ValidationRun>().AsNoTracking()
                        on new { d.TenantId, Id = d.RunId } equals new { r.TenantId, r.Id }
                    where d.TenantId == tenant && d.FindingId == findingId
                    orderby r.CompletedAt
                    select new
                    {
                        d.RunId,
                        d.RuleId,
                        d.Dataset,
                        d.Variable,
                        d.TargetKey,
                        d.Message,
                        r.Stage,
                        r.StartedAt,
                        r.CompletedAt,
                    })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var runIdStrings = detections.Select(d => d.RunId.ToString()).Distinct().ToList();
            var runAudits = runIdStrings.Count == 0
                ? new Dictionary<string, AuditEntryRecord>(StringComparer.OrdinalIgnoreCase)
                : (await db.AuditEntries.AsNoTracking()
                        .Where(a => a.TenantId == tenant
                            && a.AggregateType == "ValidationRun"
                            && a.AggregateId != null
                            && runIdStrings.Contains(a.AggregateId))
                        .ToListAsync(ct)
                        .ConfigureAwait(false))
                    .GroupBy(a => a.AggregateId!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Sequence).First(), StringComparer.OrdinalIgnoreCase);

            foreach (var det in detections)
            {
                runAudits.TryGetValue(det.RunId.ToString(), out var runAudit);
                var recordedAt = runAudit?.RecordedAt ?? det.CompletedAt;
                candidates.Add(new RawHistoryCandidate(
                    EntryId: runAudit is not null
                        ? NodeRef.For(AuditEntryId.FromGuid(runAudit.Id)).ToString()
                        : $"detection:{det.RunId}:{node.Id}",
                    AuditEntryRef: runAudit is not null ? NodeRef.For(AuditEntryId.FromGuid(runAudit.Id)) : null,
                    EventKind: "Detection",
                    Action: "Detected",
                    CommandType: "validation.run",
                    Outcome: "Committed",
                    RecordedAt: recordedAt,
                    ValidAt: det.CompletedAt,
                    ActorId: runAudit?.ActorId ?? "system",
                    ActorKind: null,
                    TargetKind: NodeKinds.ValidationFinding,
                    TargetRef: unversioned,
                    Version: null,
                    ReasonForChange: runAudit?.ReasonForChange,
                    Summary: $"Detected {det.RuleId} during {det.Stage} validation on {det.Dataset} ({det.TargetKey})",
                    FieldChanges: det.Variable is null ? [] : [new KernelHistoryFieldChange("variable", null, det.Variable)],
                    Attestation: null,
                    Sequence: runAudit?.Sequence,
                    Hash: runAudit?.Hash,
                    PredecessorHash: runAudit?.PredecessorHash));
            }
        }

        return candidates;
    }

    private static async Task<List<RawHistoryCandidate>> BuildAuditCandidatesAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef queriedNode,
        List<AuditEntryRecord> auditRecords,
        List<Assertion> relatedAssertions,
        List<Attestation> directAttestations,
        CancellationToken ct)
    {
        var activityIds = auditRecords
            .Where(a => a.ActivityId != Guid.Empty)
            .Select(a => a.ActivityId)
            .Distinct()
            .ToList();

        var provenanceById = activityIds.Count == 0
            ? new Dictionary<Guid, ProvenanceActivityRecord>()
            : await db.ProvenanceActivities.AsNoTracking()
                .Where(p => p.TenantId == tenant && activityIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct)
                .ConfigureAwait(false);

        var activityAttestationAudits = activityIds.Count == 0
            ? []
            : await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == tenant
                    && a.ActivityId != Guid.Empty
                    && activityIds.Contains(a.ActivityId)
                    && a.AggregateType == "Attestation")
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var linkedAttestationIds = new HashSet<AttestationId>(directAttestations.Select(a => a.Id));
        foreach (var assertion in relatedAssertions)
        {
            if (assertion.AttestationId is { } aid)
            {
                linkedAttestationIds.Add(aid);
            }
        }

        foreach (var attAudit in activityAttestationAudits)
        {
            if (AttestationId.TryParse(attAudit.AggregateId, out var parsedAttId))
            {
                linkedAttestationIds.Add(parsedAttId);
            }
        }

        if (queriedNode.Kind == NodeKinds.ProtocolVersion)
        {
            var approvalAttId = await db.ProtocolVersions.AsNoTracking()
                .Where(p => p.TenantId == tenant && p.Id == ProtocolVersionId.FromGuid(queriedNode.Id) && p.ApprovalAttestationId != null)
                .Select(p => p.ApprovalAttestationId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (approvalAttId is { } pid)
            {
                linkedAttestationIds.Add(pid);
            }
        }

        var allAttestations = linkedAttestationIds.Count == 0
            ? new Dictionary<AttestationId, Attestation>()
            : await db.Attestations.AsNoTracking()
                .Where(a => a.TenantId == tenant && linkedAttestationIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct)
                .ConfigureAwait(false);

        var attestationIdStrings = allAttestations.Keys.Select(k => k.ToString()).ToList();
        var standaloneAttestationAudits = attestationIdStrings.Count == 0
            ? new Dictionary<string, AuditEntryRecord>(StringComparer.OrdinalIgnoreCase)
            : (await db.AuditEntries.AsNoTracking()
                    .Where(a => a.TenantId == tenant
                        && a.AggregateType == "Attestation"
                        && a.AggregateId != null
                        && attestationIdStrings.Contains(a.AggregateId))
                    .ToListAsync(ct)
                    .ConfigureAwait(false))
                .GroupBy(a => a.AggregateId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Sequence).First(), StringComparer.OrdinalIgnoreCase);

        DateTimeOffset? obsObservedAt = null;
        var obsRevisionsByVersion = new Dictionary<int, ObservationRevision>();
        if (queriedNode.Kind == NodeKinds.Observation)
        {
            var obsId = ObservationId.FromGuid(queriedNode.Id);
            obsObservedAt = await db.Observations.AsNoTracking()
                .Where(o => o.TenantId == tenant && o.Id == obsId)
                .Select(o => (DateTimeOffset?)o.ObservedAt)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            var obsRevisions = await db.ObservationRevisions.AsNoTracking()
                .Where(r => r.TenantId == tenant && r.ObservationId == obsId)
                .OrderBy(r => r.Number)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            obsRevisionsByVersion = obsRevisions.ToDictionary(r => r.Number);
        }

        var assertionsById = relatedAssertions.ToDictionary(a => a.Id.ToString(), StringComparer.OrdinalIgnoreCase);
        var attachedAttestationIds = new HashSet<AttestationId>();
        var candidates = new List<RawHistoryCandidate>(auditRecords.Count + directAttestations.Count);

        foreach (var audit in auditRecords)
        {
            var provenance = audit.ActivityId != Guid.Empty ? provenanceById.GetValueOrDefault(audit.ActivityId) : null;
            var auditNodeRef = NodeRef.For(AuditEntryId.FromGuid(audit.Id));
            var mappedKind = KernelAggregateKinds.TryMapAggregateTypeToKind(audit.AggregateType) ?? queriedNode.Kind;
            NodeRef targetRef = queriedNode.Unversioned();
            if (KernelAggregateKinds.TryResolveTarget(audit.AggregateType, audit.AggregateId, out var resolvedTarget))
            {
                targetRef = audit.AggregateVersion is { } v
                    ? new NodeRef(resolvedTarget.Kind, resolvedTarget.Id, v)
                    : resolvedTarget;
            }

            // When a parent aggregate command (such as ProtocolVersion) produced a lineage node for a child element, keep targetRef on the queried child node.
            if (queriedNode.Kind == NodeKinds.ProtocolElement && mappedKind == NodeKinds.ProtocolVersion)
            {
                mappedKind = NodeKinds.ProtocolElement;
                targetRef = queriedNode.Unversioned();
            }

            var eventKind = "Audit";
            var action = audit.Action;
            DateTimeOffset? validAt = null;
            var fieldChanges = ParseChangesJson(audit.Changes);
            KernelHistoryAttestation? boundAttestation = null;

            if (audit.ActivityId != Guid.Empty)
            {
                var sameTxAttAudit = activityAttestationAudits.FirstOrDefault(x => x.ActivityId == audit.ActivityId);
                if (sameTxAttAudit is not null
                    && AttestationId.TryParse(sameTxAttAudit.AggregateId, out var sameTxAttId)
                    && allAttestations.TryGetValue(sameTxAttId, out var sameTxAtt))
                {
                    boundAttestation = MapAttestation(sameTxAtt);
                    attachedAttestationIds.Add(sameTxAttId);
                }
            }

            if (mappedKind == NodeKinds.Observation && audit.AggregateVersion is { } revNum && obsRevisionsByVersion.TryGetValue((int)revNum, out var rev))
            {
                validAt = obsObservedAt ?? rev.RecordedAt;
                obsRevisionsByVersion.TryGetValue((int)revNum - 1, out var prevRev);
                fieldChanges = BuildObservationFieldChanges(prevRev, rev, fieldChanges);
                if (revNum > 1)
                {
                    eventKind = "Correction";
                    action = "Corrected";
                }
            }
            else if (mappedKind == NodeKinds.Assertion
                && audit.AggregateId is not null
                && assertionsById.TryGetValue(audit.AggregateId, out var assertionRow))
            {
                validAt = assertionRow.AssertedAt;
                eventKind = ClassifyAssertionEventKind(assertionRow);
                action = ClassifyAssertionAction(assertionRow);
                if (boundAttestation is null
                    && assertionRow.AttestationId is { } asrtAttId
                    && allAttestations.TryGetValue(asrtAttId, out var asrtAtt))
                {
                    boundAttestation = MapAttestation(asrtAtt);
                    attachedAttestationIds.Add(asrtAttId);
                }
            }
            else if (mappedKind == NodeKinds.Attestation
                && AttestationId.TryParse(audit.AggregateId, out var selfAttId)
                && allAttestations.TryGetValue(selfAttId, out var selfAtt))
            {
                eventKind = "Attestation";
                action = "Signed";
                validAt = selfAtt.AuthenticatedAt;
                boundAttestation = MapAttestation(selfAtt);
                attachedAttestationIds.Add(selfAttId);
            }
            else if (string.Equals(provenance?.CommandType, "protocol.approve", StringComparison.OrdinalIgnoreCase)
                || string.Equals(provenance?.CommandType, "ApproveProtocolVersion", StringComparison.OrdinalIgnoreCase))
            {
                var approvalAtt = allAttestations.Values.FirstOrDefault(a =>
                    a.TargetKind == NodeKinds.ProtocolVersion && a.TargetId == queriedNode.Id);
                if (approvalAtt is not null && boundAttestation is null)
                {
                    boundAttestation = MapAttestation(approvalAtt);
                    attachedAttestationIds.Add(approvalAtt.Id);
                }
            }

            var reason = FirstNonBlank(audit.ReasonForChange, provenance?.ReasonForChange, boundAttestation?.Reason);
            var kindLabel = KernelAggregateKinds.HumanizeKind(mappedKind, audit.AggregateType);
            var summary = DescribeActionSummary(action, provenance?.CommandType, kindLabel, audit.AggregateVersion);

            candidates.Add(new RawHistoryCandidate(
                EntryId: auditNodeRef.ToString(),
                AuditEntryRef: auditNodeRef,
                EventKind: eventKind,
                Action: action,
                CommandType: provenance?.CommandType,
                Outcome: provenance?.Outcome ?? "Committed",
                RecordedAt: audit.RecordedAt,
                ValidAt: validAt,
                ActorId: audit.ActorId,
                ActorKind: provenance?.ActorKind.ToString(),
                TargetKind: mappedKind,
                TargetRef: targetRef,
                Version: audit.AggregateVersion,
                ReasonForChange: reason,
                Summary: summary,
                FieldChanges: fieldChanges,
                Attestation: boundAttestation,
                Sequence: audit.Sequence,
                Hash: audit.Hash,
                PredecessorHash: audit.PredecessorHash));
        }

        foreach (var attestation in directAttestations.Where(a => !attachedAttestationIds.Contains(a.Id)))
        {
            standaloneAttestationAudits.TryGetValue(attestation.Id.ToString(), out var attAudit);
            var provenance = attAudit is not null && attAudit.ActivityId != Guid.Empty
                ? provenanceById.GetValueOrDefault(attAudit.ActivityId)
                : null;
            var auditNodeRef = attAudit is not null ? NodeRef.For(AuditEntryId.FromGuid(attAudit.Id)) : null;
            var mappedAttestation = MapAttestation(attestation);

            candidates.Add(new RawHistoryCandidate(
                EntryId: auditNodeRef?.ToString() ?? mappedAttestation.AttestationRef.ToString(),
                AuditEntryRef: auditNodeRef,
                EventKind: "Attestation",
                Action: "Signed",
                CommandType: provenance?.CommandType ?? "attestation.sign",
                Outcome: provenance?.Outcome ?? "Committed",
                RecordedAt: attestation.RecordedAt,
                ValidAt: attestation.AuthenticatedAt,
                ActorId: attestation.ActorId,
                ActorKind: provenance?.ActorKind.ToString(),
                TargetKind: attestation.TargetKind,
                TargetRef: new NodeRef(attestation.TargetKind, attestation.TargetId),
                Version: attAudit?.AggregateVersion ?? attestation.VersionIndex,
                ReasonForChange: FirstNonBlank(attestation.Reason, attAudit?.ReasonForChange),
                Summary: $"{attestation.Meaning} electronic signature recorded",
                FieldChanges: [],
                Attestation: mappedAttestation,
                Sequence: attAudit?.Sequence,
                Hash: attAudit?.Hash,
                PredecessorHash: attAudit?.PredecessorHash));
        }

        return candidates;
    }

    private static List<RawHistoryCandidate> FilterAndOrderCandidates(
        List<RawHistoryCandidate> candidates,
        NodeHistoryWindow window,
        int limit)
    {
        IEnumerable<RawHistoryCandidate> query = candidates;
        if (window.KnownAt is { } knownAt)
        {
            query = query.Where(c => c.RecordedAt <= knownAt);
        }

        if (window.ValidAt is { } validAt)
        {
            query = query.Where(c => (c.ValidAt ?? c.RecordedAt) <= validAt);
        }

        return query
            .OrderByDescending(c => c.RecordedAt)
            .ThenByDescending(c => c.Sequence ?? 0L)
            .Take(limit)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<NodeRef, MaskReason>> ComputeVisibilityMasksAsync(
        TenantId tenant,
        Actor? reader,
        NodeRef queriedNode,
        List<KernelHistoryEntry> entries,
        CancellationToken ct)
    {
        var nodes = new HashSet<NodeRef> { queriedNode };
        foreach (var entry in entries)
        {
            if (entry.AuditEntryRef is { } auditRef)
            {
                nodes.Add(auditRef.Unversioned());
            }

            if (entry.TargetRef is { } targetRef)
            {
                nodes.Add(targetRef.Unversioned());
            }

            if (entry.Attestation is { } attestation)
            {
                nodes.Add(attestation.AttestationRef.Unversioned());
            }
        }

        if (reader is null)
        {
            return nodes.ToDictionary(n => n, _ => MaskReason.NotPermitted);
        }

        return await visibility.MaskAsync(tenant, reader, [.. nodes], ct).ConfigureAwait(false);
    }

    private static MaskReason? ResolveEntryMask(
        KernelHistoryEntry entry,
        MaskReason? nodeMask,
        IReadOnlyDictionary<NodeRef, MaskReason> masks)
    {
        if (nodeMask is not null)
        {
            return nodeMask;
        }

        if (entry.TargetRef is { } targetRef && masks.TryGetValue(targetRef.Unversioned(), out var targetMask))
        {
            return targetMask;
        }

        if (entry.AuditEntryRef is { } auditRef && masks.TryGetValue(auditRef.Unversioned(), out var auditMask))
        {
            return auditMask;
        }

        if (entry.Attestation is { } att && masks.TryGetValue(att.AttestationRef.Unversioned(), out var attMask))
        {
            return attMask;
        }

        return null;
    }

    private static KernelHistoryEntry MaskHistoryEntry(KernelHistoryEntry entry, MaskReason reason, int ordinal) =>
        new(
            EntryId: $"masked:{ordinal}",
            AuditEntryRef: null,
            EventKind: "Restricted",
            Action: "Restricted",
            CommandType: null,
            Outcome: null,
            RecordedAt: entry.RecordedAt,
            ValidAt: null,
            ActorId: null,
            ActorKind: null,
            ActorRef: null,
            TargetKind: entry.TargetKind,
            TargetRef: null,
            Version: null,
            ReasonForChange: null,
            Summary: null,
            FieldChanges: [],
            Attestation: null,
            Sequence: null,
            Hash: null,
            PredecessorHash: null,
            Mask: reason);

    private static KernelHistoryEntry MaterializeEntry(RawHistoryCandidate c, HashSet<Guid> knownPeople)
    {
        KernelHistoryAttestation? attestation = null;
        if (c.Attestation is not null)
        {
            attestation = c.Attestation with
            {
                ActorRef = ResolveActorNodeRef(c.Attestation.ActorId, knownPeople),
            };
        }

        return new KernelHistoryEntry(
            EntryId: c.EntryId,
            AuditEntryRef: c.AuditEntryRef,
            EventKind: c.EventKind,
            Action: c.Action,
            CommandType: c.CommandType,
            Outcome: c.Outcome,
            RecordedAt: c.RecordedAt,
            ValidAt: c.ValidAt,
            ActorId: c.ActorId,
            ActorKind: c.ActorKind,
            ActorRef: ResolveActorNodeRef(c.ActorId, knownPeople),
            TargetKind: c.TargetKind,
            TargetRef: c.TargetRef,
            Version: c.Version,
            ReasonForChange: c.ReasonForChange,
            Summary: c.Summary,
            FieldChanges: c.FieldChanges,
            Attestation: attestation,
            Sequence: c.Sequence,
            Hash: c.Hash,
            PredecessorHash: c.PredecessorHash,
            Mask: null);
    }

    private static async Task<NodeRef?> ResolveAuditTargetRefAsync(
        KernelDbContext db,
        TenantId tenant,
        AuditEntryRecord record,
        CancellationToken ct)
    {
        if (KernelAggregateKinds.TryResolveTarget(record.AggregateType, record.AggregateId, out var target)
            && target.Kind != NodeKinds.AuditEntry)
        {
            if (target.Kind == NodeKinds.Attestation)
            {
                var att = await db.Attestations.AsNoTracking()
                    .Where(a => a.TenantId == tenant && a.Id == AttestationId.FromGuid(target.Id))
                    .Select(a => new { a.TargetKind, a.TargetId })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
                if (att is not null && NodeKinds.IsRegistered(att.TargetKind) && att.TargetId != Guid.Empty)
                {
                    return new NodeRef(att.TargetKind, att.TargetId);
                }
            }

            return target;
        }

        if (record.ActivityId != Guid.Empty)
        {
            var lineageNode = await db.LineageNodes.AsNoTracking()
                .Where(l => l.TenantId == tenant && l.ActivityId == record.ActivityId && l.Kind != NodeKinds.Tenant)
                .OrderBy(l => l.RecordedAt)
                .Select(l => new { l.Kind, l.NodeId })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (lineageNode is not null && NodeKinds.IsRegistered(lineageNode.Kind))
            {
                return new NodeRef(lineageNode.Kind, lineageNode.NodeId);
            }
        }

        return null;
    }

    private static async Task<KernelHistoryAttestation?> FindAttestationForAuditEntryAsync(
        KernelDbContext db,
        TenantId tenant,
        AuditEntryRecord record,
        NodeRef? targetRef,
        CancellationToken ct)
    {
        if (KernelAggregateKinds.TryMapAggregateTypeToKind(record.AggregateType) == NodeKinds.Attestation
            && AttestationId.TryParse(record.AggregateId, out var directAttId))
        {
            var direct = await db.Attestations.AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == tenant && a.Id == directAttId, ct)
                .ConfigureAwait(false);
            if (direct is not null)
            {
                return MapAttestation(direct);
            }
        }

        if (record.ActivityId != Guid.Empty)
        {
            var siblingAttAudit = await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.ActivityId == record.ActivityId && a.AggregateType == "Attestation")
                .Select(a => a.AggregateId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (AttestationId.TryParse(siblingAttAudit, out var siblingAttId))
            {
                var sibling = await db.Attestations.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.TenantId == tenant && a.Id == siblingAttId, ct)
                    .ConfigureAwait(false);
                if (sibling is not null)
                {
                    return MapAttestation(sibling);
                }
            }
        }

        if (targetRef?.Kind == NodeKinds.Assertion && AssertionId.TryParse(targetRef.Id.ToString(), out var assertionId))
        {
            var assertionAttId = await db.Assertions.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.Id == assertionId && a.AttestationId != null)
                .Select(a => a.AttestationId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (assertionAttId is { } foundAttId)
            {
                var bound = await db.Attestations.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.TenantId == tenant && a.Id == foundAttId, ct)
                    .ConfigureAwait(false);
                if (bound is not null)
                {
                    return MapAttestation(bound);
                }
            }
        }

        return null;
    }

    private static async Task<bool> ExistsInDomainTableAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef node,
        CancellationToken ct) =>
        node.Kind switch
        {
            NodeKinds.Participant => await db.Participants.AsNoTracking()
                .AnyAsync(p => p.TenantId == tenant && p.Id == ParticipantId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.Person => await db.TenantMemberships.AsNoTracking()
                .AnyAsync(m => m.TenantId == tenant && m.PersonId == PersonId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.Role => await db.Roles.AsNoTracking()
                .AnyAsync(r => r.TenantId == tenant && r.Id == RoleId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.Grant => await db.Grants.AsNoTracking()
                .AnyAsync(g => g.TenantId == tenant && g.Id == GrantId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.Qualification => await db.Qualifications.AsNoTracking()
                .AnyAsync(q => q.TenantId == tenant && q.Id == QualificationId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.QualificationRequirement => await db.QualificationRequirements.AsNoTracking()
                .AnyAsync(q => q.TenantId == tenant && q.Id == QualificationRequirementId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.Effectivity => await db.Effectivities.AsNoTracking()
                .AnyAsync(e => e.TenantId == tenant && e.Id == EffectivityId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.WorkQueueDefinition => await db.WorkQueueDefinitions.AsNoTracking()
                .AnyAsync(w => w.TenantId == tenant && w.Id == WorkQueueDefinitionId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.Subscription => await db.Subscriptions.AsNoTracking()
                .AnyAsync(s => s.TenantId == tenant && s.Id == SubscriptionId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            NodeKinds.ProductEntitlement => await db.ProductEntitlements.AsNoTracking()
                .AnyAsync(p => p.TenantId == tenant && p.Id == ProductEntitlementId.FromGuid(node.Id), ct)
                .ConfigureAwait(false),
            _ => false,
        };

    private static List<PersonId> CollectCandidatePersonIds(IEnumerable<string?> actorIds)
    {
        var ids = new HashSet<PersonId>();
        foreach (var raw in actorIds)
        {
            if (TryExtractPersonGuid(raw, out var guid))
            {
                ids.Add(PersonId.FromGuid(guid));
            }
        }

        return [.. ids];
    }

    private static async Task<HashSet<Guid>> ResolveKnownPeopleAsync(
        KernelDbContext db,
        TenantId tenant,
        List<PersonId> candidates,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var rows = await db.TenantMemberships.AsNoTracking()
            .Where(m => m.TenantId == tenant && candidates.Contains(m.PersonId))
            .Select(m => m.PersonId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. rows.Select(id => id.Value)];
    }

    private static NodeRef? ResolveActorNodeRef(string? actorId, HashSet<Guid> knownPeople)
    {
        if (TryExtractPersonGuid(actorId, out var guid) && knownPeople.Contains(guid))
        {
            return NodeRef.For(PersonId.FromGuid(guid));
        }

        if (NodeRef.TryParse(actorId, out var parsed) && parsed.Kind == NodeKinds.Person)
        {
            return parsed.Unversioned();
        }

        return null;
    }

    private static bool TryExtractPersonGuid(string? actorId, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return false;
        }

        if (Guid.TryParse(actorId, out var direct) && direct != Guid.Empty)
        {
            guid = direct;
            return true;
        }

        if (NodeRef.TryParse(actorId, out var parsed) && parsed.Kind == NodeKinds.Person)
        {
            guid = parsed.Id;
            return true;
        }

        return false;
    }

    private static KernelHistoryAttestation MapAttestation(Attestation a) =>
        new(
            AttestationRef: NodeRef.For(a.Id),
            ActorId: a.ActorId,
            ActorRef: null,
            Meaning: a.Meaning.ToString(),
            Method: a.Method.ToString(),
            AuthenticatedAt: a.AuthenticatedAt,
            RecordedAt: a.RecordedAt,
            TargetContentHash: a.TargetContentHash,
            Reason: string.IsNullOrWhiteSpace(a.Reason) ? null : a.Reason);

    private static IReadOnlyList<KernelHistoryFieldChange> BuildObservationFieldChanges(
        ObservationRevision? previous,
        ObservationRevision current,
        IReadOnlyList<KernelHistoryFieldChange> parsedChanges)
    {
        var result = new List<KernelHistoryFieldChange>
        {
            new(
                "value",
                previous?.Value.ToDisplayString(),
                current.Value.ToDisplayString()),
        };

        foreach (var item in parsedChanges)
        {
            if (!string.Equals(item.Field, "value", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Field, "Value", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(item);
            }
        }

        return result;
    }

    internal static IReadOnlyList<KernelHistoryFieldChange> ParseChangesJson(string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(changesJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var hasBefore = root.TryGetProperty("before", out var beforeEl) && beforeEl.ValueKind == JsonValueKind.Object;
            var hasAfter = root.TryGetProperty("after", out var afterEl) && afterEl.ValueKind == JsonValueKind.Object;

            if (hasBefore || hasAfter)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                if (hasBefore)
                {
                    foreach (var prop in beforeEl.EnumerateObject())
                    {
                        keys.Add(prop.Name);
                    }
                }

                if (hasAfter)
                {
                    foreach (var prop in afterEl.EnumerateObject())
                    {
                        keys.Add(prop.Name);
                    }
                }

                var diffs = new List<KernelHistoryFieldChange>();
                foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (IgnoredDiffKeys.Contains(key))
                    {
                        continue;
                    }

                    var beforeVal = hasBefore && beforeEl.TryGetProperty(key, out var bProp) ? FormatJsonValue(bProp) : null;
                    var afterVal = hasAfter && afterEl.TryGetProperty(key, out var aProp) ? FormatJsonValue(aProp) : null;
                    if (!string.Equals(beforeVal, afterVal, StringComparison.Ordinal))
                    {
                        diffs.Add(new KernelHistoryFieldChange(key, beforeVal, afterVal));
                    }
                }

                return diffs;
            }

            var flat = new List<KernelHistoryFieldChange>();
            foreach (var prop in root.EnumerateObject())
            {
                if (IgnoredDiffKeys.Contains(prop.Name))
                {
                    continue;
                }

                if (prop.Value.ValueKind == JsonValueKind.Object
                    && (prop.Value.TryGetProperty("old", out _)
                        || prop.Value.TryGetProperty("before", out _)
                        || prop.Value.TryGetProperty("new", out _)
                        || prop.Value.TryGetProperty("after", out _)))
                {
                    var oldVal = TryGetAnyProperty(prop.Value, "before", "old");
                    var newVal = TryGetAnyProperty(prop.Value, "after", "new");
                    if (!string.Equals(oldVal, newVal, StringComparison.Ordinal))
                    {
                        flat.Add(new KernelHistoryFieldChange(prop.Name, oldVal, newVal));
                    }
                }
                else
                {
                    var formatted = FormatJsonValue(prop.Value);
                    if (formatted is not null)
                    {
                        flat.Add(new KernelHistoryFieldChange(prop.Name, null, formatted));
                    }
                }
            }

            return flat;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? TryGetAnyProperty(JsonElement obj, string first, string second)
    {
        if (obj.TryGetProperty(first, out var p1))
        {
            return FormatJsonValue(p1);
        }

        if (obj.TryGetProperty(second, out var p2))
        {
            return FormatJsonValue(p2);
        }

        return null;
    }

    private static string? FormatJsonValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            _ => element.GetRawText(),
        };

    private static string ClassifyAssertionEventKind(Assertion assertion)
    {
        if (assertion.RetractsId is not null)
        {
            return assertion.IsLock ? "Lock" : "Retraction";
        }

        return assertion.Kind switch
        {
            AssertionKind.Verified or AssertionKind.Reviewed => "Verification",
            AssertionKind.Frozen or AssertionKind.Locked => "Lock",
            _ => "Assertion",
        };
    }

    private static string ClassifyAssertionAction(Assertion assertion)
    {
        if (assertion.RetractsId is not null)
        {
            return assertion.IsLock ? "Unlocked" : "Retracted";
        }

        return assertion.Kind switch
        {
            AssertionKind.Verified => "Verified",
            AssertionKind.Reviewed => "Reviewed",
            AssertionKind.Frozen => "Frozen",
            AssertionKind.Locked => "Locked",
            AssertionKind.Queried => "Queried",
            AssertionKind.QueryAnswered => "Answered",
            AssertionKind.CodedAs => "Coded",
            AssertionKind.Adjudicated => "Adjudicated",
            _ => assertion.Kind.ToString(),
        };
    }

    private static string DescribeActionSummary(string action, string? commandType, string kindLabel, long? version)
    {
        var versionSuffix = version is > 0 ? $" (v{version})" : string.Empty;
        if (!string.IsNullOrWhiteSpace(commandType))
        {
            return $"{action} {kindLabel}{versionSuffix} via {commandType}";
        }

        return $"{action} {kindLabel}{versionSuffix}";
    }

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private sealed record RawHistoryCandidate(
        string EntryId,
        NodeRef? AuditEntryRef,
        string EventKind,
        string Action,
        string? CommandType,
        string? Outcome,
        DateTimeOffset RecordedAt,
        DateTimeOffset? ValidAt,
        string ActorId,
        string? ActorKind,
        string TargetKind,
        NodeRef? TargetRef,
        long? Version,
        string? ReasonForChange,
        string? Summary,
        IReadOnlyList<KernelHistoryFieldChange> FieldChanges,
        KernelHistoryAttestation? Attestation,
        long? Sequence,
        string? Hash,
        string? PredecessorHash);
}
