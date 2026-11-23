using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core adapter for <see cref="ISiteComplianceRepository"/>.
/// Replaces <c>apps/execution/adapters/repositories.py</c> (compliance-cache lookup methods).
/// @req:PRD-SUB-001
/// @req:PRD-SYS-192
/// </summary>
public sealed class SiteComplianceRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), ISiteComplianceRepository
{
    // Tracked (not AsNoTracking): SiteComplianceApplicationService.ApplyEtmfWebhookAsync (MIG-SUBJ-020) loads
    // via this method, calls SiteComplianceCache.ApplyWebhookUpdate on the result, and persists the change
    // through a subsequent SaveChangesAsync with no intervening Attach/Update call -- see this method's own
    // interface-level doc comment (ISiteComplianceRepository.GetAsync) and SubjectRepository.GetByIdAsync's
    // identical rationale.
    public Task<SiteComplianceCache?> GetAsync(
        StudyCode studyCode, SiteCode? siteCode, string milestone, CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        Guid? relationalTenantFilter = Context.ActiveRelationalTenantFilter;
        return Context.SiteComplianceCaches.FirstOrDefaultAsync(
            c => c.StudyCode == studyCode
                && c.Milestone == milestone
                && siteCode.HasValue && c.SiteCode == siteCode.Value
                && (relationalTenantFilter == null || c.TenantId == relationalTenantFilter),
            cancellationToken);
    }

    public async Task AddAsync(SiteComplianceCache cache, CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        if (Context.ActiveBoundTenantId is { } tenantId)
        {
            cache.TenantId = tenantId;
        }

        await Context.SiteComplianceCaches.AddAsync(cache, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureVerifiedTenantContext()
    {
        if (Context.HasUnverifiedRequestTenant)
        {
            throw new InvalidOperationException("Verified workspace tenant context is required for site compliance persistence.");
        }
    }
}
