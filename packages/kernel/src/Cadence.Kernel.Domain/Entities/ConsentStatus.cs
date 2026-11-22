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
/// Computable bitemporal consent for one enrollment and protocol version (spec §9.4, K-CONS-1, K-CONS-2).
/// <para>
/// Paper consent and electronic consent share the same bitemporal representation without override flags (spec §9.4):
/// <see cref="ConsentBasisKind.ElectronicAttestation"/> or <see cref="ConsentBasisKind.PaperSourceDocument"/>, optionally
/// referencing the kernel <see cref="AttestationId"/> and ICF/source <see cref="ArtifactVersionId"/>.
/// </para>
/// </summary>
public class ConsentStatus : AggregateRoot<ConsentStatusId>
{
    private readonly List<ConsentStatusPeriod> _periods = [];

    protected ConsentStatus() { }

    private ConsentStatus(
        ConsentStatusId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        StudySiteId studySiteId,
        ProtocolVersionId protocolVersionId)
        : base(id)
    {
        TenantId = tenantId;
        EnrollmentId = enrollmentId;
        ParticipantId = participantId;
        StudyId = studyId;
        StudySiteId = studySiteId;
        ProtocolVersionId = protocolVersionId;
    }

    public TenantId TenantId { get; private set; }

    public EnrollmentId EnrollmentId { get; private set; }

    public ParticipantId ParticipantId { get; private set; }

    public StudyId StudyId { get; private set; }

    public StudySiteId StudySiteId { get; private set; }

    public ProtocolVersionId ProtocolVersionId { get; private set; }

    public bool Signed { get; private set; }

    public DateTimeOffset SignedAt { get; private set; }

    public ConsentBasisKind BasisKind { get; private set; }

    public AttestationId? AttestationId { get; private set; }

    public ArtifactVersionId? ArtifactVersionId { get; private set; }

    public IReadOnlyList<ConsentStatusPeriod> Periods => _periods;

    public IReadOnlyList<ConsentScopeEntry> CurrentScopes() =>
        _periods.SingleOrDefault(p => p.IsCurrent)?.Scopes ?? [];

    public static CommandResult<ConsentStatus> Record(
        ConsentStatusId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        StudySiteId studySiteId,
        ProtocolVersionId protocolVersionId,
        bool signed,
        DateTimeOffset signedAt,
        ConsentBasisKind basisKind,
        IReadOnlyCollection<ConsentScopeEntry> scopes,
        DateTimeOffset recordedAt,
        AttestationId? attestationId = null,
        ArtifactVersionId? artifactVersionId = null)
    {
        if (ValidateScopes(scopes) is { } invalidScopes)
        {
            return invalidScopes;
        }

        signedAt = KernelTimestamps.ToStoredPrecision(signedAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (signedAt > recordedAt)
        {
            return ConsentErrors.SignedAtInvalid;
        }

        var status = new ConsentStatus(id, tenantId, enrollmentId, participantId, studyId, studySiteId, protocolVersionId);
        status.BeginPeriod(signed, signedAt, basisKind, [.. scopes], recordedAt, attestationId, artifactVersionId);
        return CommandResult<ConsentStatus>.Success(status);
    }

    /// <summary>Updates consent bitemporally for the same enrollment and protocol version.</summary>
    public KernelError? Update(
        bool signed,
        DateTimeOffset effectiveAt,
        ConsentBasisKind basisKind,
        IReadOnlyCollection<ConsentScopeEntry> scopes,
        DateTimeOffset recordedAt,
        AttestationId? attestationId = null,
        ArtifactVersionId? artifactVersionId = null)
    {
        if (ValidateScopes(scopes) is { } invalidScopes)
        {
            return invalidScopes;
        }

        effectiveAt = KernelTimestamps.ToStoredPrecision(effectiveAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (effectiveAt > recordedAt)
        {
            return ConsentErrors.SignedAtInvalid;
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
                _periods.Add(new ConsentStatusPeriod(
                    this,
                    existing.Signed,
                    existing.BasisKind,
                    existing.AttestationId,
                    existing.ArtifactVersionId,
                    existing.Scopes,
                    new TemporalPeriod(new TimeRange(existing.Period.Valid.Start, priorEnd), TimeRange.From(recordedAt))));
            }
        }

        BeginPeriod(signed, effectiveAt, basisKind, [.. scopes], recordedAt, attestationId, artifactVersionId);
        return null;
    }

    public ConsentStatusPeriod? PeriodAsOf(DateTimeOffset validAt, DateTimeOffset knownAt) =>
        _periods.SingleOrDefault(p => p.Period.IsEffectiveAsOf(validAt, knownAt));

    private static KernelError? ValidateScopes(IReadOnlyCollection<ConsentScopeEntry>? scopes)
    {
        if (scopes is null || scopes.Count == 0 || scopes.Any(s => s is null || string.IsNullOrWhiteSpace(s.Purpose) || !Enum.IsDefined(s.Decision)))
        {
            return ConsentErrors.ScopesInvalid;
        }

        return null;
    }

    private void BeginPeriod(
        bool signed,
        DateTimeOffset signedAt,
        ConsentBasisKind basisKind,
        IReadOnlyList<ConsentScopeEntry> scopes,
        DateTimeOffset recordedAt,
        AttestationId? attestationId,
        ArtifactVersionId? artifactVersionId)
    {
        _periods.Add(new ConsentStatusPeriod(
            this,
            signed,
            basisKind,
            attestationId,
            artifactVersionId,
            scopes,
            new TemporalPeriod(TimeRange.From(signedAt), TimeRange.From(recordedAt))));
        Signed = signed;
        SignedAt = signedAt;
        BasisKind = basisKind;
        AttestationId = attestationId;
        ArtifactVersionId = artifactVersionId;
    }
}

/// <summary>
/// One bitemporal consent state slice in <c>kernel.consent_status_periods</c> (spec §9.4, K-TIME-1, K-TIME-2).
/// </summary>
public class ConsentStatusPeriod : IBitemporalEntity
{
    protected ConsentStatusPeriod() { }

    internal ConsentStatusPeriod(
        ConsentStatus consentStatus,
        bool signed,
        ConsentBasisKind basisKind,
        AttestationId? attestationId,
        ArtifactVersionId? artifactVersionId,
        IReadOnlyList<ConsentScopeEntry> scopes,
        TemporalPeriod period)
    {
        Id = Guid.CreateVersion7(period.Recorded.Start);
        TenantId = consentStatus.TenantId;
        ConsentStatusId = consentStatus.Id;
        EnrollmentId = consentStatus.EnrollmentId;
        ProtocolVersionId = consentStatus.ProtocolVersionId;
        Signed = signed;
        BasisKind = basisKind;
        AttestationId = attestationId;
        ArtifactVersionId = artifactVersionId;
        Scopes = scopes;
        Period = period;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public ConsentStatusId ConsentStatusId { get; private set; }

    public EnrollmentId EnrollmentId { get; private set; }

    public ProtocolVersionId ProtocolVersionId { get; private set; }

    public bool Signed { get; private set; }

    public ConsentBasisKind BasisKind { get; private set; }

    public AttestationId? AttestationId { get; private set; }

    public ArtifactVersionId? ArtifactVersionId { get; private set; }

    public IReadOnlyList<ConsentScopeEntry> Scopes { get; private set; } = [];

    public TemporalPeriod Period { get; private set; }

    internal bool IsCurrent => Period.Recorded.IsOpen && Period.Valid.IsOpen;

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}
