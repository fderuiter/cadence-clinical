using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>Result of verifying one tenant's audit chain.</summary>
/// <param name="IsIntact">True when every check passed.</param>
/// <param name="EntriesChecked">Links verified before the first problem (all of them when intact).</param>
/// <param name="LastHash">Hash of the last verified link (<see cref="KernelAuditHash.Genesis"/> for an empty chain).</param>
/// <param name="BrokenAtSequence">Sequence of the first broken link, when not intact.</param>
/// <param name="Problem">What was wrong, when not intact.</param>
internal sealed record AuditChainVerification(
    bool IsIntact, long EntriesChecked, string LastHash, long? BrokenAtSequence, string? Problem)
{
    public static AuditChainVerification Intact(long entries, string lastHash) => new(true, entries, lastHash, null, null);

    public static AuditChainVerification Broken(long entries, string lastHash, long sequence, string problem) =>
        new(false, entries, lastHash, sequence, problem);
}

/// <summary>
/// Re-derives a tenant's audit chain from the stored rows (spec §5.3 tamper evidence): sequences run 1..n without
/// gaps, each link names its predecessor's hash, each hash recomputes from the stored fields, the chain head matches
/// the last link, and every seal still matches the link it sealed (so a chain cut back below a seal is caught).
/// Runs as an inspector tool and as the first step of <see cref="Audit.KernelAuditChainSealer"/>.
/// @req:PRD-SYS-053
/// </summary>
/// <remarks>
/// The head is read first and only links up to it are checked. A link and its head update commit together, so
/// commands appending while the verifier runs never make an intact chain look broken.
/// </remarks>
internal sealed class KernelAuditChainVerifier(IDbContextFactory<KernelDbContext> contextFactory)
{
    private const int BatchSize = 1000;

    public async Task<AuditChainVerification> VerifyAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var head = await db.AuditChainHeads.AsNoTracking()
            .SingleOrDefaultAsync(h => h.TenantId == tenant, cancellationToken).ConfigureAwait(false);
        var headSequence = head?.LastSequence ?? 0;
        var seals = await db.AuditChainSeals.AsNoTracking()
            .Where(s => s.TenantId == tenant)
            .ToDictionaryAsync(s => s.Sequence, s => s.Hash, cancellationToken).ConfigureAwait(false);

        long checkedCount = 0;
        var expectedPredecessor = KernelAuditHash.Genesis;
        while (checkedCount < headSequence)
        {
            var batch = await db.AuditEntries.AsNoTracking()
                .Where(e => e.TenantId == tenant && e.Sequence > checkedCount && e.Sequence <= headSequence)
                .OrderBy(e => e.Sequence)
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var entry in batch)
            {
                if (Check(entry, checkedCount, expectedPredecessor, seals) is { } problem)
                {
                    return AuditChainVerification.Broken(checkedCount, expectedPredecessor, checkedCount + 1, problem);
                }

                expectedPredecessor = entry.Hash;
                checkedCount++;
            }
        }

        if (checkedCount != headSequence)
        {
            return AuditChainVerification.Broken(
                checkedCount, expectedPredecessor, checkedCount + 1, $"Sequence {checkedCount + 1} is missing.");
        }

        if (!string.Equals(head?.LastHash ?? KernelAuditHash.Genesis, expectedPredecessor, StringComparison.Ordinal))
        {
            return AuditChainVerification.Broken(
                checkedCount, expectedPredecessor, headSequence, "The chain head does not match the last link.");
        }

        var lostSeal = seals.Keys.Where(s => s > headSequence).Order().FirstOrDefault();
        if (lostSeal != 0)
        {
            return AuditChainVerification.Broken(
                checkedCount, expectedPredecessor, lostSeal, $"The chain ends at {headSequence} but was sealed at {lostSeal}.");
        }

        return AuditChainVerification.Intact(checkedCount, expectedPredecessor);
    }

    private static string? Check(
        AuditEntryRecord entry, long checkedCount, string expectedPredecessor, Dictionary<long, string> seals)
    {
        if (entry.Sequence != checkedCount + 1)
        {
            return $"Sequence {checkedCount + 1} is missing.";
        }

        if (!string.Equals(entry.PredecessorHash, expectedPredecessor, StringComparison.Ordinal))
        {
            return "The predecessor hash does not match the previous link.";
        }

        if (!string.Equals(KernelAuditHash.Compute(entry), entry.Hash, StringComparison.Ordinal))
        {
            return "The stored hash does not match the entry's contents.";
        }

        return seals.TryGetValue(entry.Sequence, out var sealedHash) && !string.Equals(sealedHash, entry.Hash, StringComparison.Ordinal)
            ? "The link no longer matches the hash it was sealed with."
            : null;
    }
}
