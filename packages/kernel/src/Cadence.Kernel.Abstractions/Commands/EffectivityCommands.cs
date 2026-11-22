using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Execution;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Declares that an approved protocol version is being made effective in a scope of its study (spec §7.1). The effectivity
/// starts <see cref="EffectivityStatus.Pending"/> at <c>DeclaredAt</c>, in valid time. A version has at most one effectivity
/// per scope.
/// </summary>
[KernelCapability("effectivity.declare", VerbClass.Execute, Resource = nameof(ProtocolVersionId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(EffectivityErrors),
    nameof(EffectivityErrors.ScopeInvalid),
    nameof(EffectivityErrors.TransitionTimeInvalid),
    nameof(EffectivityErrors.ProtocolVersionUnknown),
    nameof(EffectivityErrors.ProtocolVersionNotApproved),
    nameof(EffectivityErrors.StudySiteUnknown),
    nameof(EffectivityErrors.StudySiteNotInStudy),
    nameof(EffectivityErrors.AlreadyDeclared))]
public sealed record DeclareEffectivity(
    TenantId TenantId,
    EffectivityId EffectivityId,
    ProtocolVersionId ProtocolVersionId,
    EffectivityScope Scope,
    DateTimeOffset DeclaredAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<EffectivityState>;

/// <summary>
/// Moves an effectivity to <c>To</c> from <c>EffectiveAt</c> in valid time (spec §7.1). <c>Evidence</c> names the
/// artifact versions that justify the move. <see cref="EffectivityStatus.Effective"/> needs at least one ethics
/// approval, regulatory approval or CTA acknowledgement of the version's study (K-TIME-5), and the protocol version must
/// be approved.
/// </summary>
[KernelCapability("effectivity.change_status", VerbClass.Approve, Resource = nameof(EffectivityId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(
    typeof(EffectivityErrors),
    nameof(EffectivityErrors.InvalidTransition),
    nameof(EffectivityErrors.TransitionTimeInvalid),
    nameof(EffectivityErrors.ProtocolVersionUnknown),
    nameof(EffectivityErrors.ProtocolVersionNotApproved),
    nameof(EffectivityErrors.EvidenceRequired),
    nameof(EffectivityErrors.EvidenceInvalid))]
public sealed record ChangeEffectivityStatus(
    TenantId TenantId,
    EffectivityId EffectivityId,
    EffectivityStatus To,
    DateTimeOffset EffectiveAt,
    IReadOnlyList<ArtifactVersionId> Evidence,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<EffectivityState>;

/// <summary>
/// Activates a study site (spec §7.1). It needs at least one protocol version in force at the site now (K-TIME-4).
/// </summary>
[KernelCapability("study_site.activate", VerbClass.Approve, Resource = nameof(StudySiteId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[RaisesKernelErrors(
    typeof(EffectivityErrors),
    nameof(EffectivityErrors.StudySiteUnknown),
    nameof(EffectivityErrors.StudySiteAlreadyActive),
    nameof(EffectivityErrors.NoEffectiveVersion))]
public sealed record ActivateStudySite(
    TenantId TenantId,
    StudySiteId StudySiteId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<StudySiteState>;

/// <summary>
/// Records bitemporal participant consent for a protocol version and scope set on an enrollment (spec §9.4, PRD-SUB-007).
/// When <c>Signed</c> is true, resolves every pending re-consent transition covered by <c>ProtocolVersionId</c> (or its
/// amendment ancestor chain) and advances or establishes bitemporal <c>Applicability</c> at <c>SignedAt</c> (spec §7.5).
/// </summary>
[KernelCapability("consent.record", VerbClass.Execute, Resource = nameof(RecordConsentStatus.EnrollmentId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(ConsentErrors),
    nameof(ConsentErrors.EnrollmentUnknown),
    nameof(ConsentErrors.ProtocolVersionUnknown),
    nameof(ConsentErrors.SignedAtInvalid),
    nameof(ConsentErrors.ScopesInvalid))]
public sealed record RecordConsentStatus(
    TenantId TenantId,
    ConsentStatusId ConsentStatusId,
    EnrollmentId EnrollmentId,
    ProtocolVersionId ProtocolVersionId,
    bool Signed,
    DateTimeOffset SignedAt,
    ConsentBasisKind BasisKind,
    AttestationId? AttestationId,
    ArtifactVersionId? ArtifactVersionId,
    IReadOnlyList<ConsentScopeEntry> Scopes,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ConsentStatusState>;

/// <summary>
/// Flags or clears an applicability inconsistency on an enrollment (spec §7.5, K-TIME-6); while flagged, new clinical
/// data capture is refused until the inconsistency is resolved.
/// </summary>
[KernelCapability("applicability.flag_inconsistency", VerbClass.Execute, Resource = nameof(FlagApplicabilityInconsistency.EnrollmentId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[RaisesKernelErrors(typeof(ConsentErrors), nameof(ConsentErrors.EnrollmentUnknown))]
public sealed record FlagApplicabilityInconsistency(
    TenantId TenantId,
    EnrollmentId EnrollmentId,
    bool IsInconsistent,
    string? InconsistencyReason,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ApplicabilityState>;

/// <summary>An effectivity after a command.</summary>
public sealed record EffectivityState(
    EffectivityId EffectivityId,
    ProtocolVersionId ProtocolVersionId,
    EffectivityScope Scope,
    EffectivityStatus Status,
    DateTimeOffset StatusSince,
    IReadOnlyList<ArtifactVersionId> Evidence);

/// <summary>A study site after a command, with the versions in force there when it was activated.</summary>
public sealed record StudySiteState(StudySiteId StudySiteId, string Status, IReadOnlyList<EffectiveVersion> EffectiveVersions);

/// <summary>Errors raised by the effectivity and study-site commands (spec §7.1).</summary>
public static class EffectivityErrors
{
    public static readonly KernelError ScopeInvalid =
        new("K.Effectivity.ScopeInvalid", 422, "The effectivity scope is not valid.");

    public static readonly KernelError ProtocolVersionUnknown =
        new("K.Effectivity.ProtocolVersionUnknown", 404, "The protocol version does not exist.");

    public static readonly KernelError ProtocolVersionWithdrawn =
        new("K.Effectivity.ProtocolVersionWithdrawn", 409, "A withdrawn protocol version cannot be made effective.");

    public static readonly KernelError ProtocolVersionNotApproved =
        new("K.Effectivity.ProtocolVersionNotApproved", 409, "Only an approved protocol version can become effective.");

    public static readonly KernelError StudySiteNotInStudy =
        new("K.Effectivity.StudySiteNotInStudy", 422, "The study site does not belong to the protocol version's study.");

    public static readonly KernelError AlreadyDeclared =
        new("K.Effectivity.AlreadyDeclared", 409, "The protocol version already has an effectivity for this scope.");

    public static readonly KernelError InvalidTransition =
        new("K.Effectivity.InvalidTransition", 409, "The effectivity cannot move to that status.");

    public static readonly KernelError TransitionTimeInvalid =
        new("K.Effectivity.TransitionTimeInvalid", 422, "A status change happens after the current status began and not after it is recorded.");

    public static readonly KernelError EvidenceRequired =
        new("K.Effectivity.EvidenceRequired", 422, "An effectivity becomes effective only with an ethics or regulatory approval as evidence (K-TIME-5).");

    public static readonly KernelError EvidenceInvalid =
        new("K.Effectivity.EvidenceInvalid", 422, "The evidence names an artifact version that does not exist, belongs to another study, or is not of a kind this status accepts.");

    public static readonly KernelError StudySiteUnknown =
        new("K.Effectivity.StudySiteUnknown", 404, "The study site does not exist.");

    public static readonly KernelError StudySiteAlreadyActive =
        new("K.Effectivity.StudySiteAlreadyActive", 409, "The study site is already active.");

    public static readonly KernelError NoEffectiveVersion =
        new("K.Effectivity.NoEffectiveVersion", 409, "A study site is activated only when a protocol version is effective there (K-TIME-4).");
}
