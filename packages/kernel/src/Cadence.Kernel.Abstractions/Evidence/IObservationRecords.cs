using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// Reads an observation with its correction history and its audit trail (spec §8.1, ADR-2620). Reads are tenant-scoped
/// by Row-Level Security and never change state. Kernel persistence implements it (<c>AddKernelPersistence</c>).
/// </summary>
public interface IObservationRecords
{
    /// <summary>The observation, or null when it does not exist in <paramref name="tenant"/>.</summary>
    Task<ObservationRecord?> FindAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken);

    /// <summary>
    /// Reads <paramref name="observation"/> for a caller whose unblinding status is <paramref name="unblinded"/>,
    /// masking blinded current values, historical revisions, assertions, locks, audit reasons, and observation handles
    /// before returning (K-AUTH-4).
    /// </summary>
    async Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        bool unblinded,
        CancellationToken cancellationToken)
    {
        var record = await FindAsync(tenant, observation, cancellationToken).ConfigureAwait(false);
        if (record is null || unblinded)
        {
            return record;
        }

        var isBlinded = record.IsBlindedTreatment
            || BlindedTreatmentClassifier.Instance.IsBlindedObservation(
                record.Concept.ConceptId,
                record.Value,
                concept: null,
                collectionSpecId: null,
                record.ItemId,
                []);
        return isBlinded ? record.MaskBlinded() : record;
    }

    /// <summary>
    /// Reads <paramref name="observation"/> and evaluates <paramref name="canUnblindAsync"/> against its enrollment
    /// before returning so blinded observations are masked inside the port when the caller lacks <c>enrollment.unblind</c>.
    /// </summary>
    async Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canUnblindAsync);
        var record = await FindAsync(tenant, observation, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var isBlinded = record.IsBlindedTreatment
            || BlindedTreatmentClassifier.Instance.IsBlindedObservation(
                record.Concept.ConceptId,
                record.Value,
                concept: null,
                collectionSpecId: null,
                record.ItemId,
                []);
        if (!isBlinded)
        {
            return record;
        }

        var unblinded = await canUnblindAsync(record.EnrollmentId, cancellationToken).ConfigureAwait(false);
        return unblinded ? record : record.MaskBlinded();
    }

    /// <summary>
    /// Reads <paramref name="observation"/> and evaluates unblinding authority against the ambient caller when available,
    /// failing closed (masked) when no unblinding probe permits disclosure.
    /// </summary>
    Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        CancellationToken cancellationToken) =>
        FindForReaderAsync(tenant, observation, unblinded: false, cancellationToken);

    /// <summary>
    /// Reads a narrow batched projection of candidate <paramref name="observations"/> whose latest revision does not yet
    /// carry an active (un-retracted) <c>Verified</c> assertion, omitting blinded treatment observations when
    /// <paramref name="canUnblindAsync"/> denies <c>enrollment.unblind</c> for their enrollment (<c>K-AUTH-4</c>).
    /// </summary>
    /// <param name="tenant">The caller's tenant.</param>
    /// <param name="observations">Candidate observation identifiers to evaluate.</param>
    /// <param name="canUnblindAsync">Callback evaluated at most once per distinct enrollment for blinded observations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    async Task<IReadOnlyDictionary<ObservationId, ObservationSdvAlertTarget>> FindUnverifiedSdvTargetsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(canUnblindAsync);
        if (observations.Count == 0)
        {
            return new Dictionary<ObservationId, ObservationSdvAlertTarget>();
        }

        var unblindCache = new Dictionary<EnrollmentId, bool>();
        async ValueTask<bool> CanUnblindOnceAsync(EnrollmentId enrollment, CancellationToken ct)
        {
            if (unblindCache.TryGetValue(enrollment, out var cached))
            {
                return cached;
            }

            var permitted = await canUnblindAsync(enrollment, ct).ConfigureAwait(false);
            unblindCache[enrollment] = permitted;
            return permitted;
        }

        var result = new Dictionary<ObservationId, ObservationSdvAlertTarget>();
        foreach (var observationId in observations)
        {
            if (observationId.IsEmpty || result.ContainsKey(observationId))
            {
                continue;
            }

            var obs = await FindForReaderAsync(tenant, observationId, CanUnblindOnceAsync, cancellationToken).ConfigureAwait(false);
            if (obs is null || (obs.IsBlindedTreatment && obs.ObservationId.IsEmpty))
            {
                continue;
            }

            int currentRevision = obs.Revisions.Count > 0 ? obs.Revisions[^1].Number : (int)obs.Version;
            bool latestVerified = false;
            for (var i = 0; i < obs.Assertions.Count; i++)
            {
                var assertion = obs.Assertions[i];
                if (assertion.Kind == AssertionKind.Verified
                    && assertion.Revision == currentRevision
                    && assertion.RetractedBy is null)
                {
                    latestVerified = true;
                    break;
                }
            }

            if (latestVerified)
            {
                continue;
            }

            var latestRev = obs.Revisions.Count > 0 ? obs.Revisions[^1] : null;
            result[observationId] = new ObservationSdvAlertTarget(
                obs.ObservationId,
                obs.StudyId,
                obs.EnrollmentId,
                obs.CollectionInstanceId,
                obs.ItemId,
                currentRevision,
                latestRev?.RecordedBy ?? string.Empty,
                latestRev?.RecordedAt ?? obs.ObservedAt,
                latestRev?.ReasonForChange ?? string.Empty,
                obs.IsBlindedTreatment);
        }

        return result;
    }
}

