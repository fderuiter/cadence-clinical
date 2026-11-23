using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Tenancy;

/// <summary>
/// Reads whether an authenticated person subject holds a <c>TenantMembership</c> in a resolved kernel tenant (K-ID-4).
/// </summary>
public interface ITenantMembershipReader
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="subjectId"/> has an enrolled <c>TenantMembership</c> row
    /// inside <paramref name="tenantId"/>.
    /// </summary>
    Task<bool> HasMembershipAsync(TenantId tenantId, string subjectId, CancellationToken cancellationToken);
}
