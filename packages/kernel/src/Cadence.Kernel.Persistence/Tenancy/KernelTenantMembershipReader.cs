using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// Queries <c>kernel.tenant_memberships</c> under Row-Level Security for a pseudonymous OIDC subject (matching the
/// deterministic self-service onboarding person UUID, the development demo person UUID, or a directly mapped UUID subject).
/// </summary>
internal sealed class KernelTenantMembershipReader(KernelTenantDbContextFactory contexts) : ITenantMembershipReader
{
    public async Task<bool> HasMembershipAsync(
        TenantId tenantId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        if (tenantId.IsEmpty || string.IsNullOrWhiteSpace(subjectId))
        {
            return false;
        }

        string trimmed = subjectId.Trim();
        PersonId derivedTrialPerson = DeriveTrialPersonId(trimmed);
        PersonId derivedDemoPerson = DeriveDemoPersonId(tenantId, trimmed);
        PersonId? directPerson = Guid.TryParse(trimmed, out var parsed)
            ? PersonId.FromGuid(parsed)
            : null;

        await using var db = contexts.Create(tenantId);
        return await db.TenantMemberships
            .AsNoTracking()
            .AnyAsync(
                m => m.TenantId == tenantId
                    && (m.PersonId == derivedTrialPerson
                        || m.PersonId == derivedDemoPerson
                        || (directPerson != null && m.PersonId == directPerson.Value)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static PersonId DeriveDemoPersonId(TenantId tenantId, string subject) =>
        DeriveUuidV8PersonId($"cadence-development-demo:{tenantId}:person:{subject}");

    private static PersonId DeriveTrialPersonId(string subject) =>
        DeriveUuidV8PersonId($"cadence-trial-onboarding:{subject}:person:founder");

    private static PersonId DeriveUuidV8PersonId(string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(seed), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return PersonId.FromGuid(new Guid(bytes, bigEndian: true));
    }
}
