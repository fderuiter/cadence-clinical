using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core adapter for <see cref="ISubjectRepository"/>. <see cref="EnrollNextIndexAsync"/> implements
/// <c>02_technical_design.md</c> §4.2 exactly: a <see cref="IsolationLevel.RepeatableRead"/> transaction,
/// serialized on PostgreSQL by <c>pg_advisory_xact_lock(hashtext(studyCode))</c> (see
/// <see cref="AdvisoryLockSql"/>), left open on <see cref="ExecutionRepositoryBase.PendingTransaction"/> so the
/// subsequent <see cref="AddAsync"/> + <see cref="ExecutionRepositoryBase.SaveChangesAsync"/> participates in
/// the very same transaction before it commits and releases the lock. The advisory-lock statement itself is
/// PostgreSQL-specific and is skipped entirely when <c>ExecutionDbContext.Database</c> is not Npgsql
/// (SQLite/InMemory in tests) — per this ticket's own accommodation, the method must still be "structurally
/// correct" (compiles, opens/commits its transaction, returns the right sequential index for single-caller
/// cases) against those providers even though only a real PostgreSQL connection can exercise the lock itself.
/// Replaces <c>apps/execution/adapters/repositories.py</c> /
/// <c>apps/execution/infrastructure/repositories/execution_repositories.py</c>.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectRepository(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
    : ExecutionRepositoryBase(context, saveChangesResilience), ISubjectRepository
{
    public async Task<int> EnrollNextIndexAsync(StudyCode studyCode, CancellationToken cancellationToken)
    {
        var transaction = await Context.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // Advisory locks are a PostgreSQL-only concept (no SQLite/InMemory equivalent). Skipping this
            // call under those providers keeps the rest of the method — the transaction/lock *scoping*, and
            // the sequential-index arithmetic — exercisable by this ticket's own unit tests; genuine
            // multi-connection serialization against a real advisory lock is MIG-SUBJ-028's Testcontainers
            // integration test.
            if (Context.Database.IsNpgsql())
            {
                await Context.Database
                    .ExecuteSqlInterpolatedAsync(AdvisoryLockSql.ForStudy(studyCode), cancellationToken)
                    .ConfigureAwait(false);
            }

            int? maxIndex = await Context.ClinicalSubjects.AsNoTracking()
                .Where(s => s.StudyCode == studyCode)
                .Select(s => (int?)s.EnrollmentIndex)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false);

            // Left open deliberately -- see ExecutionRepositoryBase.PendingTransaction and this repository's
            // own doc comment: the caller's subsequent AddAsync + SaveChangesAsync must participate in this
            // same transaction, and SaveChangesAsync is what commits it.
            PendingTransaction = transaction;
            return maxIndex is null ? 0 : maxIndex.Value + 1;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ClinicalSubject?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            return null;
        }

        var subjectId = new SubjectId(guid);

        // Tracked (not AsNoTracking): callers such as SubjectApplicationService.AdvanceStateAsync load via
        // this method, call a mutating domain method on the result, and persist the change through a
        // subsequent SaveChangesAsync with no intervening Attach/Update call (02_technical_design.md §3.2) --
        // that flow only works if the returned entity is tracked by this repository's ExecutionDbContext.
        return await Context.ClinicalSubjects
            .SingleOrDefaultAsync(s => s.Id == subjectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClinicalSubject?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var subjectCode = new SubjectCode(code);

        // Tracked -- see GetByIdAsync's comment; the same load-then-mutate-then-save flow applies here.
        return await Context.ClinicalSubjects
            .SingleOrDefaultAsync(s => s.SubjectCode == subjectCode, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ClinicalSubject subject, CancellationToken cancellationToken) =>
        await Context.ClinicalSubjects.AddAsync(subject, cancellationToken).ConfigureAwait(false);
}
