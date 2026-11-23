using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Applies a <see cref="LockKind.Locked"/> or <see cref="LockKind.Frozen"/> lock at a scope of a study (spec §10.5,
/// ADR-2621): the study itself (database lock), a study site, a participant enrollment or one observation. While it
/// holds, no observation in the scope is recorded or corrected. The signer attests
/// <see cref="LockStatement.Apply"/> for <c>AssertionId</c> first, with meaning Approved or Responsibility.
/// </summary>
/// <remarks>
/// <para><c>AssertionId</c>: The id of the lock, chosen before signing, since the attestation names it.</para>
/// <para><c>StudyId</c>: The study the scope belongs to; the command is authorized against it.</para>
/// <para><c>Scope</c>: The study, study site, enrollment or observation to lock, without a version.</para>
/// </remarks>
[KernelCapability("lock.apply", VerbClass.Approve, Resource = nameof(ApplyLock.StudyId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(AssertionErrors),
    nameof(AssertionErrors.ScopeInvalid),
    nameof(AssertionErrors.ScopeUnknown),
    nameof(AssertionErrors.AlreadyLocked),
    nameof(AssertionErrors.ScopeNotReady),
    nameof(AssertionErrors.AttestationRejected))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ApplyLock(
    TenantId TenantId,
    AssertionId AssertionId,
    StudyId StudyId,
    NodeRef Scope,
    LockKind Kind,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<LockState>;

/// <summary>
/// Releases a lock (spec §10.5): a retracting Assertion with its own reason and attestation. The signer attests
/// <see cref="LockStatement.Release"/> for <c>AssertionId</c> first.
/// </summary>
/// <remarks>
/// <para><c>AssertionId</c>: The id of the release, chosen before signing.</para>
/// <para><c>LockId</c>: The lock to release.</para>
/// </remarks>
[KernelCapability("lock.release", VerbClass.Approve, Resource = nameof(ReleaseLock.StudyId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(
    typeof(AssertionErrors),
    nameof(AssertionErrors.AlreadyRetracted),
    nameof(AssertionErrors.AttestationRejected))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ReleaseLock(
    TenantId TenantId,
    AssertionId AssertionId,
    StudyId StudyId,
    AssertionId LockId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<LockState>;

/// <summary>
/// Verifies one revision of an observation's value against source (SDV, spec §8.2). It targets
/// <c>(ObservationId, Revision)</c>, so a later correction is not verified (ADR-2621). Refused when the revision is
/// not the current one, or when a <see cref="LockKind.Locked"/> lock covers the observation.
/// </summary>
[KernelCapability("observation.verify", VerbClass.Execute, Resource = nameof(VerifyObservation.EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.RevisionStale), nameof(AssertionErrors.Locked))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record VerifyObservation(
    TenantId TenantId,
    AssertionId AssertionId,
    EnrollmentId EnrollmentId,
    ObservationId ObservationId,
    int Revision,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<AssertionState>;

/// <summary>
/// Raises a query about an observation (spec §8.2). It names the <c>ObservationId</c> and records the revision judged
/// (<c>TargetRevision</c>, defaulting to the observation's current revision), staying open across corrections until it
/// is retracted. Service-account forwarders may supply <c>AssertedBy</c> to preserve the originating actor.
/// Refused when a <see cref="LockKind.Locked"/> lock covers the observation.
/// </summary>
[KernelCapability("observation.query", VerbClass.Execute, Resource = nameof(RaiseQuery.EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(
    typeof(AssertionErrors),
    nameof(AssertionErrors.RevisionStale),
    nameof(AssertionErrors.TextInvalid),
    nameof(AssertionErrors.Locked))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RaiseQuery(
    TenantId TenantId,
    AssertionId AssertionId,
    EnrollmentId EnrollmentId,
    ObservationId ObservationId,
    string Text,
    string IdempotencyKey,
    string ReasonForChange,
    int? Revision = null,
    string? AssertedBy = null) : ICommand<AssertionState>;

/// <summary>
/// Answers an open query (spec §8.2). Service-account forwarders may supply <c>AssertedBy</c> and <c>Revision</c> to
/// preserve the originating actor and value revision. Refused when the query is closed or a lock covers its observation.
/// </summary>
[KernelCapability("observation.query.answer", VerbClass.Execute, Resource = nameof(AnswerQuery.EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(
    typeof(AssertionErrors),
    nameof(AssertionErrors.RevisionStale),
    nameof(AssertionErrors.TextInvalid),
    nameof(AssertionErrors.NotAQuery),
    nameof(AssertionErrors.AlreadyRetracted),
    nameof(AssertionErrors.Locked))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record AnswerQuery(
    TenantId TenantId,
    AssertionId AssertionId,
    EnrollmentId EnrollmentId,
    AssertionId QueryId,
    string Text,
    string IdempotencyKey,
    string ReasonForChange,
    int? Revision = null,
    string? AssertedBy = null) : ICommand<AssertionState>;

/// <summary>
/// Retracts an assertion about an observation (spec §8.2): withdraws a verification or an answer, or closes a query.
/// Service-account forwarders may supply <c>AssertedBy</c> to preserve the originating actor.
/// The retracted assertion stays readable. Locks are released with <see cref="ReleaseLock"/> instead.
/// </summary>
[KernelCapability("assertion.retract", VerbClass.Execute, Resource = nameof(RetractAssertion.EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(
    typeof(AssertionErrors),
    nameof(AssertionErrors.AlreadyRetracted),
    nameof(AssertionErrors.NotRetractable),
    nameof(AssertionErrors.Locked))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RetractAssertion(
    TenantId TenantId,
    AssertionId AssertionId,
    EnrollmentId EnrollmentId,
    AssertionId RetractedId,
    string IdempotencyKey,
    string ReasonForChange,
    string? AssertedBy = null) : ICommand<AssertionState>;

/// <summary>An assertion after a command.</summary>
/// <remarks>
/// <para><c>Target</c>: What it is about: an observation, another assertion, or a lock scope.</para>
/// <para><c>Revision</c>: For a judgement of a value, the revision judged.</para>
/// <para><c>Retracts</c>: The assertion it retracts, if it is a retraction.</para>
/// </remarks>
public sealed record AssertionState(
    AssertionId AssertionId,
    AssertionKind Kind,
    NodeRef Target,
    int? Revision,
    AssertionId? Retracts,
    DateTimeOffset AssertedAt,
    string AssertedBy);

/// <summary>A lock after <see cref="ApplyLock"/>, or its release after <see cref="ReleaseLock"/>.</summary>
/// <remarks>
/// <para><c>LockId</c>: The lock.</para>
/// <para><c>Released</c>: True after <see cref="ReleaseLock"/>.</para>
/// </remarks>
public sealed record LockState(
    AssertionId LockId,
    LockKind Kind,
    NodeRef Scope,
    AttestationId AttestationId,
    bool Released,
    DateTimeOffset At,
    string By);

/// <summary>Errors raised by the assertion and lock commands (spec §8.2, §10.5, ADR-2621).</summary>
public static class AssertionErrors
{
    public static readonly KernelError ScopeInvalid =
        new("K.Assertion.ScopeInvalid", 422, "A lock scope is a study, study site, enrollment or observation, named without a version.");

    public static readonly KernelError ScopeUnknown =
        new("K.Assertion.ScopeUnknown", 404, "The lock scope does not exist in this study.");

    public static readonly KernelError AlreadyLocked =
        new("K.Assertion.AlreadyLocked", 409, "A lock of this kind already holds at this scope.");

    public static readonly KernelError ScopeNotReady =
        new("K.Assertion.ScopeNotReady", 409, "The lock scope has open queries or missing required items that must be resolved before locking.");

    public static readonly KernelError AlreadyRetracted =
        new("K.Assertion.AlreadyRetracted", 409, "The assertion is already retracted.");

    public static readonly KernelError NotRetractable =
        new("K.Assertion.NotRetractable", 422, "Only a verification, query or answer about an observation is retracted this way; release a lock instead.");

    public static readonly KernelError NotAQuery =
        new("K.Assertion.NotAQuery", 422, "The assertion answered is not a query.");

    public static readonly KernelError RevisionStale =
        new("K.Assertion.RevisionStale", 409, "The revision is not the observation's current one; reload it and judge the current value.");

    public static readonly KernelError TextInvalid =
        new("K.Assertion.TextInvalid", 422, $"The text is required and at most {AssertionText.MaxLength} characters.");

    public static readonly KernelError Locked =
        new("K.Assertion.Locked", 423, "A lock covers the observation; nothing more is asserted about it.");

    public static readonly KernelError AttestationRejected =
        new("K.Assertion.AttestationRejected", 422, "The attestation does not sign this lock statement.");

    /// <summary>Every assertion error, for the API's documented problem set.</summary>
    public static IReadOnlyList<KernelError> All { get; } =
        [ScopeInvalid, ScopeUnknown, AlreadyLocked, ScopeNotReady, AlreadyRetracted, NotRetractable, NotAQuery, RevisionStale, TextInvalid, Locked, AttestationRejected];
}
