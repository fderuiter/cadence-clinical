using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.TestSupport.Execution.Fakes;

/// <summary>
/// In-memory fake implementation of <see cref="IClinicalQueryRepository"/> for isolated unit tests.
/// </summary>
public sealed class InMemoryClinicalQueryRepository : IClinicalQueryRepository
{
    private static readonly QueryStatus[] ActiveStatuses =
    [
        QueryStatus.Candidate,
        QueryStatus.DraftAi,
        QueryStatus.Open,
        QueryStatus.Answered,
        QueryStatus.Reopened
    ];

    private readonly ConcurrentDictionary<QueryId, ClinicalQuery> _store = new();

    public Task<ClinicalQuery?> GetForUpdateAsync(QueryId id, CancellationToken cancellationToken) =>
        GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<ClinicalQuery>> GetForUpdateAsync(IEnumerable<QueryId> ids, CancellationToken cancellationToken)
    {
        var idSet = ids.ToHashSet();
        IReadOnlyList<ClinicalQuery> list = _store.Values
            .Where(q => idSet.Contains(q.Id))
            .ToList();
        return Task.FromResult(list);
    }

    public Task<ClinicalQuery?> GetByIdAsync(QueryId id, CancellationToken cancellationToken)
    {
        _store.TryGetValue(id, out var query);
        return Task.FromResult(query);
    }

    public Task<ClinicalQuery?> GetActiveQueryOnCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode testCode,
        CancellationToken cancellationToken)
    {
        ClinicalQuery? match = _store.Values.FirstOrDefault(q =>
            q.StudyId == studyId &&
            q.SubjectId == subjectId &&
            q.TestCode == testCode &&
            ActiveStatuses.Contains(q.Status) &&
            (visitId == null || q.VisitId == visitId) &&
            (!domain.HasValue || q.Domain == domain.Value));

        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<ClinicalQuery>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClinicalQuery> list = _store.Values
            .Where(q => q.StudyId == studyId && q.SubjectId == subjectId)
            .OrderByDescending(q => q.CreatedAt)
            .ToList();

        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<ClinicalQuery>> ListAsync(
        StudyCode? studyId,
        SubjectCode? subjectId,
        string? visitId,
        QueryStatus? status,
        CancellationToken cancellationToken)
    {
        var query = _store.Values.AsEnumerable();

        if (studyId.HasValue)
        {
            query = query.Where(q => q.StudyId == studyId.Value);
        }

        if (subjectId.HasValue)
        {
            query = query.Where(q => q.SubjectId == subjectId.Value);
        }

        if (visitId != null)
        {
            query = query.Where(q => q.VisitId == visitId);
        }

        if (status.HasValue)
        {
            query = query.Where(q => q.Status == status.Value);
        }

        IReadOnlyList<ClinicalQuery> list = query
            .OrderByDescending(q => q.CreatedAt)
            .ToList();

        return Task.FromResult(list);
    }

    public Task AddAsync(ClinicalQuery query, CancellationToken cancellationToken)
    {
        _store[query.Id] = query;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
