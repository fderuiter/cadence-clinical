using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.TestSupport.Execution.Fakes;

/// <summary>
/// Slice-local in-memory fake for <see cref="ISubjectRepository"/>, added by `MIG-SUBJ-018` because
/// <c>Cadence.Testing.InMemoryRepository{TEntity,TId}</c>'s generic <c>TId id</c>-keyed CRUD shape does not
/// fit this port's caller-supplied string id-or-code dual-lookup contract or its dedicated
/// <see cref="EnrollNextIndexAsync"/> sequencing method. Lives in <c>tests/Cadence.TestSupport</c>, shared by the
/// unit and integration test projects, rather than <c>packages/testing/src</c>: a fake implementing this
/// execution-slice port needs a project reference to <c>Cadence.Apps.Execution</c>, and adding that reference
/// from the shared <c>Cadence.Testing</c> package reintroduces an <c>Microsoft.EntityFrameworkCore.Relational</c>
/// assembly-version conflict (10.0.4, transitively via <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>, vs the
/// centrally-pinned 10.0.11) as a build warning, which <c>TreatWarningsAsErrors</c> rejects. Reused by
/// `MIG-SUBJ-019` (the service's remaining methods), `MIG-SUBJ-021`
/// (<c>EConsentCompletedConsumer</c>), and `MIG-SUBJ-026` (golden-master fixture replay).
///
/// <para>
/// Deliberately simplified relative to the real EF Core adapter (<c>SubjectRepository</c>,
/// <c>Infrastructure.Persistence.Repositories</c>): <see cref="AddAsync"/> writes immediately rather than
/// staging until <see cref="SaveChangesAsync"/> (a no-op here), and <see cref="EnrollNextIndexAsync"/>
/// performs no real transaction/advisory-lock serialization. This fake exists to test application-service
/// orchestration logic in isolation, not persistence-layer transaction semantics — the real advisory-lock
/// serialization is covered separately by <c>SubjectRepositoryTests</c> and MIG-SUBJ-028's Testcontainers
/// integration tests.
/// </para>
/// </summary>
public sealed class InMemorySubjectRepository : ISubjectRepository
{
    private readonly ConcurrentDictionary<SubjectId, ClinicalSubject> _store = new();

    /// <summary>Runs the unit of work directly: the fake has no transactions to open.</summary>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        System.Func<CancellationToken, Task<TResult>> unitOfWork, CancellationToken cancellationToken) =>
        unitOfWork(cancellationToken);

    public Task<int> EnrollNextIndexAsync(StudyCode studyCode, CancellationToken cancellationToken)
    {
        int? maxIndex = _store.Values
            .Where(s => s.StudyCode == studyCode)
            .Select(s => (int?)s.EnrollmentIndex)
            .Max();
        return Task.FromResult(maxIndex is null ? 0 : maxIndex.Value + 1);
    }

    public Task<ClinicalSubject?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            return Task.FromResult<ClinicalSubject?>(null);
        }

        _store.TryGetValue(new SubjectId(guid), out var subject);
        return Task.FromResult(subject);
    }

    public Task<ClinicalSubject?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult<ClinicalSubject?>(null);
        }

        var match = _store.Values.FirstOrDefault(s => s.SubjectCode.Value == code);
        return Task.FromResult(match);
    }

    public Task<SubjectPage> ListAsync(SubjectListCriteria criteria, CancellationToken cancellationToken)
    {
        string? query = string.IsNullOrWhiteSpace(criteria.Query) ? null : criteria.Query.Trim();
        var matches = _store.Values
            .Where(s => s.StudyCode == criteria.StudyCode)
            .Where(s => criteria.SiteCode is null || s.SiteCode == criteria.SiteCode)
            .Where(s => criteria.AllowedSites is null || (s.SiteCode is { } siteCode && criteria.AllowedSites.Contains(siteCode)))
            .Where(s => criteria.Status is null || s.Status == criteria.Status)
            .Where(s => query is null || s.SubjectCode.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.EnrollmentIndex)
            .ThenBy(s => s.SubjectCode.Value, StringComparer.Ordinal)
            .ToList();

        var page = matches.Skip(criteria.Offset).Take(criteria.Limit).ToList();
        return Task.FromResult(new SubjectPage(page, matches.Count));
    }

    public Task AddAsync(ClinicalSubject subject, CancellationToken cancellationToken)
    {
        _store[subject.Id] = subject;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
