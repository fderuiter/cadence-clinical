using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cadence.Database;

/// <summary>
/// The one way to run an explicit database transaction (#5383). <see cref="CadenceDatabaseExtensions.AddCadencePostgresDbContext{TDbContext}"/>
/// registers a retrying execution strategy, and EF Core refuses a user-initiated transaction under it unless the whole
/// unit of work runs inside <see cref="IExecutionStrategy.ExecuteAsync{TState, TResult}(TState, Func{DbContext, TState, CancellationToken, Task{TResult}}, Func{DbContext, TState, CancellationToken, Task{ExecutionResult{TResult}}}, CancellationToken)"/>.
/// <c>BeginTransaction</c> and <c>BeginTransactionAsync</c> are banned everywhere else (<c>BannedSymbols.txt</c>).
/// </summary>
public static class CadenceTransactions
{
    /// <summary>
    /// Runs <paramref name="unitOfWork"/> in one transaction, inside the context's execution strategy, and commits it
    /// when the delegate returns; an exception rolls it back. On a transient failure the strategy runs the delegate
    /// again from the start with a cleared change tracker and no pending audit actions, so the delegate must load what it changes and make every
    /// change itself, never rely on entities tracked before the call. When a transaction is already open (a caller
    /// further up, or MassTransit's outbox) the delegate joins it and that owner commits. A provider without
    /// transactions (the in-memory provider in unit tests) runs the delegate directly.
    /// </summary>
    /// <param name="db">The context the unit of work uses.</param>
    /// <param name="unitOfWork">The work, which saves its own changes.</param>
    /// <param name="isolationLevel">The transaction's isolation level.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <typeparam name="TResult">What the work returns.</typeparam>
    /// <returns>The delegate's result, after the commit.</returns>
    public static async Task<TResult> ExecuteInTransactionAsync<TResult>(
        this DbContext db,
        Func<CancellationToken, Task<TResult>> unitOfWork,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
        {
            return await unitOfWork(cancellationToken).ConfigureAwait(false);
        }

        int attempt = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            unitOfWork,
            async (context, work, ct) =>
            {
                if (attempt++ > 0)
                {
                    // Changes and audit actions the failed attempt staged were never committed; the retry starts from
                    // the database and records its own.
                    context.ChangeTracker.Clear();
                    _ = AuditActions.Drain(context);
                }

#pragma warning disable RS0030 // The one sanctioned call: this method is the execution-strategy wrapper the ban points to.
                await using IDbContextTransaction transaction = await context.Database
                    .BeginTransactionAsync(isolationLevel, ct)
                    .ConfigureAwait(false);
#pragma warning restore RS0030
                TResult result = await work(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="unitOfWork"/> in one transaction, as
    /// <see cref="ExecuteInTransactionAsync{TResult}(DbContext, Func{CancellationToken, Task{TResult}}, IsolationLevel, CancellationToken)"/> does.
    /// </summary>
    /// <param name="db">The context the unit of work uses.</param>
    /// <param name="unitOfWork">The work, which saves its own changes.</param>
    /// <param name="isolationLevel">The transaction's isolation level.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes after the commit.</returns>
    public static Task ExecuteInTransactionAsync(
        this DbContext db,
        Func<CancellationToken, Task> unitOfWork,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        return db.ExecuteInTransactionAsync(
            async ct =>
            {
                await unitOfWork(ct).ConfigureAwait(false);
                return true;
            },
            isolationLevel,
            cancellationToken);
    }
}
