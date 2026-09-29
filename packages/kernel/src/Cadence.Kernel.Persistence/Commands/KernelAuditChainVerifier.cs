using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>Result of verifying one tenant's audit chain.</summary>
internal sealed record AuditChainVerification(bool IsIntact, long EntriesChecked, long? BrokenAtSequence, string? Problem)
{
    public static AuditChainVerification Intact(long entries) => new(true, entries, null, null);

    public static AuditChainVerification Broken(long entries, long sequence, string problem) => new(false, entries, sequence, problem);
}

/// <summary>
/// Re-derives a tenant's audit chain from the stored rows (spec §5.3 tamper evidence): sequences run 1..n without
/// gaps, each link names its predecessor's hash, each hash recomputes from the stored fields, and the chain head
/// matches the last link. Runs as an inspector tool and, later, as the scheduled sealing job's first step.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class KernelAuditChainVerifier(IDbContextFactory<KernelDbContext> contextFactory)
{
    private const int BatchSize = 1000;

    public async Task<AuditChainVerification> VerifyAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        long checkedCount = 0;
        var expectedPredecessor = KernelAuditHash.Genesis;
        while (true)
        {
            var batch = await db.AuditEntries.AsNoTracking()
                .Where(e => e.TenantId == tenant && e.Sequence > checkedCount)
                .OrderBy(e => e.Sequence)
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var entry in batch)
            {
                if (entry.Sequence != checkedCount + 1)
                {
                    return AuditChainVerification.Broken(checkedCount, checkedCount + 1, $"Sequence {checkedCount + 1} is missing.");
                }

                if (!string.Equals(entry.PredecessorHash, expectedPredecessor, StringComparison.Ordinal))
                {
                    return AuditChainVerification.Broken(checkedCount, entry.Sequence, "The predecessor hash does not match the previous link.");
                }

                if (!string.Equals(KernelAuditHash.Compute(entry), entry.Hash, StringComparison.Ordinal))
                {
                    return AuditChainVerification.Broken(checkedCount, entry.Sequence, "The stored hash does not match the entry's contents.");
                }

                expectedPredecessor = entry.Hash;
                checkedCount++;
            }

            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        var head = await db.AuditChainHeads.AsNoTracking()
            .SingleOrDefaultAsync(h => h.TenantId == tenant, cancellationToken).ConfigureAwait(false);
        var headSequence = head?.LastSequence ?? 0;
        var headHash = head?.LastHash ?? KernelAuditHash.Genesis;
        if (headSequence != checkedCount || !string.Equals(headHash, expectedPredecessor, StringComparison.Ordinal))
        {
            return AuditChainVerification.Broken(checkedCount, headSequence, "The chain head does not match the last link (entries removed or appended outside the pipeline).");
        }

        return AuditChainVerification.Intact(checkedCount);
    }
}
