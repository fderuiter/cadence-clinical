using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel;

namespace Cadence.TestSupport.Execution.Fakes;

/// <summary>
/// In-memory fake implementation of <see cref="IObservationRepository"/> for isolated unit tests.
/// </summary>
public sealed class InMemoryObservationRepository : IObservationRepository
{
    private readonly ConcurrentDictionary<ObservationId, ClinicalObservation> _store = new();

    public Task<ClinicalObservation?> GetForUpdateAsync(ObservationId id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var observation);
        return Task.FromResult(observation is not null && observation.IsActive ? observation : null);
    }

    public Task<ClinicalObservation?> GetByIdAsync(ObservationId id, CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, includeInactive: false, cancellationToken);

    public Task<ClinicalObservation?> GetByIdAsync(
        ObservationId id,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var observation);
        if (observation is not null && (includeInactive || observation.IsActive))
        {
            return Task.FromResult<ClinicalObservation?>(observation);
        }

        return Task.FromResult<ClinicalObservation?>(null);
    }

    public Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken = default) =>
        ListBySubjectAsync(studyId, subjectId, includeInactive: false, cancellationToken);

    public Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClinicalObservation> list = _store.Values
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId && (includeInactive || o.IsActive))
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
        CancellationToken cancellationToken = default) =>
        ListByCoordinatesAsync(studyId, subjectId, visitId, domain, testCode, includeInactive: false, cancellationToken);

    public Task<IReadOnlyList<ClinicalObservation>> ListByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode? testCode,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = _store.Values
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId && (includeInactive || o.IsActive));

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

    public Task<IReadOnlyList<ClinicalObservation>> ListByFormSubmissionAsync(
        FormSubmissionId formSubmissionId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClinicalObservation> list = _store.Values
            .Where(o => o.FormSubmissionId == formSubmissionId && (includeInactive || o.IsActive))
            .OrderByDescending(o => o.ObservationDate)
            .ToList();

        return Task.FromResult(list);
    }

    public Task DeactivateByFormSubmissionIdAsync(
        FormSubmissionId formSubmissionId,
        CancellationToken cancellationToken = default)
    {
        var matches = _store.Values
            .Where(o => o.FormSubmissionId == formSubmissionId && o.IsActive);

        foreach (var obs in matches)
        {
            obs.Deactivate();
        }

        return Task.CompletedTask;
    }

    public Task AddAsync(ClinicalObservation observation, CancellationToken cancellationToken = default)
    {
        _store[observation.Id] = observation;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
