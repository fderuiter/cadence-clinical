using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Persistence.Outbox;

/// <summary>What one relay pass did, over every tenant.</summary>
/// <param name="Dispatched">Messages delivered and marked dispatched.</param>
/// <param name="Failed">Messages whose delivery failed and were scheduled for a retry.</param>
/// <param name="MoreDue">True when some tenant filled its batch without a failure, so another pass should run now.</param>
internal sealed record OutboxRelayResult(int Dispatched, int Failed, bool MoreDue)
{
    public int Claimed => Dispatched + Failed;
}

/// <summary>
/// Delivers committed events from <c>kernel.outbox_messages</c> to <see cref="IKernelEventPublisher"/> (spec §5.1,
/// at-least-once). Each pass visits every tenant in turn through a connection scoped to that tenant (ADR-2250 rule
/// 3), claims up to a batch of its due messages with <c>FOR UPDATE SKIP LOCKED</c>, so several relays never deliver
/// the same row at once, then marks each one dispatched or schedules a retry with exponential backoff.
/// </summary>
/// <remarks>
/// Per-tenant order: a tenant's messages are claimed oldest first, and a failure holds back that tenant's later
/// messages, in the same pass and until the failed one is retried. One relay per deployment keeps each tenant's order
/// exactly; several relays stay correct but may interleave. The relay changes only delivery bookkeeping
/// (<c>dispatched_at</c>, <c>attempts</c>, <c>next_attempt_at</c>, <c>last_error</c>), never kernel truth.
/// </remarks>
internal sealed partial class KernelOutboxRelay(
    KernelTenantDbContextFactory contextFactory,
    IKernelEventPublisher publisher,
    IClock clock,
    ILogger<KernelOutboxRelay> logger)
{
    /// <summary>The longest wait between retries of one message.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    private const int MaxErrorLength = 2000;

    /// <summary>Relays up to <paramref name="batchSize"/> due messages for each tenant.</summary>
    public async Task<OutboxRelayResult> RelayBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var now = KernelTimestamps.ToStoredPrecision(clock.UtcNow);

        int dispatched = 0, failed = 0;
        var moreDue = false;
        foreach (var tenant in await contextFactory.ListTenantsAsync(cancellationToken).ConfigureAwait(false))
        {
            var (tenantDispatched, tenantFailed) = await RelayTenantAsync(tenant, batchSize, now, cancellationToken).ConfigureAwait(false);
            dispatched += tenantDispatched;
            failed += tenantFailed;
            moreDue |= tenantFailed == 0 && tenantDispatched == batchSize;
        }

        return new OutboxRelayResult(dispatched, failed, moreDue);
    }

    private async Task<(int Dispatched, int Failed)> RelayTenantAsync(
        TenantId tenant, int batchSize, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = contextFactory.Create(tenant);
        using var writes = db.PermitPipelineWrites();
#pragma warning disable RS0030 // Kernel contexts register no retrying execution strategy (KernelDatabase.UseKernelNpgsql; KernelExecutionStrategyTests), so EF permits this transaction and the commit pipeline keeps its own conflict handling.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore RS0030

        var due = await db.OutboxMessages
            .FromSql(
                $"""
                SELECT o.* FROM kernel.outbox_messages o
                WHERE o.tenant_id = {tenant.Value}
                  AND o.dispatched_at IS NULL
                  AND (o.next_attempt_at IS NULL OR o.next_attempt_at <= {now})
                  AND NOT EXISTS (
                      SELECT 1 FROM kernel.outbox_messages earlier
                      WHERE earlier.tenant_id = o.tenant_id
                        AND earlier.dispatched_at IS NULL
                        AND earlier.next_attempt_at > {now}
                        AND (earlier.enqueued_at, earlier.id) < (o.enqueued_at, o.id))
                ORDER BY o.enqueued_at, o.id
                LIMIT {batchSize}
                FOR UPDATE OF o SKIP LOCKED
                """)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var heldBack = false;
        int dispatched = 0, failed = 0;
        foreach (var message in due)
        {
            if (heldBack)
            {
                continue;
            }

            try
            {
                var envelope = JsonSerializer.Deserialize<EventEnvelope>(message.Envelope, KernelCommandPipelineJson.Options)
                    ?? throw new JsonException("The stored envelope is null.");
                await publisher.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
                message.DispatchedAt = now;
                message.NextAttemptAt = null;
                message.LastError = null;
                dispatched++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                message.Attempts++;
                message.NextAttemptAt = now + Backoff(message.Attempts);
                message.LastError = Truncate($"{ex.GetType().Name}: {ex.Message}");
                heldBack = true;
                failed++;
                LogDeliveryFailed(logger, ex, message.EventId, message.Type, message.Attempts, message.NextAttemptAt.Value);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (dispatched, failed);
    }

    /// <summary>2^attempts seconds, capped at <see cref="MaxBackoff"/>.</summary>
    internal static TimeSpan Backoff(int attempts) =>
        attempts >= 12 ? MaxBackoff : TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), MaxBackoff.TotalSeconds));

    private static string Truncate(string value) => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivering kernel event {EventId} ({Type}) failed on attempt {Attempts}; retrying at {NextAttemptAt}.")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, Guid eventId, string type, int attempts, DateTimeOffset nextAttemptAt);
}
