using System;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;

namespace Cadence.Kernel.Events;

/// <summary>A participant identity was registered (spec §7.4, Execution family).</summary>
[KernelEvent("ParticipantRegistered", 1)]
public sealed record ParticipantRegistered(TenantId TenantId, ParticipantId ParticipantId);

/// <summary>A participant began screening at a study site under a subject code.</summary>
[KernelEvent("ParticipantScreened", 1)]
public sealed record ParticipantScreened(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    SubjectCode SubjectCode,
    DateTimeOffset ScreenedAt);

/// <summary>A participant was enrolled and given the next enrollment number in the study.</summary>
[KernelEvent("ParticipantEnrolled", 1)]
public sealed record ParticipantEnrolled(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    int EnrollmentNumber,
    DateTimeOffset EnrolledAt);

/// <summary>An enrollment moved to a status other than Enrolled or Withdrawn (screen failure, randomization, activation, completion).</summary>
[KernelEvent("EnrollmentStatusChanged", 1)]
public sealed record EnrollmentStatusChanged(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    EnrollmentStatus From,
    EnrollmentStatus To,
    DateTimeOffset EffectiveAt);

/// <summary>A participant was withdrawn by the named attestation.</summary>
[KernelEvent("ParticipantWithdrawn", 1)]
public sealed record ParticipantWithdrawn(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    EnrollmentStatus From,
    WithdrawalKind Kind,
    AttestationId AttestationId,
    DateTimeOffset WithdrawnAt);
