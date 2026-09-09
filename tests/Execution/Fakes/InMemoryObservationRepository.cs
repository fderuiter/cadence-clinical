using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.UnitTests.Execution.Fakes;

/// <summary>
/// In-memory fake implementation of <see cref="IObservationRepository"/> for isolated unit tests.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed class InMemoryObservationRepository : IObservationRepository
{
    private readonly ConcurrentDictionary<ObservationId, ClinicalObservation> _store = new();

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

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
