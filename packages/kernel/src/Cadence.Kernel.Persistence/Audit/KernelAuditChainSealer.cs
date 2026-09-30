using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Persistence.Audit;

/// <summary>What sealing one tenant's chain did.</summary>
internal enum SealOutcome
{
    /// <summary>The chain was intact and a new seal was written at its head.</summary>
    Sealed,

    /// <summary>The chain was intact and already sealed at its head (or empty).</summary>
    UpToDate,

    /// <summary>Verification failed; no seal was written.</summary>
    Broken,
}

/// <summary>The result of sealing one tenant's chain.</summary>
internal sealed record SealResult(TenantId TenantId, SealOutcome Outcome, AuditChainVerification Verification);

/// <summary>
/// Periodic root sealing of the per-tenant audit chains (spec §5.3): verifies a chain end to end and, when it is
/// intact and has grown, records a seal of its head. A seal is never written over a broken chain, and a broken chain
/// is logged as critical. Runs as the named service account <see cref="SealerActorId"/> (K-TX-5).
/// @req:PRD-SYS-053
/// </summary>
internal sealed partial class KernelAuditChainSealer(
    IDbContextFactory<KernelDbContext> contextFactory,
    KernelAuditChainVerifier verifier,
    IClock clock,
    ILogger<KernelAuditChainSealer> logger)
{
    /// <summary>The service account recorded on every seal.</summary>
    public static readonly string SealerActorId = Actor.ServiceAccount("audit-sealer").Id;

    public async Task<SealResult> SealAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var verification = await verifier.VerifyAsync(tenant, cancellationToken).ConfigureAwait(false);
        if (!verification.IsIntact)
        {
            LogBrokenChain(logger, tenant.Value, verification.BrokenAtSequence, verification.Problem);
            return new SealResult(tenant, SealOutcome.Broken, verification);
        }

        if (verification.EntriesChecked == 0)
        {
            return new SealResult(tenant, SealOutcome.UpToDate, verification);
        }

        var sealedAt = KernelTimestamps.ToStoredPrecision(clock.UtcNow);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO kernel.audit_chain_seals (id, tenant_id, sequence, hash, sealed_at, sealed_by)
            VALUES ({Guid.CreateVersion7()}, {tenant.Value}, {verification.EntriesChecked}, {verification.LastHash}, {sealedAt}, {SealerActorId})
            ON CONFLICT (tenant_id, sequence) DO NOTHING
            """,
            cancellationToken).ConfigureAwait(false);

        return new SealResult(tenant, inserted == 1 ? SealOutcome.Sealed : SealOutcome.UpToDate, verification);
    }

    /// <summary>Seals every tenant that has an audit chain. One tenant's failure does not stop the others.</summary>
    public async Task<IReadOnlyList<SealResult>> SealAllAsync(CancellationToken cancellationToken)
    {
        List<TenantId> tenants;
        await using (var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            tenants = await db.AuditChainHeads.AsNoTracking().Select(h => h.TenantId).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var results = new List<SealResult>(tenants.Count);
        foreach (var tenant in tenants)
        {
            results.Add(await SealAsync(tenant, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Audit chain for tenant {Tenant} is broken at sequence {Sequence}: {Problem}. It was not sealed.")]
    private static partial void LogBrokenChain(ILogger logger, Guid tenant, long? sequence, string? problem);
}
