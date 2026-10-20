using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Records a clinical fact for the participant of an enrollment (spec §8.1): this concept had this value, observed at
/// <c>ObservedAt</c>. The value is the authoritative one; an eCRF field is a representation of it (ADR-2620). Refused
/// when a lock covers the enrollment, its study site or its study (spec §10.5).
/// </summary>
/// <remarks>
/// <para><c>EnrollmentId</c>: The enrollment the fact is about; the participant and study are taken from it, and the
/// command is authorized against it.</para>
/// <para><c>Value</c>: The value as entered: a quantity with its UCUM unit, a code, text, a boolean, a date or time
/// at its precision, or a reasoned absence (<see cref="ObservationValue"/>, ADR-2623).</para>
/// <para><c>ObservedAt</c>: When the fact held in the trial world (valid time); not after the command is recorded.</para>
/// </remarks>
[KernelCapability("observation.record", VerbClass.Execute, Resource = nameof(RecordObservation.EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(ObservationErrors),
    nameof(ObservationErrors.EnrollmentUnknown),
    nameof(ObservationErrors.ValueInvalid),
    nameof(ObservationErrors.ObservedAtInvalid),
    nameof(ObservationErrors.Locked))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RecordObservation(
    TenantId TenantId,
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ConceptRef Concept,
    ObservationValue Value,
    DateTimeOffset ObservedAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ObservationState>;

/// <summary>
/// Corrects an observation's value (K-FACT-1): a new revision supersedes the current one, with the reason for change.
/// Refused when a lock covers the observation (spec §10.5), when <c>ExpectedVersion</c> is not its current version, or
/// when the value is unchanged (ADR-2620).
/// </summary>
/// <remarks>
/// <para><c>EnrollmentId</c>: The enrollment the observation belongs to; the command is authorized against it.</para>
/// <para><c>ExpectedVersion</c>: The version the corrector saw (<see cref="ObservationState.Version"/>).</para>
/// </remarks>
[KernelCapability("observation.correct", VerbClass.Execute, Resource = nameof(CorrectObservation.EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(
    typeof(ObservationErrors),
    nameof(ObservationErrors.Locked),
    nameof(ObservationErrors.VersionStale),
    nameof(ObservationErrors.ValueInvalid),
    nameof(ObservationErrors.ValueUnchanged))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record CorrectObservation(
    TenantId TenantId,
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ObservationValue Value,
    long ExpectedVersion,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ObservationState>;

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
    ObservationValue Value,
    long Version,
    int Revision);

/// <summary>Errors raised by the observation commands (spec §8.1, ADR-2620).</summary>
public static class ObservationErrors
{
    public static readonly KernelError ValueInvalid =
        new("K.Observation.ValueInvalid", 422, "The observation value is missing or invalid.");

    public static readonly KernelError ObservedAtInvalid =
        new("K.Observation.ObservedAtInvalid", 422, "An observation cannot be observed after it is recorded.");

    public static readonly KernelError EnrollmentUnknown =
        new("K.Observation.EnrollmentUnknown", 404, "The enrollment does not exist.");

    public static readonly KernelError ValueUnchanged =
        new("K.Observation.ValueUnchanged", 422, "The correction does not change the value.");

    public static readonly KernelError VersionStale =
        new("K.Observation.VersionStale", 409, "The observation changed since you read it; reload it and correct the current value.");

    public static readonly KernelError Locked =
        new("K.Observation.Locked", 423, "A lock covers this observation's scope; nothing is recorded or corrected there.");

    /// <summary>Every observation error, for the API's documented problem set.</summary>
    public static IReadOnlyList<KernelError> All { get; } =
        [ValueInvalid, ObservedAtInvalid, EnrollmentUnknown, ValueUnchanged, VersionStale, Locked];
}
