using System;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Records an electronic signature (spec §9.5, 21 CFR 11.50, 11.70): the acting person asserts
/// <c>Meaning</c> about the exact content identified by <c>Target</c>. Only a person can sign,
/// and the signature must follow a fresh authentication. The reason for change is stored as the signature's reason.
/// Attestations are append-only.
/// </summary>
/// <remarks>
/// <para><c>AuthenticatedAt</c>: When the signer last authenticated, from the identity provider (<c>auth_time</c>).</para>
/// <para><c>Method</c>: How the signer authenticated, for example the token's <c>amr</c> values (<c>pwd+otp</c>).</para>
/// </remarks>
[KernelCapability("attestation.sign", VerbClass.Execute, InCatalogue = false)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(TrustErrors),
    nameof(TrustErrors.PersonRequired),
    nameof(TrustErrors.MethodRequired),
    nameof(TrustErrors.AuthenticationStale),
    nameof(TrustErrors.TargetKindUnsupported),
    nameof(TrustErrors.TargetUnknown),
    nameof(TrustErrors.TargetNotSignable),
    nameof(TrustErrors.TargetContentChanged))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record Attest(
    TenantId TenantId,
    AttestationId AttestationId,
    AttestationMeaning Meaning,
    AttestationTarget Target,
    DateTimeOffset AuthenticatedAt,
    string Method,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<AttestationId>;

/// <summary>Errors raised by the trust commands.</summary>
public static class TrustErrors
{
    public static readonly KernelError PersonRequired =
        new("K.Trust.PersonRequired", 403, "Only a person can sign an attestation.");

    public static readonly KernelError MethodRequired =
        new("K.Trust.MethodRequired", 422, "The authentication method is required.");

    public static readonly KernelError AuthenticationStale =
        new("K.Trust.AuthenticationStale", 401, "Signing requires a recent authentication; sign in again and retry.");

    public static readonly KernelError TargetKindUnsupported =
        new("K.Trust.TargetKindUnsupported", 422, "Attestations cannot target this kind of record.");

    public static readonly KernelError TargetUnknown =
        new("K.Trust.TargetUnknown", 404, "The attested record does not exist.");

    public static readonly KernelError TargetNotSignable =
        new("K.Trust.TargetNotSignable", 409, "The attested record has no frozen content to sign.");

    public static readonly KernelError TargetContentChanged =
        new("K.Trust.TargetContentChanged", 409, "The content hash does not match the attested record's current content.");
}
