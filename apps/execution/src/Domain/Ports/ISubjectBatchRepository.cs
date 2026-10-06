using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Persistence port for <see cref="SubjectBatchImport"/> staging entities.
/// @req:PRD-SUB-001
/// </summary>
public interface ISubjectBatchRepository
{
    Task AddAsync(SubjectBatchImport batch, CancellationToken cancellationToken);

    Task<SubjectBatchImport?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<SubjectBatchImport?> GetByBatchIdAsync(string batchId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
