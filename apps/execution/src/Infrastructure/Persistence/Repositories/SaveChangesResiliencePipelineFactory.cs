using System;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Polly;
using Polly.Retry;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// Builds the shared <see cref="ResiliencePipeline"/> that <see cref="ExecutionRepositoryBase.SaveChangesAsync"/>
/// wraps around <c>ExecutionDbContext.SaveChangesAsync</c>: up to <see cref="MaxRetryAttempts"/> retries with
/// exponential backoff, for transient database failures only (<see cref="IsTransient"/>). One instance is
/// registered in DI (<c>Program.cs</c>) and shared by every repository.
///
/// <para>
/// A <see cref="DbUpdateConcurrencyException"/> is never retried (#4766). It means another request changed the
/// same row after this one read it, so replaying the save would overwrite that change without anyone seeing
/// the conflict. The repository turns it into a <c>ConcurrencyConflictException</c>, which the endpoint returns
/// as 409 Conflict. This replaces the earlier "client wins" reload, which silently defeated optimistic
/// concurrency.
/// </para>
/// @req:PRD-SUB-001
/// </summary>
public static class SaveChangesResiliencePipelineFactory
{
    /// <summary>Retries after the first attempt, so up to 4 attempts in total.</summary>
    public const int MaxRetryAttempts = 3;

    public static ResiliencePipeline Create() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransient),
                MaxRetryAttempts = MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(25),
                UseJitter = true,
            })
            .Build();

    /// <summary>
    /// A failure worth retrying: anywhere in the exception chain, an <see cref="NpgsqlException"/> that Npgsql
    /// marks transient (connection failures, serialization failures, deadlocks). A concurrency conflict never is.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return false;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}
