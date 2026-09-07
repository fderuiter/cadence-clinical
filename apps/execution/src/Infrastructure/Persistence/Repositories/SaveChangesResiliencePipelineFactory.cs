using System;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Polly;
using Polly.Retry;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// Builds the shared <see cref="ResiliencePipeline"/> that <see cref="ExecutionRepositoryBase.SaveChangesAsync"/>
/// wraps around <c>ExecutionDbContext.SaveChangesAsync</c>: up to <see cref="MaxRetryAttempts"/> retries with
/// exponential backoff, scoped specifically to <see cref="DbUpdateConcurrencyException"/>
/// (<c>02_technical_design.md</c> §4.2). One instance is registered in DI (<c>Program.cs</c>) and shared by
/// all three repositories, per the ticket's explicit "do not duplicate the retry wrapper three times"
/// instruction.
///
/// <para>
/// A blind retry alone would be pointless here: re-sending the exact same UPDATE/DELETE with the exact same
/// (now-stale) concurrency-token predicate would just fail identically on every attempt, since nothing about
/// the tracked entity's original/expected values changes between calls. <c>RetryStrategyOptions.OnRetry</c>
/// therefore performs EF Core's own documented "client wins" conflict resolution between attempts: for every
/// entry the failed attempt reports via <c>DbUpdateConcurrencyException.Entries</c>, it re-reads that
/// row's current database values into the entry's <i>original</i> values only — via
/// <c>EntityEntry.GetDatabaseValuesAsync</c> + <c>EntityEntry.OriginalValues.SetValues</c> — leaving the
/// caller's pending edits (the entry's <i>current</i> values) untouched, so the next attempt's
/// concurrency-token <c>WHERE</c> clause targets the row's now-current state instead of the stale one this
/// attempt started with. This mirrors the "Resolving concurrency conflicts" sample in Microsoft's own EF Core
/// documentation (https://learn.microsoft.com/ef/core/saving/concurrency) and is what makes a retry capable of
/// actually succeeding rather than merely repeating a doomed write up to <see cref="MaxRetryAttempts"/> times.
/// If <c>GetDatabaseValuesAsync</c> returns <see langword="null"/> (the row was deleted, not just modified, by
/// the other writer), there is nothing to merge into and the conflict is left for the next attempt to
/// rediscover — a genuinely unresolvable conflict still surfaces as
/// <see cref="DbUpdateConcurrencyException"/> once <see cref="MaxRetryAttempts"/> is exhausted, it is not
/// swallowed.
/// </para>
/// @req:PRD-SUB-001
/// </summary>
public static class SaveChangesResiliencePipelineFactory
{
    /// <summary>
    /// Retries beyond the initial attempt (Polly's own <c>RetryStrategyOptions.MaxRetryAttempts</c>
    /// semantics) — i.e. up to 4 total attempts, matching the ticket's "3 attempts, exponential backoff" AC
    /// read as 3 retries following the first try.
    /// </summary>
    public const int MaxRetryAttempts = 3;

    public static ResiliencePipeline Create() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<DbUpdateConcurrencyException>(),
                MaxRetryAttempts = MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(25),
                UseJitter = true,
                OnRetry = ReloadConflictingEntriesAsync,
            })
            .Build();

    private static async ValueTask ReloadConflictingEntriesAsync(OnRetryArguments<object> args)
    {
        if (args.Outcome.Exception is not DbUpdateConcurrencyException concurrencyException)
        {
            return;
        }

        foreach (var entry in concurrencyException.Entries)
        {
            var databaseValues = await entry.GetDatabaseValuesAsync(args.Context.CancellationToken).ConfigureAwait(false);
            if (databaseValues is not null)
            {
                entry.OriginalValues.SetValues(databaseValues);
            }
        }
    }
}
