using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
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

/// <summary>Header metadata of an opened form instance without member values, assertions, codelists, or signatures.</summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="ParticipantId">The enrollment's participant.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="CollectedAt">When its values were collected in the trial world.</param>
/// <param name="OpenedAt">When it was opened.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
public sealed record CollectionInstanceHeader(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    DateTimeOffset OpenedAt,
    long Version)
{
    /// <summary>Initializes a scalar header with study, site, enrollment, specification, and planned activity identifiers.</summary>
    public CollectionInstanceHeader(
        CollectionInstanceId collectionInstanceId,
        StudyId studyId,
        StudySiteId studySiteId,
        EnrollmentId enrollmentId,
        string collectionSpecId,
        string? plannedActivityId)
        : this(
            collectionInstanceId,
            enrollmentId,
            ParticipantId.Empty,
            studyId,
            studySiteId,
            ProtocolVersionId.Empty,
            collectionSpecId,
            plannedActivityId,
            default,
            default,
            1)
    {
    }

    /// <summary>Projects header metadata from <paramref name="record"/>.</summary>
    public static CollectionInstanceHeader From(CollectionInstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(
            record.CollectionInstanceId,
            record.EnrollmentId,
            record.ParticipantId,
            record.StudyId,
            record.StudySiteId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.CollectedAt,
            record.OpenedAt,
            record.Version);
    }
}

/// <summary>
/// Current revision number and treatment-blinding flag for a kernel observation, read in batch without loading revision
/// history, assertions, or signatures.
/// </summary>
/// <param name="Revision">Current observation revision number.</param>
/// <param name="IsBlinded">True when the observation classifies as blinded treatment data (<c>K-AUTH-4</c>).</param>
public readonly record struct CollectionObservationProjection(int Revision, bool IsBlinded = false);

/// <summary>
/// Targeted projection of a form instance for query commands and blank-item checks: its scalar header, lineage versions,
/// whether an active <see cref="LockKind.Locked"/> assertion covers the form or target observation, and the matched
/// item/observation member's current revision.
/// </summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="EnrollmentId">The participant enrollment.</param>
/// <param name="CollectionSpecId">The CollectionSpec identifier.</param>
/// <param name="PlannedActivityId">The planned activity (visit) identifier, when scheduled.</param>
/// <param name="Version">Current version index of the form instance.</param>
/// <param name="EnrollmentVersion">Current version index of the governing enrollment.</param>
/// <param name="IsLocked">True when an active <see cref="LockKind.Locked"/> assertion covers the form or target observation.</param>
/// <param name="ItemId">Item identifier of the matched member, when present.</param>
/// <param name="ObservationId">Observation identifier of the matched member, when present.</param>
/// <param name="Revision">Current revision number of the matched member, when present.</param>
/// <param name="IsUnaskedPlaceholder">True when the matched member's current value is <see cref="NullFlavor.NotAsked"/>.</param>
public sealed record CollectionQueryTarget(
    CollectionInstanceId CollectionInstanceId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    EnrollmentId EnrollmentId,
    string CollectionSpecId,
    string? PlannedActivityId,
    long Version,
    long EnrollmentVersion,
    bool IsLocked,
    string? ItemId = null,
    ObservationId? ObservationId = null,
    int? Revision = null,
    bool IsUnaskedPlaceholder = false);

/// <summary>
/// Narrow projection of a form instance for query worklists and query step responses: header metadata, current
/// observation and item revisions, and blinding classification without assertions, locks, codelists, or signatures.
/// </summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="ObservationRevisions">Current revision number per unmasked observation on the form.</param>
/// <param name="ItemRevisions">Current revision number per item identifier on the form.</param>
/// <param name="BlindedItemIds">Items classified as blinded treatment data (K-AUTH-4).</param>
/// <param name="BlindedObservationIds">Observations classified as blinded treatment data (K-AUTH-4).</param>
public sealed record CollectionInstanceQueryTarget(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    IReadOnlyDictionary<ObservationId, int> ObservationRevisions,
    IReadOnlyDictionary<string, int> ItemRevisions,
    IReadOnlySet<string>? BlindedItemIds = null,
    IReadOnlySet<ObservationId>? BlindedObservationIds = null)
{
    /// <summary>True when a query over <paramref name="itemId"/> / <paramref name="observationId"/> targets blinded treatment data.</summary>
    public bool IsQueryBlinded(string itemId, ObservationId observationId) =>
        (BlindedItemIds?.Contains(itemId) ?? false)
        || (BlindedObservationIds?.Contains(observationId) ?? false);

    /// <summary>Projects query target metadata from <paramref name="record"/>.</summary>
    public static CollectionInstanceQueryTarget From(CollectionInstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(
            record.CollectionInstanceId,
            record.EnrollmentId,
            record.StudyId,
            record.StudySiteId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.Values.Where(v => !v.ObservationId.IsEmpty).ToDictionary(v => v.ObservationId, v => v.Revision),
            record.Values.ToDictionary(v => v.ItemId, v => v.Revision, StringComparer.Ordinal),
            record.BlindedItemIds,
            record.BlindedObservationIds);
    }
}

