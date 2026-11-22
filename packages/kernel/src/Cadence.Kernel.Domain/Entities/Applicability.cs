using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Bitemporal protocol applicability for one enrollment in a study (spec §7.5, K-TIME-6, K-TIME-8).
/// <para>
/// Answers "which protocol version governs this participant, when?" across valid and recorded time. PostgreSQL enforces
/// single governing version per <c>(tenant_id, study_id, participant_id)</c> at any valid/recorded instant via a
/// <c>btree_gist</c> exclusion constraint and append-only triggers on <c>kernel.applicability_periods</c> (K-TIME-1,
/// K-TIME-2, K-TIME-6).
/// </para>
/// <para>
/// Amendments with <c>RequiresReconsent</c> record bitemporal <see cref="PendingApplicabilityTransition"/> rows that
/// block new data capture until a <see cref="ConsentStatus"/> covering the target version resolves them (PRD-SUB-007).
/// </para>
/// </summary>
public class Applicability : AggregateRoot<ApplicabilityId>
{
    public const string InitialBasis = "InitialEffectivity";
    public const string AutomaticAmendmentBasis = "AutomaticAmendment";
    public const string CohortAmendmentBasis = "CohortAmendment";
    public const string ConsentBasis = "Consent";

    private readonly List<ApplicabilityPeriod> _periods = [];
    private readonly List<PendingApplicabilityTransition> _pendingTransitions = [];

    protected Applicability() { }

    private Applicability(
        ApplicabilityId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        StudySiteId studySiteId)
        : base(id)
    {
        TenantId = tenantId;
        EnrollmentId = enrollmentId;
        ParticipantId = participantId;
        StudyId = studyId;
        StudySiteId = studySiteId;
    }

    public TenantId TenantId { get; private set; }

    public EnrollmentId EnrollmentId { get; private set; }

    public ParticipantId ParticipantId { get; private set; }

    public StudyId StudyId { get; private set; }

    public StudySiteId StudySiteId { get; private set; }

    /// <summary>Current open governing protocol version, or null when closed or unestablished.</summary>
    public ProtocolVersionId? GoverningVersionId { get; private set; }

    /// <summary>When the current governing slice began in valid time, or null when closed or unestablished.</summary>
    public DateTimeOffset? ValidFrom { get; private set; }

    /// <summary>True when a governance tie or discrepancy requires remediation before new capture (K-TIME-6).</summary>
    public bool IsInconsistent { get; private set; }

    public string? InconsistencyReason { get; private set; }

    public IReadOnlyList<ApplicabilityPeriod> Periods => _periods;

    public IReadOnlyList<PendingApplicabilityTransition> PendingTransitions => _pendingTransitions;

    /// <summary>Creates an unestablished applicability record (for example to hold a pending transition or flagged inconsistency).</summary>
    public static Applicability CreateUnestablished(
        ApplicabilityId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        StudySiteId studySiteId) =>
        new(id, tenantId, enrollmentId, participantId, studyId, studySiteId);

    /// <summary>Creates an applicability record with an initial governing protocol version slice.</summary>
    public static CommandResult<Applicability> Establish(
        ApplicabilityId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        StudySiteId studySiteId,
        ProtocolVersionId protocolVersionId,
        string basis,
        DateTimeOffset validFrom,
        DateTimeOffset recordedAt,
        EffectivityId? effectivityId = null,
        ConsentStatusId? consentStatusId = null)
    {
        validFrom = KernelTimestamps.ToStoredPrecision(validFrom);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (validFrom > recordedAt)
        {
            return EffectivityErrors.TransitionTimeInvalid;
        }

        var applicability = new Applicability(id, tenantId, enrollmentId, participantId, studyId, studySiteId);
        applicability.BeginPeriod(protocolVersionId, basis, validFrom, recordedAt, effectivityId, consentStatusId);
        return CommandResult<Applicability>.Success(applicability);
    }

    /// <summary>
    /// Transitions the enrollment to <paramref name="toVersionId"/> at <paramref name="effectiveAt"/>, recorded at
    /// <paramref name="recordedAt"/>, closing overlapping open-recorded periods in recorded time and preserving
    /// non-overlapping historical valid slices (K-TIME-1, K-TIME-2).
    /// </summary>
    public KernelError? TransitionTo(
        ProtocolVersionId toVersionId,
        string basis,
        DateTimeOffset effectiveAt,
        DateTimeOffset recordedAt,
        EffectivityId? effectivityId = null,
        ConsentStatusId? consentStatusId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basis);
        effectiveAt = KernelTimestamps.ToStoredPrecision(effectiveAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (effectiveAt > recordedAt)
        {
            return EffectivityErrors.TransitionTimeInvalid;
        }

        if (GoverningVersionId == toVersionId
            && _periods.SingleOrDefault(p => p.IsCurrent) is { } unchanged
            && effectiveAt >= unchanged.Period.Valid.Start)
        {
            return null;
        }

        var overlapping = _periods
            .Where(p => p.Period.Recorded.IsOpen && p.Period.Valid.Overlaps(TimeRange.From(effectiveAt)))
            .ToList();
        if (overlapping.Any(p => recordedAt <= p.Period.Recorded.Start))
        {
            return KernelErrors.ConcurrencyConflict;
        }

        foreach (var existing in overlapping)
        {
            existing.Supersede(recordedAt);
            if (effectiveAt > existing.Period.Valid.Start)
            {
                var priorEnd = existing.Period.Valid.End is { } bounded && bounded < effectiveAt ? bounded : effectiveAt;
                _periods.Add(new ApplicabilityPeriod(
                    this,
                    existing.ProtocolVersionId,
                    existing.Basis,
                    existing.EffectivityId,
                    existing.ConsentStatusId,
                    new TemporalPeriod(new TimeRange(existing.Period.Valid.Start, priorEnd), TimeRange.From(recordedAt))));
            }
        }

        BeginPeriod(toVersionId, basis, effectiveAt, recordedAt, effectivityId, consentStatusId);
        return null;
    }

