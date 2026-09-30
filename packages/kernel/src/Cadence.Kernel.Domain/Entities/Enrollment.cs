using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Execution;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// One participant's membership of one study site (spec §7.4). The status history is bitemporal: each change closes the
/// recorded period of the current <see cref="EnrollmentStatusPeriod"/> and records what was true before and after the
/// change, so the enrollment can be read as of any valid and known instant. <see cref="Status"/> and
/// <see cref="StatusSince"/> repeat the current row for queries and the audit trail.
/// <list type="bullet">
/// <item>Transitions follow <see cref="EnrollmentTransitions"/>; a change happens after the current status began and
/// not after it is recorded (K-TIME-9: valid time is the trial's, recorded time is the kernel's).</item>
/// <item>Entering Enrolled sets the <see cref="EnrollmentNumber"/> the handler allocated under the study lock.</item>
/// <item>Withdrawal records its kind and the attestation that authorized it.</item>
/// </list>
/// Expected failures return a <see cref="KernelError"/>; nothing here throws for them.
/// </summary>
public class Enrollment : AggregateRoot<EnrollmentId>
{
    private static readonly JsonSerializerOptions HashJson = new() { WriteIndented = false };

    private readonly List<EnrollmentStatusPeriod> _periods = [];

    protected Enrollment() { }

    private Enrollment(
        EnrollmentId id,
        TenantId tenantId,
        ParticipantId participantId,
        StudyId studyId,
        StudySiteId studySiteId,
        SubjectCode subjectCode)
        : base(id)
    {
        TenantId = tenantId;
        ParticipantId = participantId;
        StudyId = studyId;
        StudySiteId = studySiteId;
        SubjectCode = subjectCode;
    }

    public TenantId TenantId { get; private set; }

    public ParticipantId ParticipantId { get; private set; }

    /// <summary>The study of <see cref="StudySiteId"/>, repeated so codes and numbers are unique per study.</summary>
    public StudyId StudyId { get; private set; }

    public StudySiteId StudySiteId { get; private set; }

    /// <summary>The participant's code in the study, unique per study (spec §3.2).</summary>
    public SubjectCode SubjectCode { get; private set; }

    /// <summary>The status now: the status of the current open row in <see cref="Periods"/>.</summary>
    public EnrollmentStatus Status { get; private set; }

    /// <summary>When the current status began, in valid time.</summary>
    public DateTimeOffset StatusSince { get; private set; }

    /// <summary>The gap-free number in the study, set on entering Enrolled; null before.</summary>
    public int? EnrollmentNumber { get; private set; }

    public WithdrawalKind? WithdrawalKind { get; private set; }

    public AttestationId? WithdrawalAttestationId { get; private set; }

    /// <summary>Every bitemporal status row of this enrollment, including superseded ones.</summary>
    public IReadOnlyList<EnrollmentStatusPeriod> Periods => _periods;

