using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Tenancy;

/// <summary>
/// Resolves the tenant code the gateway sends to a <see cref="TenantId"/>, before any tenant-scoped read (ADR-2250,
/// #4780). This is the only lookup across tenants by code: it returns an id and nothing else about the tenant.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>The id of the active tenant with <paramref name="code"/> (exact, case-sensitive), or null.</summary>
    Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken);
}
