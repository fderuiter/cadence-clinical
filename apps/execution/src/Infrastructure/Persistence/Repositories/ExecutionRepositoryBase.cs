using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Database;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// Shared base for the Execution repositories: holds the common scoped <see cref="ExecutionDbContext"/>, saves
/// through one <see cref="SaveChangesAsync"/>, and runs explicit transactions through
/// <see cref="ExecuteInTransactionAsync{TResult}"/>. Transient failures are retried by the context's own retrying
/// execution strategy (<c>EnableRetryOnFailure</c> in <c>Program.cs</c>), which replaced a Polly pipeline around
/// <c>SaveChangesAsync</c> that could not retry a save inside an open transaction (#5383).
/// @req:PRD-SUB-001
/// </summary>
public abstract class ExecutionRepositoryBase(ExecutionDbContext context)
{
    protected ExecutionDbContext Context { get; } = context;

    /// <summary>
    /// Persists all pending changes on <see cref="Context"/>. A concurrency conflict is never retried: it is
    /// rethrown as <see cref="ConcurrencyConflictException"/> (409) so the other request's change is not
    /// overwritten (#4766).
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException conflict)
        {
            throw new ConcurrencyConflictException(
                conflict.Entries.Select(e => e.Metadata.ClrType.Name).Distinct(StringComparer.Ordinal).ToList(),
                conflict);
        }
    }

    /// <summary>
    /// Runs <paramref name="unitOfWork"/> in one transaction inside the context's execution strategy
    /// (<see cref="CadenceTransactions.ExecuteInTransactionAsync{TResult}(DbContext, Func{CancellationToken, Task{TResult}}, System.Data.IsolationLevel, CancellationToken)"/>).
    /// On a transient failure the whole unit of work runs again from the start.
    /// </summary>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> unitOfWork, CancellationToken cancellationToken) =>
        Context.ExecuteInTransactionAsync(unitOfWork, cancellationToken: cancellationToken);
}
