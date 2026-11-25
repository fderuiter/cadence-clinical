using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Database.Temporal;

namespace Cadence.TestSupport.Execution.Fakes;

/// <summary>
/// Slice-local in-memory fake for <see cref="ISiteComplianceRepository"/>, added by `MIG-SUBJ-018` alongside
/// <see cref="InMemorySubjectRepository"/>/<see cref="InMemoryConsentRepository"/> purely to satisfy
/// <c>SubjectApplicationService</c>'s primary-constructor shape in tests that never exercise the Rule 2
/// compliance gate (<c>CreateAsync</c>/<c>RecordConsentAsync</c> never call this port at all). <see cref="Seed"/>
/// lets `MIG-SUBJ-019`'s <c>AdvanceStateAsync</c>/SCN-005 tests and `MIG-SUBJ-020`'s
/// <c>SiteComplianceApplicationService</c> tests preconfigure rows. See
/// <see cref="InMemorySubjectRepository"/>'s doc comment for why this lives under
/// <c>tests/Cadence.TestSupport</c> rather than <c>packages/testing/src</c>.
/// </summary>
public sealed class InMemorySiteComplianceRepository : ISiteComplianceRepository
{
    private readonly ConcurrentDictionary<Guid, SiteComplianceCache> _store = new();
    private readonly List<SiteActivationPeriod> _periods = [];

    public IReadOnlyList<SiteActivationPeriod> Periods => _periods;

    public int SaveChangesCallCount { get; private set; }

    public void Seed(SiteComplianceCache cache) => _store[cache.Id] = cache;

    public Task<SiteComplianceCache?> GetAsync(StudyCode studyCode, SiteCode? siteCode, string milestone, CancellationToken cancellationToken)
    {
        if (siteCode is not { } site)
        {
            return Task.FromResult<SiteComplianceCache?>(null);
        }

        var match = _store.Values.FirstOrDefault(c => c.StudyCode == studyCode && c.SiteCode == site && c.Milestone == milestone);
        return Task.FromResult(match);
    }

    public Task<IReadOnlyDictionary<SiteCode, SiteComplianceCache>> GetBySitesAsync(
        StudyCode studyCode,
        IReadOnlyCollection<SiteCode> siteCodes,
        string milestone,
        CancellationToken cancellationToken)
    {
        var requested = siteCodes.ToHashSet();
        var dict = new Dictionary<SiteCode, SiteComplianceCache>();
        foreach (var cache in _store.Values)
        {
            if (cache.StudyCode == studyCode && cache.Milestone == milestone && requested.Contains(cache.SiteCode))
            {
                dict.TryAdd(cache.SiteCode, cache);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<SiteCode, SiteComplianceCache>>(dict);
    }

    public Task AddAsync(SiteComplianceCache cache, CancellationToken cancellationToken)
    {
        _store[cache.Id] = cache;
        return Task.CompletedTask;
    }

    public Task RecordActivationPeriodAsync(
        SiteComplianceCache cache,
        DateTimeOffset recordedAt,
        string? reasonForChange,
        CancellationToken cancellationToken)
    {
        var activeOpen = _periods
            .Where(p => p.SiteComplianceCacheId == cache.Id && p.RecordedPeriod.UpperBoundInfinite && p.ValidPeriod.UpperBoundInfinite)
            .ToList();

        DateTimeOffset recordedCutover = recordedAt;
        foreach (var open in activeOpen)
        {
            var closed = StandaloneBitemporalModelExtensions.CloseRecordedAt(open.RecordedPeriod, recordedAt);
            open.RecordedPeriod = closed;
            if (closed.UpperBound > recordedCutover)
            {
                recordedCutover = closed.UpperBound;
            }
        }

        var openRecorded = StandaloneBitemporalModelExtensions.CreateOpenPeriod(recordedCutover);
        foreach (var prior in activeOpen.Where(p => p.ValidPeriod.LowerBound < recordedCutover))
        {
            _periods.Add(new SiteActivationPeriod
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
            });
        }

        _periods.Add(new SiteActivationPeriod
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
        });

        return Task.CompletedTask;
    }

    public Task<SiteActivationPeriod?> GetPeriodAsOfAsync(
        StudyCode studyCode,
        SiteCode siteCode,
        string milestone,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var match = _periods.FirstOrDefault(p =>
            p.StudyCode == studyCode
            && p.SiteCode == siteCode
            && p.Milestone == milestone
            && StandaloneBitemporalModelExtensions.ContainsInstant(p.ValidPeriod, validAt)
            && StandaloneBitemporalModelExtensions.ContainsInstant(p.RecordedPeriod, knownAt));
        return Task.FromResult(match);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
