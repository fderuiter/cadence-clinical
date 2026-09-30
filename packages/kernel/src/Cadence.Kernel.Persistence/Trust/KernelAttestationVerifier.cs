using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Trust;

/// <summary>
/// Reads the offered attestation through a context bound to the requirement's tenant, so an attestation from another
/// tenant is simply not found (ADR-2250), and compares its signer, meaning and target (spec §9.5). Attestations are
/// insert-only, so reading outside the calling command's transaction cannot see a changed one.
/// </summary>
internal sealed class KernelAttestationVerifier(KernelTenantDbContextFactory contexts) : IAttestationVerifier
{
    public async ValueTask<AttestationVerification> VerifyAsync(AttestationRequirement requirement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        await using var db = contexts.Create(requirement.Tenant);
        var attestation = await db.Set<Attestation>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == requirement.AttestationId, cancellationToken)
            .ConfigureAwait(false);
        if (attestation is null)
        {
            return AttestationVerification.Unmet("The attestation does not exist.");
        }

        return attestation.Mismatch(requirement.Meaning, requirement.Target, requirement.Signer) is { } reason
            ? AttestationVerification.Unmet(reason)
            : AttestationVerification.Met;
    }
}
