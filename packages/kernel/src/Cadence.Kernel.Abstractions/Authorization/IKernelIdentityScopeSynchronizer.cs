using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;

namespace Cadence.Kernel.Authorization;

/// <summary>
/// Synchronizes explicit workspace scope attributes (<c>study_ids</c>, <c>site_id</c>) to the external OIDC identity
/// provider when studies are created in a tenant, replacing global wildcard study access (<c>*</c>).
/// </summary>
public interface IKernelIdentityScopeSynchronizer
{
    /// <summary>
    /// Appends <paramref name="studyCode"/> to <paramref name="caller"/>'s explicit <c>study_ids</c> attribute when
    /// <paramref name="caller"/> is a person identity bound to <paramref name="tenantId"/>.
    /// </summary>
    /// <param name="caller">Authenticated kernel actor issuing the study mutation.</param>
    /// <param name="tenantId">Verified kernel tenant owning the study.</param>
    /// <param name="tenantCode">Optional verified tenant code from the gateway/OIDC session.</param>
    /// <param name="studyCode">Unique study code to add to the caller's explicit <c>study_ids</c> scope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task GrantStudyScopeAsync(
        Actor caller,
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        CancellationToken cancellationToken = default);
}