/// <summary>One member observation and its current revision on a <see cref="CollectionInstanceScopeState"/>.</summary>
/// <param name="ItemId">The CollectionSpec item identifier.</param>
/// <param name="ObservationId">The observation holding the value.</param>
/// <param name="Value">The observation's current typed value.</param>
/// <param name="Revision">The observation's current revision number.</param>
public sealed record CollectionScopeValue(
    string ItemId,
    ObservationId ObservationId,
    ObservationValue Value,
    int Revision);

/// <summary>
/// Narrow projection of a form instance for scope locking, lineage linking, and revision checks without protocol
/// design parsing, codelists, verification/query assertions, or signatures.
/// </summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
/// <param name="EnrollmentVersion">Current version index of the governing enrollment for lineage linking.</param>
/// <param name="Values">Current member values and revisions, ordered by item id.</param>
/// <param name="Locks">Active locks and freezes covering the form or its values, widest scope first.</param>
public sealed record CollectionInstanceScopeState(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    long Version,
    long EnrollmentVersion,
    IReadOnlyList<CollectionScopeValue> Values,
    IReadOnlyList<LockView> Locks)
{
    /// <summary>Projects scope state from <paramref name="record"/>.</summary>
    public static CollectionInstanceScopeState From(CollectionInstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(
            record.CollectionInstanceId,
            record.EnrollmentId,
            record.StudyId,
            record.StudySiteId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.Version,
            record.EnrollmentVersion,
            [.. record.Values.Select(v => new CollectionScopeValue(v.ItemId, v.ObservationId, v.Value, v.Revision))],
            record.Locks);
    }
}

/// <summary>One member value of a <see cref="CollectionRuleInput"/> for edit-check evaluation.</summary>
/// <param name="ItemId">The CollectionSpec item identifier.</param>
/// <param name="ObservationId">The observation holding the value.</param>
/// <param name="Value">The observation's current typed value.</param>
public sealed record CollectionRuleValue(
    string ItemId,
    ObservationId ObservationId,
    ObservationValue Value);

/// <summary>
/// Narrow projection of a form instance for cross-form edit-check evaluation and observed-value comparison without
/// observation revision histories, assertions, locks, codelists, or signatures.
/// </summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="StudyId">The study.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="OpenedAt">When the form was opened.</param>
/// <param name="Items">The CollectionSpec's items in the governing protocol version.</param>
/// <param name="Values">Current member values, ordered by item id.</param>
/// <param name="CollectionTitle">The CollectionSpec's title in the governing protocol version, when present.</param>
public sealed record CollectionRuleInput(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    DateTimeOffset OpenedAt,
    IReadOnlyList<CollectionItem> Items,
    IReadOnlyList<CollectionRuleValue> Values,
    string? CollectionTitle = null)
{
    /// <summary>Projects rule input from <paramref name="record"/>.</summary>
    public static CollectionRuleInput From(CollectionInstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(
            record.CollectionInstanceId,
            record.EnrollmentId,
            record.StudyId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.OpenedAt,
            record.Items,
            [.. record.Values.Select(v => new CollectionRuleValue(v.ItemId, v.ObservationId, v.Value))],
            record.CollectionTitle);
    }
}

