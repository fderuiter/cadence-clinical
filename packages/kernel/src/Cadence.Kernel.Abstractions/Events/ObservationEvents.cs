using System;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Events;

/// <summary>An observation was recorded (spec §8.1, Evidence family).</summary>
[KernelEvent("ObservationRecorded", 1)]
public sealed record ObservationRecorded(
    TenantId TenantId,
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ConceptRef Concept,
    string Value,
    DateTimeOffset ObservedAt);

/// <summary>
/// An observation's value was corrected (K-FACT-1, Evidence family): revision <c>Revision</c> supersedes
/// <c>Supersedes</c>. Consumers such as the eCRF projection and edit checks react after commit (ADR-2620).
/// </summary>
[KernelEvent("ObservationCorrected", 1)]
public sealed record ObservationCorrected(
    TenantId TenantId,
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    int Revision,
    int Supersedes,
    string Value,
    string ReasonForChange);

/// <summary>An observation was locked (Quality family); no correction commits after it.</summary>
[KernelEvent("ObservationLocked", 1)]
public sealed record ObservationLocked(
    TenantId TenantId,
    ObservationLockId LockId,
    ObservationId ObservationId,
    EnrollmentId EnrollmentId);
