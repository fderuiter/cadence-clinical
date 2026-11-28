using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Persistence.Outbox;

/// <summary>What one relay pass did, over every tenant.</summary>
/// <param name="Dispatched">Messages delivered and marked dispatched.</param>
/// <param name="Failed">Messages whose delivery failed (scheduled for retry or quarantined).</param>
/// <param name="MoreDue">True when some tenant filled its batch without a failure, so another pass should run now.</param>
internal sealed record OutboxRelayResult(int Dispatched, int Failed, bool MoreDue)
{
    public int Claimed => Dispatched + Failed;
}

/// <summary>
/// Delivers committed events from <c>kernel.outbox_messages</c> to <see cref="IKernelEventPublisher"/> (spec §5.1,
/// at-least-once). Each pass visits every tenant in turn through a connection scoped to that tenant (ADR-2250 rule
/// 3), claims up to a batch of its due messages with <c>FOR UPDATE SKIP LOCKED</c>, so several relays never deliver
/// the same row at once, then marks each one dispatched, schedules a retry with exponential backoff, or quarantines
/// terminal poison messages into <c>kernel.outbox_quarantine_messages</c>.
/// </summary>
/// <remarks>
/// Per-aggregate stream order: <c>TenantProvisioned</c> remains a tenant-wide bootstrap barrier, while regular
/// aggregate events are ordered by <c>(tenant_id, stream_key)</c>. A transient failure or dead-letter quarantine on
/// one aggregate stream holds back only later events for that same <c>stream_key</c>, allowing unrelated aggregate
/// streams in the same tenant to continue dispatching without head-of-line blocking.
/// </remarks>
internal sealed partial class KernelOutboxRelay(
    KernelTenantDbContextFactory contextFactory,
    IKernelEventPublisher publisher,
    IClock clock,
    ILogger<KernelOutboxRelay> logger)
{
    /// <summary>The longest wait between retries of one message.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    /// <summary>Maximum delivery attempts before a persistently failing message is quarantined.</summary>
    public const int MaxAttempts = 5;

    private const string TenantBootstrapEventType = "TenantProvisioned";
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
                  AND o.quarantined_at IS NULL
                  AND (o.next_attempt_at IS NULL OR o.next_attempt_at <= {now})
                  AND NOT EXISTS (
                      SELECT 1 FROM kernel.outbox_messages earlier
                      WHERE earlier.tenant_id = o.tenant_id
                        AND earlier.dispatched_at IS NULL
                        AND (earlier.quarantined_at IS NOT NULL OR earlier.next_attempt_at > {now})
                        AND (earlier.type = {TenantBootstrapEventType} OR earlier.stream_key = o.stream_key)
                        AND (earlier.enqueued_at, earlier.id) < (o.enqueued_at, o.id))
                ORDER BY o.enqueued_at, o.id
                LIMIT {batchSize}
                FOR UPDATE OF o SKIP LOCKED
                """)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var tenantBootstrapHeldBack = false;
        var heldBackStreams = new HashSet<string>(StringComparer.Ordinal);
        int dispatched = 0, failed = 0;
        foreach (var message in due)
        {
            var effectiveStreamKey = string.IsNullOrEmpty(message.StreamKey)
                ? $"tenant:{tenant}"
                : message.StreamKey;

            if (tenantBootstrapHeldBack || heldBackStreams.Contains(effectiveStreamKey))
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
                var errorText = Truncate($"{ex.GetType().Name}: {ex.Message}");
                message.LastError = errorText;

                var poisonException = FindPoisonException(ex);
                var isPoison = poisonException is not null
                    || message.Attempts >= MaxAttempts;
                if (isPoison)
                {
                    var quarantineReason = poisonException switch
                    {
                        JsonException => "MalformedEnvelope",
                        KernelOutboxPoisonMessageException => "PoisonConsumerError",
                        _ => "MaxAttemptsExceeded",
                    };
                    message.QuarantinedAt = now;
                    message.NextAttemptAt = null;
                    db.OutboxQuarantineMessages.Add(new OutboxQuarantineMessageRecord
                    {
                        Id = Guid.CreateVersion7(),
                        OutboxMessageId = message.Id,
                        EventId = message.EventId,
                        TenantId = message.TenantId,
                        StreamKey = effectiveStreamKey,
                        Type = message.Type,
                        Envelope = message.Envelope,
                        Attempts = message.Attempts,
                        Reason = quarantineReason,
                        LastError = errorText,
                        EnqueuedAt = message.EnqueuedAt,
                        QuarantinedAt = now,
                    });
                    LogMessageQuarantined(
                        logger,
                        ex,
                        message.EventId,
                        message.Type,
                        effectiveStreamKey,
                        message.Attempts,
                        quarantineReason);
                }
                else
                {
                    message.NextAttemptAt = now + Backoff(message.Attempts);
                    LogDeliveryFailed(logger, ex, message.EventId, message.Type, message.Attempts, message.NextAttemptAt.Value);
                }

                if (string.Equals(message.Type, TenantBootstrapEventType, StringComparison.Ordinal))
                {
                    tenantBootstrapHeldBack = true;
                }
                else
                {
                    heldBackStreams.Add(effectiveStreamKey);
                }

                failed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (dispatched, failed);
    }

    /// <summary>2^attempts seconds, capped at <see cref="MaxBackoff"/>.</summary>
    internal static TimeSpan Backoff(int attempts) =>
        attempts >= 12 ? MaxBackoff : TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), MaxBackoff.TotalSeconds));

    private static Exception? FindPoisonException(Exception exception)
    {
        if (exception is KernelOutboxPoisonMessageException or JsonException)
        {
            return exception;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (FindPoisonException(inner) is { } found)
                {
                    return found;
                }
            }
        }

        return exception.InnerException is { } child ? FindPoisonException(child) : null;
    }

    private static string Truncate(string value) => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivering kernel event {EventId} ({Type}) failed on attempt {Attempts}; retrying at {NextAttemptAt}.")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, Guid eventId, string type, int attempts, DateTimeOffset nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Quarantined poison kernel event {EventId} ({Type}) on stream {StreamKey} after {Attempts} attempt(s) ({Reason}).")]
    private static partial void LogMessageQuarantined(ILogger logger, Exception exception, Guid eventId, string type, string streamKey, int attempts, string reason);
}
