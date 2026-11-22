using System;
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
        StudyCode studyCode, SiteCode? siteCode, string milestone, CancellationToken cancellationToken) =>
        Context.SiteComplianceCaches.FirstOrDefaultAsync(
            c => c.StudyCode == studyCode
                && c.Milestone == milestone
                && siteCode.HasValue && c.SiteCode == siteCode.Value,
            cancellationToken);

    public async Task AddAsync(SiteComplianceCache cache, CancellationToken cancellationToken) =>
        await Context.SiteComplianceCaches.AddAsync(cache, cancellationToken).ConfigureAwait(false);

    public async Task RecordActivationPeriodAsync(
        SiteComplianceCache cache,
        DateTimeOffset recordedAt,
        string? reasonForChange,
        CancellationToken cancellationToken)
    {
        var existingPeriods = await Context.SiteActivationPeriods
            .Where(p => p.SiteComplianceCacheId == cache.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeOpenPeriods = existingPeriods
            .Where(p => p.RecordedPeriod.UpperBoundInfinite && p.ValidPeriod.UpperBoundInfinite)
            .ToList();

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

        foreach (var prior in activeOpenPeriods.Where(p => p.ValidPeriod.LowerBound < recordedCutover))
        {
            await Context.SiteActivationPeriods.AddAsync(new SiteActivationPeriod
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
            }, cancellationToken).ConfigureAwait(false);
        }

        await Context.SiteActivationPeriods.AddAsync(new SiteActivationPeriod
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
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SiteActivationPeriod?> GetPeriodAsOfAsync(
        StudyCode studyCode,
        SiteCode siteCode,
        string milestone,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var matches = await Context.SiteActivationPeriods.AsNoTracking()
            .Where(p => p.StudyCode == studyCode && p.SiteCode == siteCode && p.Milestone == milestone)
            .QueryAsOfAsync(Context, validAt, knownAt, cancellationToken)
            .ConfigureAwait(false);
        return matches.FirstOrDefault();
    }
}
