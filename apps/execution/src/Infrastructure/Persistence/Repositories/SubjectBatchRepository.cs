using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core adapter for <see cref="ISubjectBatchRepository"/>.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectBatchRepository(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
    : ExecutionRepositoryBase(context, saveChangesResilience), ISubjectBatchRepository
{
    public async Task AddAsync(SubjectBatchImport batch, CancellationToken cancellationToken)
    {
        await Context.SubjectBatchImports.AddAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubjectBatchImport?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await Context.SubjectBatchImports
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SubjectBatchImport?> GetByBatchIdAsync(string batchId, CancellationToken cancellationToken)
    {
        return await Context.SubjectBatchImports
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.BatchId == batchId, cancellationToken)
            .ConfigureAwait(false);
    }
}
