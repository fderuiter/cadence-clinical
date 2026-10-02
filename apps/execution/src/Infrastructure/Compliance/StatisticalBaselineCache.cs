using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Sentinels;

using Microsoft.Extensions.Caching.Memory;

namespace Cadence.Apps.Execution.Infrastructure.Compliance;

/// <summary>
/// In-memory caching implementation for study statistical baselines with a short TTL (60s) to keep POST ingestion latency below 15ms.
/// @req:PRD-SYS-174
/// </summary>
public sealed class StatisticalBaselineCache(
    IMemoryCache memoryCache,
    IObservationRepository observationRepository) : IFraudBaselineCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<StatisticalBaseline> GetOrFetchBaselineAsync(StudyCode studyId, CancellationToken cancellationToken)
    {
        string cacheKey = $"fraud_baseline_{studyId.Value}";

        if (memoryCache.TryGetValue(cacheKey, out StatisticalBaseline? cached) && cached is not null)
        {
            return cached;
        }

        var baseline = await observationRepository.GetStudyBaselineStatsAsync(studyId, cancellationToken).ConfigureAwait(false);

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration
        };

        memoryCache.Set(cacheKey, baseline, cacheOptions);

        return baseline;
    }

    public void InvalidateBaseline(StudyCode studyId)
    {
        string cacheKey = $"fraud_baseline_{studyId.Value}";
        memoryCache.Remove(cacheKey);
    }
}
