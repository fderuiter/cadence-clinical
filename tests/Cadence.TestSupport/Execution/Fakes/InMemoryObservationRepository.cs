using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Sentinels;

namespace Cadence.TestSupport.Execution.Fakes;

/// <summary>
/// In-memory fake implementation of <see cref="IObservationRepository"/> for isolated unit tests.
/// </summary>
public sealed class InMemoryObservationRepository : IObservationRepository
{
    private readonly ConcurrentDictionary<ObservationId, ClinicalObservation> _store = new();

    public Task<ClinicalObservation?> GetForUpdateAsync(ObservationId id, CancellationToken cancellationToken) =>
        GetByIdAsync(id, cancellationToken);

    public Task<ClinicalObservation?> GetByIdAsync(ObservationId id, CancellationToken cancellationToken)
    {
        _store.TryGetValue(id, out var observation);
        return Task.FromResult(observation);
    }

    public Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClinicalObservation> list = _store.Values
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId)
            .OrderByDescending(o => o.ObservationDate)
            .ToList();

        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<ClinicalObservation>> ListByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode? testCode,
        CancellationToken cancellationToken)
    {
        var query = _store.Values
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId);

        if (visitId != null)
        {
            query = query.Where(o => o.VisitId == visitId);
        }

        if (domain.HasValue)
        {
            query = query.Where(o => o.Domain == domain.Value);
        }

        if (testCode.HasValue)
        {
            query = query.Where(o => o.TestCode == testCode.Value);
        }

        IReadOnlyList<ClinicalObservation> list = query
            .OrderByDescending(o => o.ObservationDate)
            .ToList();

        return Task.FromResult(list);
    }

    public Task AddAsync(ClinicalObservation observation, CancellationToken cancellationToken)
    {
        _store[observation.Id] = observation;
        return Task.CompletedTask;
    }

    public Task<StatisticalBaseline> GetStudyBaselineStatsAsync(StudyCode studyId, CancellationToken cancellationToken)
    {
        var obsList = _store.Values
            .Where(o => o.StudyId == studyId && o.Value.HasValue)
            .ToList();

        var digitCounts = new Dictionary<int, long>();
        for (int d = 1; d <= 9; d++)
        {
            digitCounts[d] = 0;
        }

        long totalDigits = 0;
        var fieldStatsDict = new Dictionary<string, FieldBaselineStats>(System.StringComparer.OrdinalIgnoreCase);

        var groupedByTest = obsList.GroupBy(o => o.TestCode.Value, System.StringComparer.OrdinalIgnoreCase);
        foreach (var group in groupedByTest)
        {
            var values = group.Where(o => o.Value.HasValue).Select(o => o.Value!.Value).ToList();
            if (values.Count > 0)
            {
                double mean = values.Average();
                double variance = values.Count > 1 ? values.Sum(v => System.Math.Pow(v - mean, 2)) / (values.Count - 1) : 0.0;
                fieldStatsDict[group.Key] = new FieldBaselineStats(mean, variance, values.Count);

                foreach (var v in values)
                {
                    int digit = Cadence.Sentinels.Checks.BenfordDigitPreferenceSentinelCheck.ExtractLeadingDigit(v);
                    if (digit >= 1 && digit <= 9)
                    {
                        digitCounts[digit]++;
                        totalDigits++;
                    }
                }
            }
        }

        return Task.FromResult(new StatisticalBaseline(digitCounts, totalDigits, fieldStatsDict));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
