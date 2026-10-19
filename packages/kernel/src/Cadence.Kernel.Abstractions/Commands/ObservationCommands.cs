using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Records a clinical fact for the participant of an enrollment (spec §8.1): this concept had this value, observed at
/// <c>ObservedAt</c>. The value is the authoritative one; an eCRF field is a representation of it (ADR-2620).
/// </summary>
/// <remarks>
/// <para><c>EnrollmentId</c>: The enrollment the fact is about; the participant and study are taken from it, and the
/// command is authorized against it.</para>
/// <para><c>Value</c>: The value as entered, at most <see cref="ObservationLimits.MaxValueLength"/> characters. The
/// typed value model (#5363) replaces it.</para>
/// <para><c>ObservedAt</c>: When the fact held in the trial world (valid time); not after the command is recorded.</para>
/// </remarks>
[KernelCapability("observation.record", VerbClass.Execute, Resource = nameof(RecordObservation.EnrollmentId))]
public sealed record RecordObservation(
    TenantId TenantId,
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ConceptRef Concept,
    string Value,
    DateTimeOffset ObservedAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ObservationState>;

/// <summary>
/// Corrects an observation's value (K-FACT-1): a new revision supersedes the current one, with the reason for change.
/// Refused when the observation is locked at commit time, when <c>ExpectedVersion</c> is not its current version, or
/// when the value is unchanged (ADR-2620).
/// </summary>
/// <remarks>
/// <para><c>EnrollmentId</c>: The enrollment the observation belongs to; the command is authorized against it.</para>
/// <para><c>ExpectedVersion</c>: The version the corrector saw (<see cref="ObservationState.Version"/>).</para>
/// </remarks>
[KernelCapability("observation.correct", VerbClass.Execute, Resource = nameof(CorrectObservation.EnrollmentId))]
public sealed record CorrectObservation(
    TenantId TenantId,
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    string Value,
    long ExpectedVersion,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ObservationState>;

/// <summary>
/// Locks an observation's value (kernel Quality, ADR-2620). Once locked, every correction is refused. There is no
/// unlock in the reference slice.
/// </summary>
[KernelCapability("observation.lock", VerbClass.Execute, Resource = nameof(LockObservation.EnrollmentId))]
public sealed record LockObservation(
    TenantId TenantId,
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ObservationLockState>;

/// <summary>An observation's current value after a command.</summary>
/// <remarks>
/// <para><c>Version</c>: The observation's version; a correction names it as its expected version.</para>
/// <para><c>Revision</c>: The number of the revision holding <c>Value</c>, 1 for the recorded value.</para>
/// </remarks>
public sealed record ObservationState(
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ConceptRef Concept,
    string Value,
    long Version,
    int Revision);

/// <summary>An observation's lock after <see cref="LockObservation"/>.</summary>
public sealed record ObservationLockState(ObservationLockId LockId, ObservationId ObservationId, DateTimeOffset LockedAt, string LockedBy);

/// <summary>Limits on observation values.</summary>
public static class ObservationLimits
{
    /// <summary>The longest value the kernel stores.</summary>
    public const int MaxValueLength = 4000;
}

/// <summary>Errors raised by the observation commands (spec §8.1, ADR-2620).</summary>
public static class ObservationErrors
{
    public static readonly KernelError ReasonForChangeRequired =
        new("K.Observation.ReasonForChangeRequired", 422, "A non-empty reason for change is required to correct an observation.");

    public static readonly KernelError ValueInvalid =
        new("K.Observation.ValueInvalid", 422, $"An observation value is required, at most {ObservationLimits.MaxValueLength} characters.");

    public static readonly KernelError ObservedAtInvalid =
        new("K.Observation.ObservedAtInvalid", 422, "An observation cannot be observed after it is recorded.");

    public static readonly KernelError EnrollmentUnknown =
        new("K.Observation.EnrollmentUnknown", 404, "The enrollment does not exist.");

    public static readonly KernelError ValueUnchanged =
        new("K.Observation.ValueUnchanged", 422, "The correction does not change the value.");

    public static readonly KernelError VersionStale =
        new("K.Observation.VersionStale", 409, "The observation changed since you read it; reload it and correct the current value.");

    public static readonly KernelError Locked =
        new("K.Observation.Locked", 423, "The observation is locked; it cannot be corrected.");

    public static readonly KernelError AlreadyLocked =
        new("K.Observation.AlreadyLocked", 409, "The observation is already locked.");

    /// <summary>Every observation error, for the API's documented problem set.</summary>
    public static IReadOnlyList<KernelError> All { get; } =
        [ReasonForChangeRequired, ValueInvalid, ObservedAtInvalid, EnrollmentUnknown, ValueUnchanged, VersionStale, Locked, AlreadyLocked];
}
