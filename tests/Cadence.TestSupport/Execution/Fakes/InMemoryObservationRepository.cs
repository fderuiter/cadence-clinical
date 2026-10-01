using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

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
        CancellationToken cancellationToken) =>
        ListBySubjectAsync(studyId, subjectId, includeInactive: false, cancellationToken);

    public Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = _store.Values
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId);

        if (!includeInactive)
        {
            query = query.Where(o => o.IsActive);
        }

        IReadOnlyList<ClinicalObservation> list = query
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
        CancellationToken cancellationToken) =>
        ListByCoordinatesAsync(studyId, subjectId, visitId, domain, testCode, includeInactive: false, cancellationToken);

    public Task<IReadOnlyList<ClinicalObservation>> ListByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode? testCode,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = _store.Values
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId);

        if (!includeInactive)
        {
            query = query.Where(o => o.IsActive);
        }

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

    public Task<IReadOnlyList<ClinicalObservation>> ListByFormSubmissionIdAsync(
        FormSubmissionId formSubmissionId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _store.Values
            .Where(o => o.FormSubmissionId == formSubmissionId);

        if (!includeInactive)
        {
            query = query.Where(o => o.IsActive);
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
