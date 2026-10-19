using System;

using Cadence.Kernel.Authorization;

namespace Cadence.Kernel.Commands;

/// <summary>What a qualification attests about its holder (spec §9.3).</summary>
public enum QualificationKind
{
    /// <summary>Completed training, such as GCP or a system's user training.</summary>
    Training,

    /// <summary>Training on one protocol version, named by the qualification.</summary>
    ProtocolTraining,

    /// <summary>A professional credential or licence.</summary>
    Credential,

    /// <summary>A certificate, such as a GCP or IATA certificate.</summary>
    Certificate,
}

/// <summary>
/// Records a qualification: evidence that an actor completed training or holds a credential or certificate, valid for a
/// period (spec §9.3, K-AUTH-5). The evidence is a version of a filed training record or credential artifact. The
/// qualification is effective in valid time from <c>ValidFrom</c> but known only from when it is recorded, so training
/// recorded late cannot validate an earlier action (K-AUTH-3).
/// </summary>
/// <remarks>
/// <para><c>Code</c>: What the qualification is, in lower snake_case, for example <c>gcp</c> or <c>protocol_training</c>.
/// A requirement names the code it needs.</para>
/// <para><c>ProtocolVersionId</c>: The protocol version trained on; required for <see cref="QualificationKind.ProtocolTraining"/>
/// and not allowed otherwise.</para>
/// <para><c>ValidTo</c>: When it expires, exclusive; null when it does not.</para>
/// </remarks>
[KernelCapability("qualification.record", VerbClass.Execute)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RecordQualification(
    TenantId TenantId,
    QualificationId QualificationId,
    Actor Holder,
    QualificationKind Kind,
    string Code,
    ProtocolVersionId? ProtocolVersionId,
    ArtifactVersionId Evidence,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<QualificationState>;

/// <summary>
/// Ends a qualification's validity at <c>EffectiveAt</c>, for example when a licence is suspended or a training record
/// was filed in error. The earlier record stays, so decisions made while it was known remain explained by it.
/// </summary>
[KernelCapability("qualification.revoke", VerbClass.Approve, Resource = nameof(QualificationId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RevokeQualification(
    TenantId TenantId,
    QualificationId QualificationId,
    DateTimeOffset EffectiveAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<QualificationState>;

/// <summary>
/// Requires a current qualification of <c>QualificationCode</c> to exercise <c>Capability</c> within <c>Scope</c>
/// (spec §9.3 step 3, K-AUTH-5). While the requirement is in effect the evaluator denies the capability to an actor
/// without such a qualification, and the denial names what is missing (K-AUTH-6).
/// </summary>
/// <remarks>
/// <para><c>ProtocolVersionId</c>: When given, only a qualification for that protocol version satisfies the
/// requirement, for example protocol training on an amendment.</para>
/// <para><c>ValidTo</c>: When the requirement ends, exclusive; null while open-ended.</para>
/// </remarks>
[KernelCapability("qualification_requirement.define", VerbClass.Approve)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record DefineQualificationRequirement(
    TenantId TenantId,
    QualificationRequirementId RequirementId,
    string Capability,
    string QualificationCode,
    ProtocolVersionId? ProtocolVersionId,
    AuthorityScope Scope,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<QualificationRequirementState>;

/// <summary>Ends a qualification requirement at <c>EffectiveAt</c>; decisions made while it applied stay explained by it.</summary>
[KernelCapability("qualification_requirement.retire", VerbClass.Approve, Resource = nameof(RequirementId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RetireQualificationRequirement(
    TenantId TenantId,
    QualificationRequirementId RequirementId,
    DateTimeOffset EffectiveAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<QualificationRequirementState>;

/// <summary>A qualification after a command. <c>ValidTo</c> is null when it does not expire; <c>Revoked</c> when nothing remains valid.</summary>
public sealed record QualificationState(
    QualificationId QualificationId,
    Actor Holder,
    QualificationKind Kind,
    string Code,
    ProtocolVersionId? ProtocolVersionId,
    ArtifactVersionId Evidence,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Revoked);

/// <summary>A qualification requirement after a command. <c>Retired</c> when nothing remains in effect.</summary>
public sealed record QualificationRequirementState(
    QualificationRequirementId RequirementId,
    string Capability,
    string QualificationCode,
    ProtocolVersionId? ProtocolVersionId,
    AuthorityScope Scope,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Retired);

/// <summary>Errors raised by the qualification commands (spec §9.3).</summary>
public static class QualificationErrors
{
    public static readonly KernelError CodeInvalid =
        new("K.Qualification.CodeInvalid", 422, "A qualification code is 1 to 64 characters of lower snake_case.");

    public static readonly KernelError ProtocolVersionMismatch =
        new("K.Qualification.ProtocolVersionMismatch", 422, "Protocol training names its protocol version; other qualifications name none.");

    public static readonly KernelError ProtocolVersionUnknown =
        new("K.Qualification.ProtocolVersionUnknown", 404, "The protocol version does not exist.");

    public static readonly KernelError EvidenceInvalid =
        new("K.Qualification.EvidenceInvalid", 422, "The evidence must be a version of a training record or credential artifact.");

    public static readonly KernelError ValidityInvalid =
        new("K.Qualification.ValidityInvalid", 422, "A valid period must end after it starts.");

    public static readonly KernelError RevocationInvalid =
        new("K.Qualification.RevocationInvalid", 409, "Nothing is in effect at that time.");
}