    /// <summary>
    /// The hash an attestation about this enrollment signs: the content hash of its identity, code, status, status start
    /// and number. Every change of status changes it, so a signature given before a change no longer matches.
    /// </summary>
    public string ContentHash => Trust.ContentHash.Compute(JsonSerializer.Serialize(
        new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["enrollment_id"] = Id.ToString(),
            ["enrollment_number"] = EnrollmentNumber,
            ["participant_id"] = ParticipantId.ToString(),
            ["status"] = Status.ToString(),
            ["status_since"] = StatusSince.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
            ["study_id"] = StudyId.ToString(),
            ["study_site_id"] = StudySiteId.ToString(),
            ["subject_code"] = SubjectCode.Value,
            ["tenant_id"] = TenantId.ToString(),
        },
        HashJson));

    /// <summary>What an attestation about the enrollment's current state signs.</summary>
    public AttestationTarget SignableTarget => new(NodeKinds.Enrollment, Id.Value, ContentHash);

    /// <summary>
    /// Starts an enrollment in <see cref="EnrollmentStatus.Screening"/> from <paramref name="screenedAt"/>, recorded
    /// at <paramref name="recordedAt"/>.
    /// </summary>
    public static CommandResult<Enrollment> Screen(
        EnrollmentId id,
        TenantId tenantId,
        ParticipantId participantId,
        StudyId studyId,
        StudySiteId studySiteId,
        SubjectCode subjectCode,
        DateTimeOffset screenedAt,
        DateTimeOffset recordedAt)
    {
        if (!subjectCode.IsValid)
        {
            return ExecutionErrors.SubjectCodeInvalid;
        }

        screenedAt = KernelTimestamps.ToStoredPrecision(screenedAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (screenedAt > recordedAt)
        {
            return ExecutionErrors.TransitionTimeInvalid;
        }

        var enrollment = new Enrollment(id, tenantId, participantId, studyId, studySiteId, subjectCode);
        enrollment.Begin(EnrollmentStatus.Screening, screenedAt, recordedAt);
        return CommandResult<Enrollment>.Success(enrollment);
    }

    /// <summary>
    /// Why the enrollment cannot move to <paramref name="to"/> at <paramref name="effectiveAt"/> when recorded at
    /// <paramref name="recordedAt"/>, or null when it can. Handlers check this before allocating a number.
    /// </summary>
    public KernelError? CheckTransition(EnrollmentStatus to, DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
    {
        if (!Enum.IsDefined(to) || !EnrollmentTransitions.IsAllowed(Status, to))
        {
            return ExecutionErrors.InvalidTransition.WithMessage($"An enrollment in {Status} cannot move to {to}.");
        }

        effectiveAt = KernelTimestamps.ToStoredPrecision(effectiveAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (effectiveAt <= StatusSince || effectiveAt > recordedAt)
        {
            return ExecutionErrors.TransitionTimeInvalid;
        }

        // Two changes recorded at the same instant would leave an empty recorded period; the second one retries.
        return recordedAt <= CurrentPeriod().Period.Recorded.Start ? KernelErrors.ConcurrencyConflict : null;
    }

    /// <summary>
    /// Moves the enrollment to <paramref name="to"/>. Entering Enrolled needs <paramref name="enrollmentNumber"/>;
    /// withdrawal goes through <see cref="Withdraw"/>.
    /// </summary>
    public KernelError? ChangeStatus(EnrollmentStatus to, DateTimeOffset effectiveAt, DateTimeOffset recordedAt, int? enrollmentNumber = null)
    {
        if (to == EnrollmentStatus.Withdrawn)
        {
            return ExecutionErrors.WithdrawalRequiresCommand;
        }

        if (CheckTransition(to, effectiveAt, recordedAt) is { } invalid)
        {
            return invalid;
        }

        if ((to == EnrollmentStatus.Enrolled) != enrollmentNumber.HasValue || enrollmentNumber <= 0)
        {
            throw new ArgumentException("Entering Enrolled needs a positive enrollment number, and no other status takes one.", nameof(enrollmentNumber));
        }

        Move(to, KernelTimestamps.ToStoredPrecision(effectiveAt), KernelTimestamps.ToStoredPrecision(recordedAt));
        if (enrollmentNumber is { } number)
        {
            EnrollmentNumber = number;
        }

        return null;
    }

    /// <summary>Withdraws an active enrollment; the handler has verified <paramref name="attestationId"/>.</summary>
    public KernelError? Withdraw(WithdrawalKind kind, AttestationId attestationId, DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
    {
        if (!Enum.IsDefined(kind))
        {
            return ExecutionErrors.WithdrawalKindInvalid;
        }

        if (CheckTransition(EnrollmentStatus.Withdrawn, effectiveAt, recordedAt) is { } invalid)
        {
            return invalid;
        }

        Move(EnrollmentStatus.Withdrawn, KernelTimestamps.ToStoredPrecision(effectiveAt), KernelTimestamps.ToStoredPrecision(recordedAt));
        WithdrawalKind = kind;
        WithdrawalAttestationId = attestationId;
        return null;
    }

    /// <summary>The status at <paramref name="validAt"/> as known at <paramref name="knownAt"/>, or null before screening.</summary>
    public EnrollmentStatus? StatusAsOf(DateTimeOffset validAt, DateTimeOffset knownAt) =>
        _periods.SingleOrDefault(p => p.Period.IsEffectiveAsOf(validAt, knownAt))?.Status;

    private EnrollmentStatusPeriod CurrentPeriod() =>
        _periods.SingleOrDefault(p => p.IsCurrent)
        ?? throw new InvalidOperationException($"Enrollment {Id} has no current status row; load it with its periods.");

    private void Begin(EnrollmentStatus status, DateTimeOffset validFrom, DateTimeOffset recordedAt)
    {
        _periods.Add(new EnrollmentStatusPeriod(this, status, new TemporalPeriod(TimeRange.From(validFrom), TimeRange.From(recordedAt))));
        Status = status;
        StatusSince = validFrom;
    }

    /// <summary>
    /// Supersedes the current row and records, from <paramref name="recordedAt"/>, the old status up to
    /// <paramref name="effectiveAt"/> and the new status from then on.
    /// </summary>
    private void Move(EnrollmentStatus to, DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
    {
        var current = CurrentPeriod();
        current.Supersede(recordedAt);
        _periods.Add(new EnrollmentStatusPeriod(
            this, current.Status, new TemporalPeriod(new TimeRange(current.Period.Valid.Start, effectiveAt), TimeRange.From(recordedAt))));
        Begin(to, effectiveAt, recordedAt);
    }
}