    /// <summary>Closes any open-ended valid period at <paramref name="closedAt"/> (for example on screen failure).</summary>
    public KernelError? CloseOpenPeriod(DateTimeOffset closedAt, DateTimeOffset recordedAt)
    {
        closedAt = KernelTimestamps.ToStoredPrecision(closedAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        var openPeriods = _periods.Where(p => p.IsCurrent).ToList();
        if (openPeriods.Count == 0)
        {
            return null;
        }

        if (openPeriods.Any(p => recordedAt <= p.Period.Recorded.Start))
        {
            return KernelErrors.ConcurrencyConflict;
        }

        foreach (var open in openPeriods)
        {
            open.Supersede(recordedAt);
            var clampedEnd = closedAt > open.Period.Valid.Start ? closedAt : open.Period.Valid.Start.AddMicroseconds(1);
            if (closedAt > open.Period.Valid.Start)
            {
                _periods.Add(new ApplicabilityPeriod(
                    this,
                    open.ProtocolVersionId,
                    open.Basis,
                    open.EffectivityId,
                    open.ConsentStatusId,
                    new TemporalPeriod(new TimeRange(open.Period.Valid.Start, clampedEnd), TimeRange.From(recordedAt))));
            }
        }

        foreach (var pending in _pendingTransitions.Where(t => t.IsCurrent).ToList())
        {
            if (recordedAt <= pending.Period.Recorded.Start)
            {
                return KernelErrors.ConcurrencyConflict;
            }

            pending.Supersede(recordedAt);
        }

        GoverningVersionId = null;
        ValidFrom = null;
        return null;
    }

    /// <summary>Records a pending re-consent transition without advancing the current governing version (PRD-SUB-007).</summary>
    public KernelError? HoldForReconsent(
        ProtocolVersionId targetVersionId,
        EffectivityId effectivityId,
        DateTimeOffset requiredSince,
        DateTimeOffset recordedAt)
    {
        requiredSince = KernelTimestamps.ToStoredPrecision(requiredSince);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (requiredSince > recordedAt)
        {
            return EffectivityErrors.TransitionTimeInvalid;
        }

        if (_pendingTransitions.Any(t => t.IsCurrent && t.TargetVersionId == targetVersionId))
        {
            return null;
        }

        _pendingTransitions.Add(new PendingApplicabilityTransition(
            this,
            GoverningVersionId,
            targetVersionId,
            effectivityId,
            requiredSince,
            new TemporalPeriod(TimeRange.From(requiredSince), TimeRange.From(recordedAt))));
        return null;
    }

    /// <summary>
    /// Resolves every open pending transition whose <c>TargetVersionId</c> is in <paramref name="coveredVersions"/> as of
    /// <paramref name="resolvedAt"/>, recorded at <paramref name="recordedAt"/> (spec §7.5: a newer consent resolves every
    /// pending transition whose target version it covers).
    /// </summary>
    public KernelError? ResolvePendingTransitions(
        IReadOnlySet<ProtocolVersionId> coveredVersions,
        DateTimeOffset resolvedAt,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(coveredVersions);
        resolvedAt = KernelTimestamps.ToStoredPrecision(resolvedAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);

        var matching = _pendingTransitions
            .Where(t => t.IsCurrent && coveredVersions.Contains(t.TargetVersionId))
            .ToList();
        if (matching.Count == 0)
        {
            return null;
        }

        if (matching.Any(t => recordedAt <= t.Period.Recorded.Start))
        {
            return KernelErrors.ConcurrencyConflict;
        }

        foreach (var pending in matching)
        {
            pending.Supersede(recordedAt);
            if (resolvedAt > pending.Period.Valid.Start)
            {
                _pendingTransitions.Add(new PendingApplicabilityTransition(
                    this,
                    pending.FromVersionId,
                    pending.TargetVersionId,
                    pending.EffectivityId,
                    pending.RequiredSince,
                    new TemporalPeriod(new TimeRange(pending.Period.Valid.Start, resolvedAt), TimeRange.From(recordedAt))));
            }
        }

        return null;
    }

    /// <summary>Flags or clears a K-TIME-6 applicability inconsistency.</summary>
    public void SetInconsistency(bool isInconsistent, string? reason)
    {
        IsInconsistent = isInconsistent;
        InconsistencyReason = isInconsistent ? reason?.Trim() : null;
    }

    /// <summary>The governing protocol version at <paramref name="validAt"/> as known at <paramref name="knownAt"/>.</summary>
    public ApplicabilityPeriod? PeriodAsOf(DateTimeOffset validAt, DateTimeOffset knownAt) =>
        _periods.SingleOrDefault(p => p.Period.IsEffectiveAsOf(validAt, knownAt));

    /// <summary>Pending re-consent transitions active at <paramref name="validAt"/> as known at <paramref name="knownAt"/>.</summary>
    public IReadOnlyList<PendingApplicabilityTransition> PendingAsOf(DateTimeOffset validAt, DateTimeOffset knownAt) =>
        [.. _pendingTransitions.Where(t => t.Period.IsEffectiveAsOf(validAt, knownAt)).OrderBy(t => t.RequiredSince)];

    /// <summary>Currently unresolved re-consent transitions in open recorded time.</summary>
    public IReadOnlyList<PendingApplicabilityTransition> CurrentPendingTransitions() =>
        [.. _pendingTransitions.Where(t => t.IsCurrent).OrderBy(t => t.RequiredSince)];

    private void BeginPeriod(
        ProtocolVersionId protocolVersionId,
        string basis,
        DateTimeOffset validFrom,
        DateTimeOffset recordedAt,
        EffectivityId? effectivityId,
        ConsentStatusId? consentStatusId)
    {
        _periods.Add(new ApplicabilityPeriod(
            this,
            protocolVersionId,
            basis,
            effectivityId,
            consentStatusId,
            new TemporalPeriod(TimeRange.From(validFrom), TimeRange.From(recordedAt))));
        GoverningVersionId = protocolVersionId;
        ValidFrom = validFrom;
    }
}

/// <summary>
/// One bitemporal governing-version slice in <c>kernel.applicability_periods</c> (spec §7.5, K-TIME-1, K-TIME-2, K-TIME-6).
/// Keyed by <c>(tenant_id, study_id, participant_id)</c> in the bitemporal exclusion constraint so a participant has at
/// most one governing protocol version in a study at any valid and recorded instant.
/// </summary>
public class ApplicabilityPeriod : IBitemporalEntity
{
    protected ApplicabilityPeriod() { }

