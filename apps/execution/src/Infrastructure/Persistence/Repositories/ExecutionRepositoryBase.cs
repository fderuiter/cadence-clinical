using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;

using Microsoft.EntityFrameworkCore.Storage;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// Shared base for <see cref="SubjectRepository"/>, <see cref="ConsentRepository"/>, and
/// <see cref="SiteComplianceRepository"/>: holds the common scoped <see cref="ExecutionDbContext"/> and
/// exposes a single <see cref="SaveChangesAsync"/> that wraps <c>ExecutionDbContext.SaveChangesAsync</c> in
/// the shared <see cref="ResiliencePipeline"/> (<see cref="SaveChangesResiliencePipelineFactory"/>) — the
/// ticket's explicit "applied uniformly across all three repositories via a shared base class... do not
/// duplicate the retry wrapper three times" requirement.
/// @req:PRD-SUB-001
/// </summary>
public abstract class ExecutionRepositoryBase(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
{
    protected ExecutionDbContext Context { get; } = context;

    /// <summary>
    /// Set by <see cref="SubjectRepository.EnrollNextIndexAsync"/> when it opens the advisory-lock
    /// transaction that must stay open through the caller's subsequent <c>AddAsync</c> — completed
    /// (committed on success, or rolled back via disposal if <see cref="SaveChangesAsync"/> ultimately fails)
    /// by the very next <see cref="SaveChangesAsync"/> call on this same repository instance, so the lock is
    /// held for exactly the read-then-insert span <c>02_technical_design.md</c> §4.2 requires — no longer.
    /// <see langword="private protected"/> rather than <see langword="private"/> so
    /// <see cref="SubjectRepository"/> (the only subclass that ever opens such a transaction) can set it,
    /// while <see cref="ConsentRepository"/>/<see cref="SiteComplianceRepository"/> simply never touch it.
    /// </summary>
    private protected IDbContextTransaction? PendingTransaction { get; set; }

    /// <summary>
    /// Persists all pending changes on <see cref="Context"/>, retrying up to
    /// <see cref="SaveChangesResiliencePipelineFactory.MaxRetryAttempts"/> additional times (with the
    /// "client wins" reload described on <see cref="SaveChangesResiliencePipelineFactory"/>) on a
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>, then commits any transaction
    /// left pending by a prior <see cref="ISubjectRepository"/>-style enrollment call. If the save ultimately
    /// fails (concurrency conflict exhausted, or any other exception), the pending transaction is disposed —
    /// which rolls it back — rather than left open holding its advisory lock indefinitely.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await saveChangesResilience
                .ExecuteAsync(ct => new ValueTask<int>(Context.SaveChangesAsync(ct)), cancellationToken)
                .ConfigureAwait(false);

            if (PendingTransaction is { } transaction)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (PendingTransaction is { } transaction)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                PendingTransaction = null;
            }
        }
    }
}
