using System;
using System.Text.Json.Serialization;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Identity;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Records a person's direct identifiers in the IdentityVault, sealed under a fresh data key (spec §9.2, ADR-2458).
/// Recording again replaces the sealed details. The person must be visible in the tenant (K-ID-4).
/// </summary>
/// <remarks>
/// <para><c>Details</c> is never serialized: it is not part of the request hash and never reaches the audit trail, the
/// events or the idempotency record. A retry under the same idempotency key replays the first result whatever
/// details it carries.</para>
/// </remarks>
[KernelCapability("identity.record.person", VerbClass.Execute, Resource = nameof(PersonId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record RecordPersonIdentity(
    TenantId TenantId,
    PersonId PersonId,
    [property: JsonIgnore] IdentityDetails Details,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<PersonId>;

/// <summary>
/// Records a participant's site-local identifier, such as the screening log number, in the IdentityVault (spec §9.2).
/// Recording again replaces it.
/// </summary>
/// <remarks>
/// <para><c>Details</c> is never serialized, as for <see cref="RecordPersonIdentity"/>.</para>
/// </remarks>
[KernelCapability("identity.record.site_local", VerbClass.Execute, Resource = nameof(ParticipantId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RecordSiteLocalIdentity(
    TenantId TenantId,
    ParticipantId ParticipantId,
    [property: JsonIgnore] IdentityDetails Details,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ParticipantId>;

/// <summary>
/// Audits the disclosure of a person's direct identifiers (K-IDN-1). Send it through <see cref="IIdentityVault"/>,
/// which decrypts only after it commits; sent on its own it records a reveal and discloses nothing.
/// </summary>
[KernelCapability("identity.reveal.person", VerbClass.Execute, Resource = nameof(PersonId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record RevealPersonIdentity(
    TenantId TenantId,
    PersonId PersonId,
    string IdempotencyKey,
    string ReasonForChange) : IIdentityRevealCommand
{
    /// <inheritdoc />
    [JsonIgnore]
    public IdentityLayer Layer => IdentityLayer.Person;

    /// <inheritdoc />
    [JsonIgnore]
    public Guid SubjectId => PersonId.Value;
}

/// <summary>Audits the disclosure of a participant's site-local identifier (K-IDN-1), as <see cref="RevealPersonIdentity"/>.</summary>
[KernelCapability("identity.reveal.site_local", VerbClass.Execute, Resource = nameof(ParticipantId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record RevealSiteLocalIdentity(
    TenantId TenantId,
    ParticipantId ParticipantId,
    string IdempotencyKey,
    string ReasonForChange) : IIdentityRevealCommand
{
    /// <inheritdoc />
    [JsonIgnore]
    public IdentityLayer Layer => IdentityLayer.SiteLocal;

    /// <inheritdoc />
    [JsonIgnore]
    public Guid SubjectId => ParticipantId.Value;
}

/// <summary>
/// Erases a person's direct identifiers by crypto-shredding: the sealed details and their data key are deleted, and
/// the pseudonymous kernel records remain (spec §9.2). An erased record cannot be recorded again.
/// </summary>
[KernelCapability("identity.erase.person", VerbClass.Approve, Resource = nameof(PersonId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ErasePersonIdentity(
    TenantId TenantId,
    PersonId PersonId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<PersonId>;

/// <summary>Erases a participant's site-local identifier by crypto-shredding, as <see cref="ErasePersonIdentity"/>.</summary>
[KernelCapability("identity.erase.site_local", VerbClass.Approve, Resource = nameof(ParticipantId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record EraseSiteLocalIdentity(
    TenantId TenantId,
    ParticipantId ParticipantId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ParticipantId>;

/// <summary>
/// Links a participant to the person it is, so one person's participations across studies can be found (K-IDN-2). The
/// link lives in the vault; the kernel event names only the participant. A participant links to at most one person, and
/// linking it again to the same person is a no-op.
/// </summary>
[KernelCapability("identity.link.cross_study", VerbClass.Execute, Resource = nameof(PersonId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record LinkParticipantToPerson(
    TenantId TenantId,
    ParticipantId ParticipantId,
    PersonId PersonId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ParticipantId>;

/// <summary>
/// Audits the disclosure of a person's linked participants (K-IDN-2). Send it through
/// <see cref="IIdentityVault.RevealLinksAsync"/>, which reads the links only after it commits.
/// </summary>
[KernelCapability("identity.reveal.links", VerbClass.Execute, Resource = nameof(PersonId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record RevealPersonLinks(
    TenantId TenantId,
    PersonId PersonId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<PersonId>;
