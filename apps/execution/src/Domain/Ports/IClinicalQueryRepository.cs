using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Port for persisting and retrieving <see cref="ClinicalQuery"/> aggregates.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public interface IClinicalQueryRepository
{
    Task<ClinicalQuery?> GetByIdAsync(QueryId id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the aggregate with change tracking for a command that mutates it. <see cref="GetByIdAsync"/> is the
    /// untracked read path; changes made to an aggregate it returns are not persisted by <c>SaveChangesAsync</c>.
    /// </summary>
    Task<ClinicalQuery?> GetForUpdateAsync(QueryId id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads multiple aggregates with change tracking for batch mutation commands.
    /// </summary>
    Task<IReadOnlyList<ClinicalQuery>> GetForUpdateAsync(IEnumerable<QueryId> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Starts an explicit database transaction for atomic batch operations, returning null for in-memory repositories.
    /// </summary>
    Task<System.IAsyncDisposable?> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<ClinicalQuery?> GetActiveQueryOnCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode testCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ClinicalQuery>> ListBySubjectAsync(StudyCode studyId, SubjectCode subjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClinicalQuery>> ListAsync(
        StudyCode? studyId,
        SubjectCode? subjectId,
        string? visitId,
        QueryStatus? status,
        CancellationToken cancellationToken);

    Task AddAsync(ClinicalQuery query, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