/// <summary>A form instance and everything a form screen shows about it.</summary>
/// <remarks>
/// <para><c>ProtocolVersionId</c>: The protocol version that governs the form (K-TIME-8).</para>
/// <para><c>Version</c>: 1 when opened, one more per save that changed a value.</para>
/// <para><c>Items</c>: The CollectionSpec's items in the governing version, in the specification's order: what the form
/// collects, which items are required and the kind of value each takes. Empty when that version no longer has the
/// specification.</para>
/// <para><c>Values</c>: One per CollectionSpec item that has a value, ordered by item id.</para>
/// <para><c>Locks</c>: The locks in force at the form's study, study site, enrollment, the form itself or one of its
/// values, widest scope first (spec §10.5).</para>
/// <para><c>Assertions</c>: Verifications, queries and answers about the form's values, retracted ones included,
/// oldest first (spec §8.2).</para>
/// <para><c>Attestations</c>: The signatures over the form instance, oldest first (spec §9.5).</para>
/// <para><c>ContentHash</c>: The hash a signature over the form's current values would bind to (#5463); null while the
/// form has no values.</para>
/// <para><c>CollectionTitle</c>: The CollectionSpec's title in the governing protocol version, when present.</para>
/// <para><c>EnrollmentVersion</c>: Current version index of the governing enrollment for lineage linking.</para>
/// <para><c>BlindedItemIds</c> and <c>BlindedObservationIds</c>: Items and observations classified as blinded treatment data.</para>
/// <para><c>ItemCodelists</c>: Hydrated codelist options keyed by item id for items whose terminology declares a codelist.</para>
/// </remarks>
public sealed record CollectionInstanceRecord(
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
    IReadOnlyList<CollectionItem> Items,
    IReadOnlyList<CollectedValueView> Values,
    IReadOnlyList<LockView> Locks,
    IReadOnlyList<AssertionView> Assertions,
    IReadOnlyList<FormAttestationView> Attestations,
    string? ContentHash,
    string? CollectionTitle = null,
    long EnrollmentVersion = 1,
    bool Complete = true,
    IReadOnlyList<string>? MissingRequired = null,
    int OpenQueryCount = 0,
    int AnsweredQueryCount = 0,
    int OpenErrorFindingCount = 0,
    IReadOnlyList<string>? ReadinessBlockers = null,
    IReadOnlySet<string>? BlindedItemIds = null,
    IReadOnlySet<ObservationId>? BlindedObservationIds = null,
    IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>>? ItemCodelists = null,
    DateTimeOffset? EnrollmentReferenceAt = null,
    VisitTimingAssessment? VisitTimingAssessment = null,
    bool VisitTimingEvaluated = false)
{
    private const string MaskedLiteral = "masked";

    /// <summary>True when <paramref name="itemId"/> or <paramref name="observationId"/> is classified as blinded treatment data.</summary>
    public bool IsValueBlinded(string itemId, ObservationId observationId) =>
        (BlindedItemIds?.Contains(itemId) ?? false)
        || (BlindedObservationIds?.Contains(observationId) ?? false);

    /// <summary>True when a query over <paramref name="itemId"/> / <paramref name="observationId"/> targets blinded treatment data.</summary>
    public bool IsQueryBlinded(string itemId, ObservationId observationId) =>
        IsValueBlinded(itemId, observationId);

    /// <summary>Returns a copy with blinded treatment values, signed values, content hashes, and assertions redacted (K-AUTH-4).</summary>
    public CollectionInstanceRecord MaskBlinded()
    {
        var blindedItems = BlindedItemIds ?? FrozenSet<string>.Empty;
        var blindedObservations = BlindedObservationIds ?? FrozenSet<ObservationId>.Empty;
        var effectiveBlindedItems = new HashSet<string>(blindedItems, StringComparer.Ordinal);
        var specBlinded = BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(CollectionSpecId, CollectionTitle);
        foreach (var item in Items)
        {
            if (specBlinded || BlindedTreatmentClassifier.Instance.IsBlindedCollectionItem(CollectionSpecId, CollectionTitle, item))
            {
                effectiveBlindedItems.Add(item.Id);
            }
        }

        foreach (var v in Values)
        {
            if (specBlinded
                || blindedObservations.Contains(v.ObservationId)
                || BlindedTreatmentClassifier.Instance.IsBlindedObservation(
                    v.Concept.ConceptId,
                    v.Value,
                    concept: null,
                    CollectionSpecId,
                    v.ItemId,
                    []))
            {
                effectiveBlindedItems.Add(v.ItemId);
            }
        }

        foreach (var sv in Attestations
            .Where(s => s.SignedValues is not null)
            .SelectMany(s => s.SignedValues!))
        {
            if (specBlinded
                || blindedObservations.Contains(sv.ObservationId)
                || BlindedTreatmentClassifier.Instance.IsBlindedObservation(
                    ConceptId.Empty,
                    sv.Value,
                    concept: null,
                    CollectionSpecId,
                    sv.ItemId,
                    []))
            {
                effectiveBlindedItems.Add(sv.ItemId);
            }
        }

        if (effectiveBlindedItems.Count == 0 && blindedObservations.Count == 0)
        {
            return this;
        }

        var blindedObservationGuids = blindedObservations.Select(o => o.Value).ToHashSet();
        foreach (var v in Values.Where(v => effectiveBlindedItems.Contains(v.ItemId) && !v.ObservationId.IsEmpty))
        {
            blindedObservationGuids.Add(v.ObservationId.Value);
        }

        foreach (var sv in Attestations
            .Where(s => s.SignedValues is not null)
            .SelectMany(s => s.SignedValues!)
            .Where(sv => effectiveBlindedItems.Contains(sv.ItemId) && !sv.ObservationId.IsEmpty))
        {
            blindedObservationGuids.Add(sv.ObservationId.Value);
        }

        var maskedScope = NodeRef.For(CollectionInstanceId);

        var maskedValues = Values
            .Select(v => MaskCollectedValue(v, effectiveBlindedItems, blindedObservationGuids))
            .ToList();

        var blindedQueryIds = Assertions
            .Where(a => a.Kind == AssertionKind.Queried
                && a.Target.Kind == NodeKinds.Observation
                && blindedObservationGuids.Contains(a.Target.Id))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();

        var maskedAssertions = Assertions
            .Select(a => MaskAssertionIfBlinded(a, maskedScope, blindedObservationGuids, blindedQueryIds))
            .ToList();

        var maskedLocks = Locks
            .Select(l => l.Scope.Kind == NodeKinds.Observation && blindedObservationGuids.Contains(l.Scope.Id)
                ? l with
                {
                    Scope = maskedScope,
                    LockedBy = MaskedLiteral,
                    ReasonForChange = MaskedLiteral,
                    Masked = true,
                }
                : l)
            .ToList();

        var maskedAttestations = Attestations
            .Select(sig => sig with
            {
                ContentHash = MaskedLiteral,
                Reason = MaskedLiteral,
                SignedValues = sig.SignedValues?
                    .Select(sv => MaskSignedValue(sv, effectiveBlindedItems, blindedObservationGuids))
                    .ToList(),
            })
            .ToList();

        IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>>? maskedItemCodelists = null;
        if (ItemCodelists is not null)
        {
            var remaining = ItemCodelists
                .Where(kvp => !effectiveBlindedItems.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
            if (remaining.Count > 0)
            {
                maskedItemCodelists = remaining;
            }
        }

        return this with
        {
            Values = maskedValues,
            Locks = maskedLocks,
            Assertions = maskedAssertions,
            Attestations = maskedAttestations,
            ContentHash = null,
            BlindedItemIds = effectiveBlindedItems.ToFrozenSet(StringComparer.Ordinal),
            BlindedObservationIds = FrozenSet<ObservationId>.Empty,
            ItemCodelists = maskedItemCodelists,
        };
    }

    private static CollectedValueView MaskCollectedValue(
        CollectedValueView value,
        HashSet<string> effectiveBlindedItems,
        HashSet<Guid> blindedObservationGuids)
    {
        if (!effectiveBlindedItems.Contains(value.ItemId)
            && (value.ObservationId.IsEmpty || !blindedObservationGuids.Contains(value.ObservationId.Value)))
        {
            return value;
        }

        var maskedValue = value.Value is MissingValue { Reason: NullFlavor.NotAsked }
            ? value.Value
            : new MissingValue(NullFlavor.Masked);
        return value with
        {
            ObservationId = ObservationId.Empty,
            Value = maskedValue,
            RecordedBy = MaskedLiteral,
        };
    }

    private static SignedValueView MaskSignedValue(
        SignedValueView signedValue,
        HashSet<string> effectiveBlindedItems,
        HashSet<Guid> blindedObservationGuids)
    {
        if (!effectiveBlindedItems.Contains(signedValue.ItemId)
            && (signedValue.ObservationId.IsEmpty || !blindedObservationGuids.Contains(signedValue.ObservationId.Value)))
        {
            return signedValue;
        }

        var maskedValue = signedValue.Value is MissingValue { Reason: NullFlavor.NotAsked }
            ? signedValue.Value
            : new MissingValue(NullFlavor.Masked);
        return signedValue with
        {
            ObservationId = ObservationId.Empty,
            Value = maskedValue,
        };
    }

    private static AssertionView MaskAssertionIfBlinded(
        AssertionView assertion,
        NodeRef maskedScope,
        HashSet<Guid> blindedObservationGuids,
        HashSet<Guid> blindedQueryIds)
    {
        var isBlinded = assertion.Masked
            || (assertion.Target.Kind == NodeKinds.Observation && blindedObservationGuids.Contains(assertion.Target.Id))
            || (assertion.Target.Kind == NodeKinds.Assertion && blindedQueryIds.Contains(assertion.Target.Id));
        if (!isBlinded)
        {
            return assertion;
        }

        var maskedText = assertion.Text is null ? null : MaskedLiteral;
        return assertion with
        {
            Target = maskedScope,
            Text = maskedText,
            AssertedBy = MaskedLiteral,
            ReasonForChange = MaskedLiteral,
            Masked = true,
        };
    }
}

/// <summary>Shared completeness and signature readiness rules for form instances.</summary>
public static class CollectionReadiness
{
    /// <summary>True only when required items are recorded, no active queries remain, and no active Error findings remain.</summary>
    public static bool IsComplete(
        int missingRequiredCount,
        int openQueryCount,
        int answeredQueryCount,
        int openErrorFindingCount) =>
        missingRequiredCount == 0
        && openQueryCount == 0
        && answeredQueryCount == 0
        && openErrorFindingCount == 0;

    /// <summary>Formats diagnostic readiness blockers for a form instance.</summary>
    public static IReadOnlyList<string> BuildBlockers(
        IReadOnlyList<string>? missingRequired,
        int missingRequiredCount,
        int openQueryCount,
        int answeredQueryCount,
        int openErrorFindingCount)
    {
        var blockers = new List<string>(4);
        if (missingRequired is { Count: > 0 })
        {
            blockers.Add(missingRequired.Count == 1
                ? $"Required item '{missingRequired[0]}' has no recorded value."
                : $"{missingRequired.Count} required items have no recorded value: {string.Join(", ", missingRequired)}.");
        }
        else if (missingRequiredCount > 0)
        {
            blockers.Add(missingRequiredCount == 1
                ? "1 required item has no recorded value."
                : $"{missingRequiredCount} required items have no recorded value.");
        }

        if (openQueryCount > 0)
        {
            blockers.Add(openQueryCount == 1
                ? "1 open query must be answered and closed."
                : $"{openQueryCount} open queries must be answered and closed.");
        }

        if (answeredQueryCount > 0)
        {
            blockers.Add(answeredQueryCount == 1
                ? "1 answered query is awaiting monitor closure."
                : $"{answeredQueryCount} answered queries are awaiting monitor closure.");
        }

        if (openErrorFindingCount > 0)
        {
            blockers.Add(openErrorFindingCount == 1
                ? "1 active Error validation finding must be resolved, accepted, or explained."
                : $"{openErrorFindingCount} active Error validation findings must be resolved, accepted, or explained.");
        }

        return blockers;
    }
}

/// <summary>One option in a collection item's governed codelist.</summary>
/// <param name="Code">The code recorded in <see cref="CodedValue.Code"/>.</param>
/// <param name="Display">Human-readable label for the code.</param>
/// <param name="SubmissionValue">Optional standards submission value (for example CDISC CT submission value).</param>
public sealed record CodelistOptionView(string Code, string Display, string? SubmissionValue = null);

/// <summary>One value of a form instance.</summary>
/// <remarks>
/// <para><c>Revision</c>: The value's current revision, the expected revision of its next edit.</para>
/// <para><c>RecordedBy</c> and <c>RecordedAt</c>: Who entered the current revision, and when.</para>
/// </remarks>
public sealed record CollectedValueView(
    string ItemId,
    ObservationId ObservationId,
    ConceptRef Concept,
    ObservationValue Value,
    DateTimeOffset ObservedAt,
    int Revision,
    string RecordedBy,
    DateTimeOffset RecordedAt);

/// <summary>A signature over a form instance (spec §9.5).</summary>
/// <remarks>
/// <para><c>ContentHash</c>: The hash of the member revisions it signed; a later correction leaves it bound to them.</para>
/// <para><c>Superseded</c>: True once a correction or a new value changed the form after it was signed: the signature
/// stands for the revisions it signed, not for the form as it is now (#5463).</para>
/// <para><c>SignedValues</c>: The revisions it signed, ordered by item id, rebuilt from the values' history; null in the
/// rare case they cannot be rebuilt to the signed hash (a save recorded at the same instant as the signature).</para>
/// </remarks>
public sealed record FormAttestationView(
    AttestationId AttestationId,
    AttestationMeaning Meaning,
    string SignedBy,
    DateTimeOffset SignedAt,
    string ContentHash,
    string Reason,
    bool Superseded,
    IReadOnlyList<SignedValueView>? SignedValues);

/// <summary>One value as a signature signed it.</summary>
public sealed record SignedValueView(string ItemId, ObservationId ObservationId, int Revision, ObservationValue Value);