/// <summary>
/// Narrow batched projection of an unverified observation used when evaluating active SDV modification alerts without
/// hydrating full audit chains, codelists, or superseded revisions (<c>PRD-QRY-006</c>).
/// </summary>
/// <param name="ObservationId">The observation identifier.</param>
/// <param name="StudyId">The owning study identifier.</param>
/// <param name="EnrollmentId">The owning enrollment identifier.</param>
/// <param name="CollectionInstanceId">Optional form (collection instance) containing the observation.</param>
/// <param name="ItemId">Optional form item identifier.</param>
/// <param name="CurrentRevision">Current (latest) revision number of the observation.</param>
/// <param name="LatestRecordedBy">Actor who recorded <paramref name="CurrentRevision"/>.</param>
/// <param name="LatestRecordedAt">UTC timestamp when <paramref name="CurrentRevision"/> was recorded.</param>
/// <param name="LatestReasonForChange">Reason for change recorded on <paramref name="CurrentRevision"/>.</param>
/// <param name="IsBlindedTreatment">True when classified as a blinded treatment observation (<c>K-AUTH-4</c>).</param>
public sealed record ObservationSdvAlertTarget(
    ObservationId ObservationId,
    StudyId StudyId,
    EnrollmentId EnrollmentId,
    CollectionInstanceId? CollectionInstanceId,
    string? ItemId,
    int CurrentRevision,
    string LatestRecordedBy,
    DateTimeOffset LatestRecordedAt,
    string LatestReasonForChange,
    bool IsBlindedTreatment = false);

