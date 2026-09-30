using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Application.Trust;

/// <summary>
/// Checks that a recorded attestation (spec §9.5) authorizes a governed action: it exists in the tenant, the acting
/// person signed it, it asserts the required meaning, and it is bound to the exact record and content hash, so a
/// signed record cannot silently change (21 CFR 11.70). Protocol approval and withdrawal, standards and library
/// governance, and later locks, reviews and consent call it before changing state. Kernel persistence implements it
/// over the attestation store (<c>AddKernelPersistence</c>).
/// </summary>
public interface IAttestationVerifier
{
    ValueTask<AttestationVerification> VerifyAsync(AttestationRequirement requirement, CancellationToken cancellationToken);
}

/// <summary>What an attestation must satisfy for a governed action.</summary>
/// <param name="Tenant">The tenant of the command; an attestation from another tenant is not found.</param>
/// <param name="AttestationId">The attestation the command names.</param>
/// <param name="Meaning">The meaning it must assert.</param>
/// <param name="Target">The record and content hash it must sign.</param>
/// <param name="Signer">The actor executing the command, who must be the person who signed.</param>
public sealed record AttestationRequirement(
    TenantId Tenant,
    AttestationId AttestationId,
    AttestationMeaning Meaning,
    AttestationTarget Target,
    Actor Signer);

/// <summary>Whether a requirement is met and, when it is not, why (K-AUTH-6).</summary>
public sealed record AttestationVerification(bool Satisfied, string? Reason)
{
    public static AttestationVerification Met { get; } = new(true, null);

    public static AttestationVerification Unmet(string reason) => new(false, reason);
}
