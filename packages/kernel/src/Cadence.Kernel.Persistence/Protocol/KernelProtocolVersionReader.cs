using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Protocol;

/// <summary>
/// Reads protocol versions through a context bound to the caller's tenant (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelProtocolVersionReader(KernelTenantDbContextFactory contexts) : IProtocolVersionReader
{
    public async Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var versions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.StudyId == study)
            .OrderByDescending(v => v.Number)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. versions.Select(Summary)];
    }

    public async Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.ProtocolVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.TenantId == tenant && v.Id == version, cancellationToken).ConfigureAwait(false);
        return found is null ? null : Summary(found);
    }

    public async Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
    {
        var design = await GetDesignAsync(tenant, version, cancellationToken).ConfigureAwait(false);
        return design is not null ? ScheduleOfActivities.Compile(design) : null;
    }

    public async Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var content = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.Id == version)
            .Select(v => v.Content)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return content is not null ? ProtocolDesignDocument.Read(content).Design : null;
    }

    private static ProtocolVersionSummary Summary(ProtocolVersion version) =>
        new(
            version.Id,
            version.StudyId,
            version.Number,
            version.ParentVersionId,
            version.Status,
            version.ContentHash,
            version.Amendment?.Intent,
            version.ApprovalAttestationId,
            version.WithdrawalReason);
}
