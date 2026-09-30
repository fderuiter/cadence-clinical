using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Execution;

/// <summary>
/// Reads which protocol versions are in force at a study site through a context bound to the caller's tenant
/// (ADR-2250), resolving the study's effectivities with <see cref="EffectivityResolution"/>. Read-only: no tracking.
/// </summary>
internal sealed class KernelEffectivityReader(KernelTenantDbContextFactory contexts) : IEffectivityReader
{
    public async Task<IReadOnlyList<EffectiveVersion>> EffectiveAtAsync(
        TenantId tenant, StudySiteId studySite, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var site = await (
                from s in db.StudySites.AsNoTracking()
                join located in db.Sites.AsNoTracking() on s.SiteId equals located.Id
                where s.TenantId == tenant && s.Id == studySite
                select new { s.StudyId, located.Country })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (site is null)
        {
            return [];
        }

        var effectivities = await db.Effectivities.AsNoTracking()
            .Where(e => e.TenantId == tenant && e.StudyId == site.StudyId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var deciding = EffectivityResolution.EffectiveAt(effectivities, studySite, site.Country, validAt, knownAt);
        if (deciding.Count == 0)
        {
            return [];
        }

        var versionIds = deciding.Select(e => e.ProtocolVersionId).ToList();
        var numbers = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Number, cancellationToken).ConfigureAwait(false);
        return [.. deciding
            .Select(e => new EffectiveVersion(e.ProtocolVersionId, numbers.GetValueOrDefault(e.ProtocolVersionId), e.Id, e.ScopeLevel))
            .OrderByDescending(v => v.Number)];
    }
}
