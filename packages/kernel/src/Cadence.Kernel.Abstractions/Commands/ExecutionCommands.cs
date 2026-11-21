using System;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Execution;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Registers a participant: a pseudonymous kernel identity in a tenant that holds no study, site, code or status
/// (spec §7.4, §9.2). Study membership is an <see cref="ScreenParticipant">enrollment</see>.
/// </summary>
[KernelCapability("participant.register", VerbClass.Execute)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
public sealed record RegisterParticipant(
    TenantId TenantId,
    ParticipantId ParticipantId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ParticipantId>;

/// <summary>
/// Starts a participant's enrollment at a study site in <see cref="EnrollmentStatus.Screening"/>, from
/// <c>ScreenedAt</c> in valid time. The subject code must be unused in the study, and the participant must have no
/// other active enrollment at the study site.
/// </summary>
/// <remarks>
/// <para><c>ScreenedAt</c>: When screening began in the trial world (valid time, K-TIME-9); not after the command is recorded.</para>
/// </remarks>
[KernelCapability("participant.screen", VerbClass.Execute, Resource = nameof(ScreenParticipant.StudySiteId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(ExecutionErrors),
    nameof(ExecutionErrors.ParticipantUnknown),
    nameof(ExecutionErrors.StudySiteUnknown),
    nameof(ExecutionErrors.SubjectCodeInvalid),
    nameof(ExecutionErrors.SubjectCodeTaken),
    nameof(ExecutionErrors.AlreadyActiveAtSite),
    nameof(ExecutionErrors.TransitionTimeInvalid))]
public sealed record ScreenParticipant(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudySiteId StudySiteId,
    SubjectCode SubjectCode,
    DateTimeOffset ScreenedAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<EnrollmentState>;

/// <summary>
/// Moves an enrollment to <c>To</c> from <c>EffectiveAt</c> in valid time, along
/// <see cref="EnrollmentTransitions"/>. Entering <see cref="EnrollmentStatus.Enrolled"/> assigns the enrollment number.
/// Withdrawal has its own command, <see cref="WithdrawParticipant"/>.
/// </summary>
/// <remarks>
/// <para><c>EffectiveAt</c>: When the change happened in the trial world: after the current status began and not after the command is recorded.</para>
/// </remarks>
[KernelCapability("enrollment.change_status", VerbClass.Execute, Resource = nameof(ChangeEnrollmentStatus.EnrollmentId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record ChangeEnrollmentStatus(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    EnrollmentStatus To,
    DateTimeOffset EffectiveAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<EnrollmentState>;

/// <summary>
/// Withdraws a participant from an active enrollment (spec §7.4). The acting person must have signed an attestation
/// with meaning <c>Withdrawn</c> over the enrollment's current content hash (<see cref="EnrollmentState.ContentHash"/>).
/// The reason for change is the withdrawal reason.
/// </summary>
[KernelCapability("participant.withdraw", VerbClass.Execute, Resource = nameof(WithdrawParticipant.EnrollmentId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record WithdrawParticipant(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    WithdrawalKind Kind,
    AttestationId AttestationId,
    DateTimeOffset EffectiveAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<EnrollmentState>;

/// <summary>An enrollment's current state, returned by the enrollment commands.</summary>
/// <remarks>
/// <para><c>StatusSince</c>: When the current status began, in valid time.</para>
/// <para><c>EnrollmentNumber</c>: The gap-free number in the study, set on entering <see cref="EnrollmentStatus.Enrolled"/>.</para>
/// <para><c>ContentHash</c>: The hash an attestation about this enrollment signs; it changes with every change to the enrollment.</para>
/// </remarks>
public sealed record EnrollmentState(
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    SubjectCode SubjectCode,
    EnrollmentStatus Status,
    DateTimeOffset StatusSince,
    int? EnrollmentNumber,
    string ContentHash);

/// <summary>Errors raised by the participant and enrollment commands.</summary>
public static class ExecutionErrors
{
    public static readonly KernelError ParticipantUnknown =
        new("K.Execution.ParticipantUnknown", 404, "The participant does not exist.");

    public static readonly KernelError StudySiteUnknown =
        new("K.Execution.StudySiteUnknown", 404, "The study site does not exist.");

    public static readonly KernelError SubjectCodeInvalid =
        new("K.Execution.SubjectCodeInvalid", 422, "A subject code is 1 to 64 printable characters with no leading or trailing whitespace.");

    public static readonly KernelError SubjectCodeTaken =
        new("K.Execution.SubjectCodeTaken", 409, "The subject code is already used in this study.");

    public static readonly KernelError AlreadyActiveAtSite =
        new("K.Execution.AlreadyActiveAtSite", 409, "The participant already has an active enrollment at this study site.");

    public static readonly KernelError InvalidTransition =
        new("K.Execution.InvalidTransition", 409, "The enrollment cannot move to that status from its current status.");

    public static readonly KernelError TransitionTimeInvalid =
        new("K.Execution.TransitionTimeInvalid", 422, "The change must happen after the current status began and not after it is recorded.");

    public static readonly KernelError WithdrawalRequiresCommand =
        new("K.Execution.WithdrawalRequiresCommand", 422, "Withdrawal needs WithdrawParticipant, with a withdrawal kind and an attestation.");

    public static readonly KernelError WithdrawalKindInvalid =
        new("K.Execution.WithdrawalKindInvalid", 422, "The withdrawal kind is not recognised.");

    public static readonly KernelError AttestationRejected =
        new("K.Execution.AttestationRejected", 422, "The attestation does not authorize this withdrawal.");
}