/// <summary>
/// An observation's current value, every revision, the locks that cover it, the assertions about it and its kernel
/// audit entries.
/// </summary>
/// <remarks>
/// <para><c>Version</c>: The version a correction names as expected.</para>
/// <para><c>Locks</c>: The locks in force at the observation's study, study site, enrollment, form instance or the
/// observation itself, widest scope first (spec §10.5). Empty when nothing covers it.</para>
/// <para><c>Revisions</c>: Oldest first. Each is recorded until the next one is recorded (K-FACT-1).</para>
/// <para><c>Assertions</c>: Verifications, queries and answers about the observation, retracted ones included, oldest
/// first (spec §8.2).</para>
/// <para><c>Audit</c>: The kernel audit-chain entries about the observation and the assertions covering it, oldest
/// first (spec §8.2).</para>
/// </remarks>
public sealed record ObservationRecord(
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ConceptRef Concept,
    ObservationValue Value,
    DateTimeOffset ObservedAt,
    long Version,
    IReadOnlyList<LockView> Locks,
    IReadOnlyList<ObservationRevisionView> Revisions,
    IReadOnlyList<AssertionView> Assertions,
    IReadOnlyList<ObservationAuditView> Audit,
    CollectionInstanceId? CollectionInstanceId = null,
    string? ItemId = null,
    ObservationValueKind? ExpectedValueKind = null,
    string? ExpectedUnit = null,
    CodeSystemVersionId? ExpectedCodeSystemVersionId = null,
    string? ExpectedCodelist = null,
    IReadOnlyList<CodelistOptionView>? ExpectedCodelistOptions = null,
    bool IsBlindedTreatment = false)
{
    private const string MaskedLiteral = "masked";

    /// <summary>
    /// Returns a copy with current values, revisions, assertions, observation-scoped locks, audit details, and the
    /// direct observation identifier redacted in place (K-AUTH-4).
    /// </summary>
    public ObservationRecord MaskBlinded()
    {
        var maskedScope = CollectionInstanceId is { IsEmpty: false } formId
            ? NodeRef.For(formId)
            : NodeRef.For(EnrollmentId);
        var maskedValue = Value is MissingValue { Reason: NullFlavor.NotAsked }
            ? Value
            : new MissingValue(NullFlavor.Masked);
        var maskedRevisions = new List<ObservationRevisionView>(Revisions.Count);
        foreach (var rev in Revisions)
        {
            var revValue = rev.Value is MissingValue { Reason: NullFlavor.NotAsked }
                ? rev.Value
                : new MissingValue(NullFlavor.Masked);
            maskedRevisions.Add(rev with
            {
                Value = revValue,
                ReasonForChange = MaskedLiteral,
                RecordedBy = MaskedLiteral,
            });
        }

        var maskedLocks = new List<LockView>(Locks.Count);
        foreach (var l in Locks)
        {
            if (l.Scope.Kind == NodeKinds.Observation)
            {
                maskedLocks.Add(l with
                {
                    Scope = maskedScope,
                    LockedBy = MaskedLiteral,
                    ReasonForChange = MaskedLiteral,
                    Masked = true,
                });
            }
            else
            {
                maskedLocks.Add(l);
            }
        }

        var maskedAssertions = new List<AssertionView>(Assertions.Count);
        foreach (var a in Assertions)
        {
            maskedAssertions.Add(a with
            {
                Target = maskedScope,
                Text = a.Text is null ? null : MaskedLiteral,
                AssertedBy = MaskedLiteral,
                ReasonForChange = MaskedLiteral,
                Masked = true,
            });
        }

        var maskedAudit = new List<ObservationAuditView>(Audit.Count);
        foreach (var entry in Audit)
        {
            maskedAudit.Add(entry with
            {
                ActorId = MaskedLiteral,
                Subject = MaskedLiteral,
                ReasonForChange = MaskedLiteral,
            });
        }

        return this with
        {
            ObservationId = ObservationId.Empty,
            Value = maskedValue,
            Locks = maskedLocks,
            Revisions = maskedRevisions,
            Assertions = maskedAssertions,
            Audit = maskedAudit,
            ExpectedCodeSystemVersionId = null,
            ExpectedCodelist = null,
            ExpectedCodelistOptions = null,
            IsBlindedTreatment = true,
        };
    }
}

/// <summary>One revision of an observation's value.</summary>
/// <remarks>
/// <para><c>RecordedUntil</c>: When the next revision superseded it; null for the current one.</para>
/// </remarks>
public sealed record ObservationRevisionView(
    int Number,
    ObservationValue Value,
    string ReasonForChange,
    string RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? RecordedUntil);

/// <summary>A lock in force (spec §10.5).</summary>
/// <remarks>
/// <para><c>Scope</c>: The study, study site, enrollment, form instance or observation it covers.</para>
/// <para><c>AttestationId</c>: The signature it was applied with.</para>
/// </remarks>
public sealed record LockView(
    AssertionId LockId,
    LockKind Kind,
    NodeRef Scope,
    AttestationId AttestationId,
    DateTimeOffset LockedAt,
    string LockedBy,
    string ReasonForChange,
    bool Masked = false);

/// <summary>One assertion about an observation, or about another assertion about it (spec §8.2).</summary>
/// <remarks>
/// <para><c>Target</c>: The observation, or the query an answer answers.</para>
/// <para><c>Revision</c>: The revision a verification judged; null for a query or an answer.</para>
/// <para><c>Text</c>: A query's or answer's text.</para>
/// <para><c>RetractedBy</c>: The assertion that retracted it, or null while it holds.</para>
/// </remarks>
public sealed record AssertionView(
    AssertionId AssertionId,
    AssertionKind Kind,
    NodeRef Target,
    int? Revision,
    string? Text,
    string AssertedBy,
    DateTimeOffset AssertedAt,
    string ReasonForChange,
    AssertionId? RetractedBy,
    DateTimeOffset? RetractedAt,
    bool Masked = false);

/// <summary>One kernel audit-chain entry about an observation or an assertion covering it.</summary>
/// <remarks>
/// <para><c>Sequence</c>: The entry's position in the tenant's hash chain.</para>
/// <para><c>Subject</c>: <c>Observation</c> or <c>Assertion</c>.</para>
/// </remarks>
public sealed record ObservationAuditView(
    long Sequence,
    DateTimeOffset RecordedAt,
    string ActorId,
    string Action,
    string Subject,
    long? Version,
    string ReasonForChange);