    internal ApplicabilityPeriod(
        Applicability applicability,
        ProtocolVersionId protocolVersionId,
        string basis,
        EffectivityId? effectivityId,
        ConsentStatusId? consentStatusId,
        TemporalPeriod period)
    {
        Id = Guid.CreateVersion7(period.Recorded.Start);
        TenantId = applicability.TenantId;
        ApplicabilityId = applicability.Id;
        EnrollmentId = applicability.EnrollmentId;
        ParticipantId = applicability.ParticipantId;
        StudyId = applicability.StudyId;
        ProtocolVersionId = protocolVersionId;
        Basis = basis;
        EffectivityId = effectivityId;
        ConsentStatusId = consentStatusId;
        Period = period;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public ApplicabilityId ApplicabilityId { get; private set; }

    public EnrollmentId EnrollmentId { get; private set; }

    public ParticipantId ParticipantId { get; private set; }

    public StudyId StudyId { get; private set; }

    public ProtocolVersionId ProtocolVersionId { get; private set; }

    public string Basis { get; private set; } = string.Empty;

    public EffectivityId? EffectivityId { get; private set; }

    public ConsentStatusId? ConsentStatusId { get; private set; }

    public TemporalPeriod Period { get; private set; }

    internal bool IsCurrent => Period.Recorded.IsOpen && Period.Valid.IsOpen;

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}

/// <summary>
/// One bitemporal pending re-consent transition in <c>kernel.applicability_pending_transitions</c> (spec §7.5, PRD-SUB-007).
/// </summary>
public class PendingApplicabilityTransition : IBitemporalEntity
{
    protected PendingApplicabilityTransition() { }

    internal PendingApplicabilityTransition(
        Applicability applicability,
        ProtocolVersionId? fromVersionId,
        ProtocolVersionId targetVersionId,
        EffectivityId effectivityId,
        DateTimeOffset requiredSince,
        TemporalPeriod period)
    {
        Id = Guid.CreateVersion7(period.Recorded.Start);
        TenantId = applicability.TenantId;
        ApplicabilityId = applicability.Id;
        EnrollmentId = applicability.EnrollmentId;
        FromVersionId = fromVersionId;
        TargetVersionId = targetVersionId;
        EffectivityId = effectivityId;
        RequiredSince = requiredSince;
        Period = period;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public ApplicabilityId ApplicabilityId { get; private set; }

    public EnrollmentId EnrollmentId { get; private set; }

    public ProtocolVersionId? FromVersionId { get; private set; }

    public ProtocolVersionId TargetVersionId { get; private set; }

    public EffectivityId EffectivityId { get; private set; }

    public DateTimeOffset RequiredSince { get; private set; }

    public TemporalPeriod Period { get; private set; }

    internal bool IsCurrent => Period.Recorded.IsOpen && Period.Valid.IsOpen;

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}
