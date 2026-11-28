using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// Reads a form instance in one call (spec §8.1, ADR-2622): its values with their revisions, the assertions about them,
/// the signatures over the form and the locks in force. Reads are tenant-scoped by Row-Level Security and never change
/// state. Kernel persistence implements it (<c>AddKernelPersistence</c>).
/// </summary>
public interface ICollectionInstanceRecords
{
    /// <summary>The form instance, or null when it does not exist in <paramref name="tenant"/>.</summary>
    Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the form instance using an already-parsed <paramref name="governingDesign"/> when available so callers that
    /// just validated against the governing protocol version do not parse its content a second time.
    /// </summary>
    Task<CollectionInstanceRecord?> FindAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        ProtocolDesign? governingDesign,
        CancellationToken cancellationToken = default) =>
        FindAsync(tenant, instance, cancellationToken);

    /// <summary>
    /// Reads <paramref name="instance"/> for a caller whose unblinding status is <paramref name="unblinded"/>,
    /// masking blinded values, signed snapshots, assertions, locks, and observation handles before returning (K-AUTH-4).
    /// </summary>
    async Task<CollectionInstanceRecord?> FindForReaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        bool unblinded,
        CancellationToken cancellationToken)
    {
        var record = await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false);
        return record is not null && !unblinded ? record.MaskBlinded() : record;
    }

    /// <summary>
    /// Reads <paramref name="instance"/> and evaluates <paramref name="canUnblindAsync"/> against its enrollment
    /// before returning so blinded members are masked inside the port when the caller lacks <c>enrollment.unblind</c>.
    /// </summary>
    async Task<CollectionInstanceRecord?> FindForReaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canUnblindAsync);
        var record = await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var unblinded = await canUnblindAsync(record.EnrollmentId, cancellationToken).ConfigureAwait(false);
        return unblinded ? record : record.MaskBlinded();
    }

    /// <summary>
    /// Reads <paramref name="instance"/> using an already-parsed <paramref name="governingDesign"/> and evaluates
    /// <paramref name="canUnblindAsync"/> against its enrollment before returning so blinded members are masked inside
    /// the port without re-parsing the governing protocol version.
    /// </summary>
    async Task<CollectionInstanceRecord?> FindForReaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        ProtocolDesign? governingDesign,
        Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canUnblindAsync);
        var record = await FindAsync(tenant, instance, governingDesign, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var unblinded = await canUnblindAsync(record.EnrollmentId, cancellationToken).ConfigureAwait(false);
        return unblinded ? record : record.MaskBlinded();
    }

    /// <summary>
    /// Reads <paramref name="instance"/> and evaluates unblinding authority against the ambient caller when available,
    /// failing closed (masked) when no unblinding probe permits disclosure.
    /// </summary>
    Task<CollectionInstanceRecord?> FindForReaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        CancellationToken cancellationToken) =>
        FindForReaderAsync(tenant, instance, unblinded: false, cancellationToken);

    /// <summary>
    /// Reads <paramref name="instances"/> in one batch for a caller who may unblind only <paramref name="unblindedEnrollments"/>,
    /// masking blinded members and stripping observation handles on all other enrollments before returning.
    /// </summary>
    async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyForReaderAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        IReadOnlySet<EnrollmentId> unblindedEnrollments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(unblindedEnrollments);
        var found = await FindManyAsync(tenant, instances, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<CollectionInstanceId, CollectionInstanceRecord>(found.Count);
        foreach (var (id, record) in found)
        {
            result[id] = unblindedEnrollments.Contains(record.EnrollmentId) ? record : record.MaskBlinded();
        }

        return result;
    }

    /// <summary>
    /// Reads <paramref name="instances"/> in one batch and evaluates <paramref name="canUnblindAsync"/> per distinct
    /// enrollment before returning masked or unblinded records.
    /// </summary>
    async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyForReaderAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(canUnblindAsync);
        var found = await FindManyAsync(tenant, instances, cancellationToken).ConfigureAwait(false);
        if (found.Count == 0)
        {
            return found;
        }

        var unblindedEnrollments = new HashSet<EnrollmentId>();
        foreach (var enrollmentId in found.Values.Select(r => r.EnrollmentId).Distinct())
        {
            if (await canUnblindAsync(enrollmentId, cancellationToken).ConfigureAwait(false))
            {
                unblindedEnrollments.Add(enrollmentId);
            }
        }

        var result = new Dictionary<CollectionInstanceId, CollectionInstanceRecord>(found.Count);
        foreach (var (id, record) in found)
        {
            result[id] = unblindedEnrollments.Contains(record.EnrollmentId) ? record : record.MaskBlinded();
        }

        return result;
    }

    /// <summary>
    /// Reads <paramref name="instances"/> in one batch, keyed by <see cref="CollectionInstanceId"/>; missing forms are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var found = new Dictionary<CollectionInstanceId, CollectionInstanceRecord>();
        foreach (var instance in instances.Where(id => !id.IsEmpty).Distinct())
        {
            if (await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false) is { } record)
            {
                found[instance] = record;
            }
        }

        return found;
    }

    /// <summary>
    /// Reads the current revision numbers for <paramref name="observations"/> in one batch; missing observations are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<ObservationId, int>> FindObservationRevisionsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        IReadOnlyCollection<CollectionInstanceId>? instances = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0 || instances is null || instances.Count == 0)
        {
            return new Dictionary<ObservationId, int>();
        }

        var wanted = observations.Where(id => !id.IsEmpty).ToHashSet();
        var records = await FindManyAsync(tenant, instances, cancellationToken).ConfigureAwait(false);
        var revisions = new Dictionary<ObservationId, int>();
        foreach (var value in records.Values.SelectMany(r => r.Values).Where(v => wanted.Contains(v.ObservationId)))
        {
            revisions[value.ObservationId] = value.Revision;
        }

        return revisions;
    }

    /// <summary>
    /// Reads current revision numbers and treatment-blinding flags for <paramref name="observations"/> in one batch
    /// without loading observation history, assertions, or signatures; missing observations are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<ObservationId, CollectionObservationProjection>> FindObservationProjectionsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        IReadOnlyCollection<CollectionInstanceId>? instances = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0 || instances is null || instances.Count == 0)
        {
            return new Dictionary<ObservationId, CollectionObservationProjection>();
        }

        var wanted = observations.Where(id => !id.IsEmpty).ToHashSet();
        var records = await FindManyAsync(tenant, instances, cancellationToken).ConfigureAwait(false);
        var projected = new Dictionary<ObservationId, CollectionObservationProjection>();
        foreach (var record in records.Values)
        {
            foreach (var value in record.Values.Where(v => wanted.Contains(v.ObservationId)))
            {
                var blinded = record.IsQueryBlinded(value.ItemId, value.ObservationId)
                    || BlindedTreatmentClassifier.MatchesMarkers(record.CollectionSpecId)
                    || BlindedTreatmentClassifier.MatchesMarkers(value.ItemId)
                    || value.Value is MissingValue { Reason: NullFlavor.Masked };
                projected[value.ObservationId] = new CollectionObservationProjection(value.Revision, blinded);
            }
        }

        return projected;
    }

    /// <summary>
    /// Reads the header metadata of <paramref name="instance"/> without hydrating values, assertions, codelists, or signatures.
    /// </summary>
    async Task<CollectionInstanceHeader?> FindHeaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty)
        {
            return null;
        }

        var record = await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false);
        return record is null ? null : CollectionInstanceHeader.From(record);
    }

    /// <summary>
    /// Reads header metadata for <paramref name="instances"/> in one batch; missing forms are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>> FindHeadersAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var found = await FindManyAsync(tenant, instances, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(kv => kv.Key, kv => CollectionInstanceHeader.From(kv.Value));
    }

    /// <summary>
    /// Reads narrow query-shaping targets for <paramref name="instances"/> in one batch, masking blinded observation
    /// handles on enrollments outside <paramref name="unblindedEnrollments"/> (K-AUTH-4).
    /// </summary>
    async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>> FindQueryTargetsAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        IReadOnlySet<EnrollmentId> unblindedEnrollments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(unblindedEnrollments);
        var found = await FindManyForReaderAsync(tenant, instances, unblindedEnrollments, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(kv => kv.Key, kv => CollectionInstanceQueryTarget.From(kv.Value));
    }

    /// <summary>
    /// Reads a targeted projection of <paramref name="instance"/> for query commands and blank-item checks: its lineage
    /// versions, whether an active <see cref="LockKind.Locked"/> assertion covers the form or target observation, and the
    /// current revision of the member matching <paramref name="observation"/> or <paramref name="itemId"/>.
    /// </summary>
    async Task<CollectionQueryTarget?> FindQueryTargetAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        ObservationId? observation = null,
        string? itemId = null,
        bool checkLocks = true,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty || await FindScopeStateAsync(tenant, instance, cancellationToken).ConfigureAwait(false) is not { } scopeState)
        {
            return null;
        }

        CollectionScopeValue? matched = null;
        if (observation is { IsEmpty: false } obsId)
        {
            matched = scopeState.Values.FirstOrDefault(v => v.ObservationId == obsId);
        }
        else if (!string.IsNullOrWhiteSpace(itemId))
        {
            var trimmed = itemId.Trim();
            matched = scopeState.Values.FirstOrDefault(v => string.Equals(v.ItemId, trimmed, StringComparison.Ordinal));
        }

        var lockTarget = observation is { IsEmpty: false } targetObs ? targetObs : matched?.ObservationId;
        var isLocked = checkLocks
            && scopeState.Locks.Any(l =>
                l.Kind == LockKind.Locked
                && (l.Scope.Kind != NodeKinds.Observation || (lockTarget is { } lt && l.Scope.Id == lt.Value)));

        return new CollectionQueryTarget(
            scopeState.CollectionInstanceId,
            scopeState.StudyId,
            scopeState.StudySiteId,
            scopeState.EnrollmentId,
            scopeState.CollectionSpecId,
            scopeState.PlannedActivityId,
            scopeState.Version,
            scopeState.EnrollmentVersion,
            isLocked,
            matched?.ItemId,
            matched?.ObservationId,
            matched?.Revision,
            matched?.Value is MissingValue { Reason: NullFlavor.NotAsked });
    }

    /// <summary>
    /// Reads a single recorded attestation on <paramref name="instance"/> with its reconstructed signed values without
    /// loading protocol designs, assertions, locks, or codelists.
    /// </summary>
    async Task<FormAttestationView?> FindAttestationAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        AttestationId attestation,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty || attestation.IsEmpty)
        {
            return null;
        }

        var record = await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false);
        return record?.Attestations.SingleOrDefault(a => a.AttestationId == attestation);
    }

    /// <summary>
    /// Reads a single recorded attestation on <paramref name="instance"/> for a caller whose unblinding status is
    /// <paramref name="unblinded"/>, masking blinded signed values and content hashes when <paramref name="unblinded"/>
    /// is false without loading assertions, locks, or codelists (K-AUTH-4).
    /// </summary>
    async Task<FormAttestationView?> FindAttestationForReaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        AttestationId attestation,
        bool unblinded,
        ProtocolDesign? governingDesign = null,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty || attestation.IsEmpty)
        {
            return null;
        }

        if (unblinded)
        {
            return await FindAttestationAsync(tenant, instance, attestation, cancellationToken).ConfigureAwait(false);
        }

        var record = await FindForReaderAsync(
            tenant,
            instance,
            governingDesign,
            (_, _) => ValueTask.FromResult(false),
            cancellationToken).ConfigureAwait(false);
        return record?.Attestations.SingleOrDefault(a => a.AttestationId == attestation);
    }

    /// <summary>
    /// Reads the scope-locking, lineage-version, and current-revision state of <paramref name="instance"/>.
    /// </summary>
    async Task<CollectionInstanceScopeState?> FindScopeStateAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false);
        return record is null ? null : CollectionInstanceScopeState.From(record);
    }

    /// <summary>
    /// Reads narrow rule-evaluation inputs for every form instance opened for <paramref name="enrollment"/>, oldest first.
    /// </summary>
    async Task<IReadOnlyList<CollectionRuleInput>> ListRuleInputsForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken = default)
    {
        var records = await ListRecordsForEnrollmentAsync(tenant, enrollment, cancellationToken).ConfigureAwait(false);
        return [.. records.Select(CollectionRuleInput.From)];
    }

    /// <summary>Every form instance opened for <paramref name="enrollment"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken);

    /// <summary>Every full form instance record opened for <paramref name="enrollment"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([]);

    /// <summary>Every full form instance record opened for <paramref name="study"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceRecord>> ListForStudyAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([]);
}

