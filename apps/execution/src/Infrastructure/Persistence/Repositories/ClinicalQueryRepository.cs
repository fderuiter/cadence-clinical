using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core 10 repository adapter for <see cref="ClinicalQuery"/> aggregates.
/// All query-only reads strictly enforce <c>.AsNoTracking()</c>.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed class ClinicalQueryRepository(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
    : ExecutionRepositoryBase(context, saveChangesResilience), IClinicalQueryRepository
{
    private static readonly QueryStatus[] ActiveStatuses =
    [
        QueryStatus.Candidate,
        QueryStatus.DraftAi,
        QueryStatus.Open,
        QueryStatus.Answered,
        QueryStatus.Reopened
    ];

    public async Task<ClinicalQuery?> GetByIdAsync(QueryId id, CancellationToken cancellationToken) =>
        await Context.ClinicalQueries
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<ClinicalQuery?> GetForUpdateAsync(QueryId id, CancellationToken cancellationToken) =>
        await Context.ClinicalQueries
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<ClinicalQuery>> GetForUpdateAsync(IEnumerable<QueryId> ids, CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        return await Context.ClinicalQueries
            .Where(q => idList.Contains(q.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<System.IAsyncDisposable?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (Context.Database.ProviderName?.Contains("InMemory") ?? false)
        {
            return null;
        }
        return await Context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClinicalQuery?> GetActiveQueryOnCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode testCode,
        CancellationToken cancellationToken)
    {
        var query = Context.ClinicalQueries
            .AsNoTracking()
            .Where(q => q.StudyId == studyId &&
                        q.SubjectId == subjectId &&
                        q.TestCode == testCode &&
                        ActiveStatuses.Contains(q.Status));

        if (visitId != null)
        {
            query = query.Where(q => q.VisitId == visitId);
        }

        if (domain.HasValue)
        {
            query = query.Where(q => q.Domain == domain.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClinicalQuery>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken) =>
        await Context.ClinicalQueries
            .AsNoTracking()
            .Where(q => q.StudyId == studyId && q.SubjectId == subjectId)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<ClinicalQuery>> ListAsync(
        StudyCode? studyId,
        SubjectCode? subjectId,
        string? visitId,
        QueryStatus? status,
        CancellationToken cancellationToken)
    {
        var query = Context.ClinicalQueries.AsNoTracking().AsQueryable();

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

        return await query
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ClinicalQuery query, CancellationToken cancellationToken)
    {
        await Context.ClinicalQueries.AddAsync(query, cancellationToken).ConfigureAwait(false);
    }
}
