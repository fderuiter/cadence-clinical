using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Database.Temporal;

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

    public async Task<IReadOnlyDictionary<SiteCode, SiteComplianceCache>> GetBySitesAsync(
        StudyCode studyCode,
        IReadOnlyCollection<SiteCode> siteCodes,
        string milestone,
        CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        if (siteCodes.Count == 0)
        {
            return new Dictionary<SiteCode, SiteComplianceCache>();
        }

        List<SiteCode> distinctSites = siteCodes.Distinct().ToList();
        Guid? relationalTenantFilter = Context.ActiveRelationalTenantFilter;
        var rows = await Context.SiteComplianceCaches
            .Where(c => c.StudyCode == studyCode
                && c.Milestone == milestone
                && distinctSites.Contains(c.SiteCode)
                && (relationalTenantFilter == null || c.TenantId == relationalTenantFilter))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var bySite = new Dictionary<SiteCode, SiteComplianceCache>(rows.Count);
        foreach (var row in rows)
        {
            bySite.TryAdd(row.SiteCode, row);
        }

        return bySite;
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

    public Task RecordActivationPeriodAsync(
        SiteComplianceCache cache,
        DateTimeOffset recordedAt,
        string? reasonForChange,
        CancellationToken cancellationToken) =>
        RecordActivationPeriodsBatchAsync(
            [cache],
            new HashSet<Guid> { cache.Id },
            recordedAt,
            reasonForChange,
            cancellationToken);

    public async Task RecordActivationPeriodsBatchAsync(
        IReadOnlyList<SiteComplianceCache> caches,
        IReadOnlySet<Guid> existingCacheIds,
        DateTimeOffset recordedAt,
        string? reasonForChange,
        CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        if (caches.Count == 0)
        {
            return;
        }

        var periodsByCacheId = new Dictionary<Guid, List<SiteActivationPeriod>>();
        if (existingCacheIds.Count > 0)
        {
            List<Guid> ids = existingCacheIds.ToList();
            var existingPeriods = await Context.SiteActivationPeriods
                .Where(p => ids.Contains(p.SiteComplianceCacheId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var period in existingPeriods)
            {
                if (!periodsByCacheId.TryGetValue(period.SiteComplianceCacheId, out var list))
                {
                    list = [];
                    periodsByCacheId[period.SiteComplianceCacheId] = list;
                }

                list.Add(period);
            }
        }

        foreach (var cache in caches)
        {
            var activeOpenPeriods = periodsByCacheId.TryGetValue(cache.Id, out var candidates)
                ? candidates.Where(p => p.RecordedPeriod.UpperBoundInfinite && p.ValidPeriod.UpperBoundInfinite).ToList()
                : [];

            DateTimeOffset recordedCutover = recordedAt;
            foreach (var openPeriod in activeOpenPeriods)
            {
                var closed = StandaloneBitemporalModelExtensions.CloseRecordedAt(openPeriod.RecordedPeriod, recordedAt);
                openPeriod.RecordedPeriod = closed;
                if (closed.UpperBound > recordedCutover)
                {
                    recordedCutover = closed.UpperBound;
                }
            }

            var openRecorded = StandaloneBitemporalModelExtensions.CreateOpenPeriod(recordedCutover);
            Guid? effectiveTenantId = Context.ActiveBoundTenantId ?? (cache.TenantId != Guid.Empty ? cache.TenantId : null);

            foreach (var prior in activeOpenPeriods.Where(p => p.ValidPeriod.LowerBound < recordedCutover))
            {
                var closedPrior = new SiteActivationPeriod
                {
                    SiteComplianceCacheId = prior.SiteComplianceCacheId,
                    StudyCode = prior.StudyCode,
                    SiteCode = prior.SiteCode,
                    Milestone = prior.Milestone,
                    IsComplete = prior.IsComplete,
                    MissingDocuments = prior.MissingDocuments.ToList(),
                    IsActivated = prior.IsActivated,
                    ActivatedAt = prior.ActivatedAt,
                    ActivatedBy = prior.ActivatedBy,
                    ReasonForChange = prior.ReasonForChange,
                    ValidPeriod = StandaloneBitemporalModelExtensions.CreateClosedPeriod(prior.ValidPeriod.LowerBound, recordedCutover),
                    RecordedPeriod = openRecorded,
                };
                await AddActivationPeriodAsync(closedPrior, effectiveTenantId, cancellationToken).ConfigureAwait(false);
            }

            var currentPeriod = new SiteActivationPeriod
            {
                SiteComplianceCacheId = cache.Id,
                StudyCode = cache.StudyCode,
                SiteCode = cache.SiteCode,
                Milestone = cache.Milestone,
                IsComplete = cache.IsComplete,
                MissingDocuments = cache.MissingDocuments.ToList(),
                IsActivated = cache.IsActivated,
                ActivatedAt = cache.ActivatedAt,
                ActivatedBy = cache.ActivatedBy,
                ReasonForChange = reasonForChange,
                ValidPeriod = StandaloneBitemporalModelExtensions.CreateOpenPeriod(recordedCutover),
                RecordedPeriod = openRecorded,
            };
            await AddActivationPeriodAsync(currentPeriod, effectiveTenantId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddActivationPeriodAsync(
        SiteActivationPeriod period,
        Guid? effectiveTenantId,
        CancellationToken cancellationToken)
    {
        var entry = await Context.SiteActivationPeriods.AddAsync(period, cancellationToken).ConfigureAwait(false);
        if (effectiveTenantId is { } tenantId && entry.Metadata.FindProperty("tenant_id") is not null)
        {
            entry.Property("tenant_id").CurrentValue = tenantId;
        }
    }

    public async Task<SiteActivationPeriod?> GetPeriodAsOfAsync(
        StudyCode studyCode,
        SiteCode siteCode,
        string milestone,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        Guid? relationalTenantFilter = Context.ActiveRelationalTenantFilter;
        var query = Context.SiteActivationPeriods.AsNoTracking()
            .Where(p => p.StudyCode == studyCode && p.SiteCode == siteCode && p.Milestone == milestone);
        if (relationalTenantFilter is { } tenantId)
        {
            query = query.Where(p => EF.Property<Guid>(p, "tenant_id") == tenantId);
        }

        var matches = await query
            .QueryAsOfAsync(Context, validAt, knownAt, cancellationToken)
            .ConfigureAwait(false);
        return matches.FirstOrDefault();
    }

    private void EnsureVerifiedTenantContext()
    {
        if (Context.HasUnverifiedRequestTenant)
        {
            throw new InvalidOperationException("Verified workspace tenant context is required for site compliance persistence.");
        }
    }
}