/// <summary>Summary of an opened form instance for an enrollment's form list.</summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="ParticipantId">The enrollment's participant.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="CollectedAt">When its values were collected in the trial world.</param>
/// <param name="OpenedBy">Who opened the form.</param>
/// <param name="OpenedAt">When it was opened.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
/// <param name="Complete">Whether every required item in the governing CollectionSpec has a value.</param>
/// <param name="ItemCount">Total items on the form.</param>
/// <param name="RecordedCount">Items on the form that currently hold a recorded observation.</param>
/// <param name="CollectionTitle">The CollectionSpec's title in the governing protocol version, when present.</param>
/// <param name="MissingRequiredCount">Required items on the form that still lack a recorded observation.</param>
/// <param name="VerifiedCount">Recorded items whose current revision carries an active source verification assertion.</param>
/// <param name="UnverifiedCount">Recorded items whose current revision is not source-verified.</param>
/// <param name="Locked">Whether an active hard lock covers the form or any of its covering scopes.</param>
/// <param name="Frozen">Whether an active data freeze covers the form or any of its covering scopes.</param>
/// <param name="LockCount">Active locks and freezes covering the form or its values.</param>
/// <param name="Signed">Whether at least one signature covers the form's current content hash.</param>
/// <param name="SignatureSuperseded">Whether the form has signatures, all superseded by later value changes.</param>
/// <param name="SignatureCount">Total signatures recorded over the form instance.</param>
/// <param name="OpenQueryCount">Un-retracted clinical queries on the form that still await an answer.</param>
/// <param name="AnsweredQueryCount">Un-retracted clinical queries on the form that have been answered and await closure.</param>
/// <param name="OpenErrorFindingCount">Active Open or Investigating Error validation findings targeting the form or its values.</param>
/// <param name="ReadinessBlockers">Diagnostic blockers preventing completeness, signature, or scope lock when non-empty.</param>
/// <param name="EnrollmentReferenceAt">Enrollment reference timestamp (enrolled period start when enrolled, otherwise current status start) in site offset time.</param>
/// <param name="ActiveQueryAssertionIds">Un-retracted <see cref="AssertionKind.Queried"/> assertion ids on the form's values, when projected.</param>
/// <param name="AnsweredQueryAssertionIds">Subset of <paramref name="ActiveQueryAssertionIds"/> carrying an un-retracted <see cref="AssertionKind.QueryAnswered"/> assertion, when projected.</param>
/// <param name="VisitTimingAssessment">Pre-evaluated visit timing assessment against the governing protocol schedule, when evaluated during projection.</param>
/// <param name="VisitTimingEvaluated">True when <paramref name="VisitTimingAssessment"/> was already evaluated against the governing protocol design.</param>
public sealed record CollectionInstanceSummary(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    string OpenedBy,
    DateTimeOffset OpenedAt,
    long Version,
    bool Complete,
    int ItemCount,
    int RecordedCount,
    string? CollectionTitle = null,
    int MissingRequiredCount = 0,
    int VerifiedCount = 0,
    int UnverifiedCount = 0,
    bool Locked = false,
    bool Frozen = false,
    int LockCount = 0,
    bool Signed = false,
    bool SignatureSuperseded = false,
    int SignatureCount = 0,
    int OpenQueryCount = 0,
    int AnsweredQueryCount = 0,
    int OpenErrorFindingCount = 0,
    IReadOnlyList<string>? ReadinessBlockers = null,
    DateTimeOffset? EnrollmentReferenceAt = null,
    IReadOnlySet<AssertionId>? ActiveQueryAssertionIds = null,
    IReadOnlySet<AssertionId>? AnsweredQueryAssertionIds = null,
    VisitTimingAssessment? VisitTimingAssessment = null,
    bool VisitTimingEvaluated = false);
