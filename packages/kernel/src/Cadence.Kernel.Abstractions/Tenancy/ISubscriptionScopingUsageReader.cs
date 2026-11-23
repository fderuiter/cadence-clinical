using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Tenancy;

/// <summary>
/// Reads tenant-level study, site, and participant counts so the kernel command pipeline can enforce contracted
/// <see cref="SubscriptionScoping"/> caps before committing new study, site, or participant registrations.
/// </summary>
public interface ISubscriptionScopingUsageReader
{
    /// <summary>Counts studies registered in <paramref name="tenantId"/> other than <paramref name="excludingStudyId"/>.</summary>
    Task<int> CountStudiesAsync(TenantId tenantId, StudyId excludingStudyId, CancellationToken cancellationToken);

    /// <summary>Counts sites registered in <paramref name="tenantId"/> other than <paramref name="excludingSiteId"/>.</summary>
    Task<int> CountSitesAsync(TenantId tenantId, SiteId excludingSiteId, CancellationToken cancellationToken);

    /// <summary>Counts participants registered in <paramref name="tenantId"/> other than <paramref name="excludingParticipantId"/>.</summary>
    Task<int> CountParticipantsAsync(TenantId tenantId, ParticipantId excludingParticipantId, CancellationToken cancellationToken);
}
