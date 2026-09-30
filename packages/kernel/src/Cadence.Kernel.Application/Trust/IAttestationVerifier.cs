using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Application.Trust;

/// <summary>
/// Checks that an Attestation (spec §9.5) authorizes a governed action: it exists in the tenant, was made by the
/// acting person, carries the required meaning, and is bound to the exact target and content hash, so a signed record
/// cannot silently change. Governance commands (standards and library approval, rule-pack release, finding
/// acceptance) call it before changing state. The Trust slice (#4729, #4781) implements it over the kernel's
/// Attestation store; until then a host must register its own, and the kernel refuses to resolve these commands
/// without one.
/// </summary>
public interface IAttestationVerifier
{
    ValueTask<AttestationCheck> VerifyAsync(AttestationRequirement requirement, CancellationToken cancellationToken);
}

/// <summary>What an Attestation must satisfy for a governed action.</summary>
/// <param name="TenantId">The tenant of the command.</param>
/// <param name="AttestationId">The Attestation the command names.</param>
/// <param name="Meaning">The required meaning (<see cref="AttestationMeanings"/>).</param>
/// <param name="TargetKind">The kind of the attested record, for example <c>StandardVersion</c>.</param>
/// <param name="TargetId">The attested record's id.</param>
/// <param name="ContentHash">The attested record's content hash.</param>
/// <param name="Actor">The actor executing the command, who must be the signer.</param>
public sealed record AttestationRequirement(
    TenantId TenantId,
    AttestationId AttestationId,
    string Meaning,
    string TargetKind,
    string TargetId,
    string ContentHash,
    Actor Actor);

/// <summary>The outcome of <see cref="IAttestationVerifier.VerifyAsync"/>. A rejection names what is missing (K-AUTH-6).</summary>
public sealed record AttestationCheck(bool Satisfied, string? Reason = null)
{
    public static AttestationCheck Accept() => new(true);

    public static AttestationCheck Reject(string reason) => new(false, reason);
}

/// <summary>Attestation meanings (spec §9.5) used by kernel governance.</summary>
public static class AttestationMeanings
{
    /// <summary>The signer approves the record for use.</summary>
    public const string Approved = "Approved";

    /// <summary>The signer takes responsibility for the action, used for deprecation and withdrawal.</summary>
    public const string Responsibility = "Responsibility";

    /// <summary>The signer reviewed the record.</summary>
    public const string Reviewed = "Reviewed";
}
