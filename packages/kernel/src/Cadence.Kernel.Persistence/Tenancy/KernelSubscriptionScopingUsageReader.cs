using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// Counts tenant-isolated studies, sites, and participants in the kernel schema to enforce contracted
/// <see cref="SubscriptionScoping"/> quantitative caps.
/// </summary>
internal sealed class KernelSubscriptionScopingUsageReader(KernelTenantDbContextFactory contexts)
    : ISubscriptionScopingUsageReader
{
    public async Task<int> CountStudiesAsync(
        TenantId tenantId,
        StudyId excludingStudyId,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenantId);
        return await db.Studies
            .AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId && s.Id != excludingStudyId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountSitesAsync(
        TenantId tenantId,
        SiteId excludingSiteId,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenantId);
        return await db.Sites
            .AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId && s.Id != excludingSiteId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountParticipantsAsync(
        TenantId tenantId,
        ParticipantId excludingParticipantId,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenantId);
        return await db.Participants
            .AsNoTracking()
            .CountAsync(p => p.TenantId == tenantId && p.Id != excludingParticipantId, cancellationToken)
            .ConfigureAwait(false);
    }
}
