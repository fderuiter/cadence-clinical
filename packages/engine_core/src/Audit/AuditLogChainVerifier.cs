using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Domain.Audit;

/// <summary>The verification outcome for one <see cref="AuditLogEntry.EntityName"/> chain.</summary>
/// <param name="EntityName">The chain key.</param>
/// <param name="Valid">True when every entry's hash recomputes and the entries form one unbroken line.</param>
/// <param name="EntryCount">How many entries carry this chain key.</param>
/// <param name="VerifiedCount">How many entries were reached from the genesis entry and verified.</param>
/// <param name="Error">What broke the chain, when <paramref name="Valid"/> is false.</param>
public sealed record AuditLogChainResult(string EntityName, bool Valid, int EntryCount, int VerifiedCount, string? Error);

/// <summary>
/// Verifies <see cref="AuditLogEntry"/> Merkle chains end to end. Each <see cref="AuditLogEntry.EntityName"/> is
/// its own chain: exactly one genesis entry (empty predecessor), each entry naming the previous entry's
/// <see cref="AuditLogEntry.BlockHash"/>, no forks, no unreachable entries, and every block hash recomputing
/// from the entry's own fields. Historical chains are verified as they are, never rewritten.
/// @req:PRD-SYS-053
/// </summary>
public static class AuditLogChainVerifier
{
    /// <summary>Verifies every chain in <paramref name="entries"/>, one result per entity name.</summary>
    public static IReadOnlyList<AuditLogChainResult> Verify(IEnumerable<AuditLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .GroupBy(e => e.EntityName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => VerifyChain(g.Key, g.ToList()))
            .ToList();
    }

    private static AuditLogChainResult VerifyChain(string entityName, List<AuditLogEntry> chain)
    {
        AuditLogChainResult Broken(int verified, string error) => new(entityName, false, chain.Count, verified, error);

        var bySuccessorOf = chain.ToLookup(e => e.PredecessorHash, StringComparer.Ordinal);
        var genesis = bySuccessorOf[string.Empty].ToList();
        if (genesis.Count != 1)
        {
            return Broken(0, $"expected exactly one genesis entry, found {genesis.Count}");
        }

        AuditLogEntry? current = genesis[0];
        int verified = 0;
        while (current is not null)
        {
            string primaryHash = AuditLogEntry.ComputeBlockHash(
                current.EntityName, current.EntityId, current.Action, AuditLogEntry.NormalizeTimestamp(current.Timestamp), current.UserId,
                current.ReasonForChange, current.VersionIndex, current.ChangesJson, current.PredecessorHash);

            bool isValid = string.Equals(primaryHash, current.BlockHash, StringComparison.OrdinalIgnoreCase);
            if (!isValid)
            {
                string legacyHash = AuditLogEntry.ComputeLegacyBlockHash(
                    current.EntityName, current.EntityId, current.Action, AuditLogEntry.NormalizeTimestamp(current.Timestamp), current.UserId,
                    current.VersionIndex, current.ChangesJson, current.PredecessorHash);
                isValid = string.Equals(legacyHash, current.BlockHash, StringComparison.OrdinalIgnoreCase);
            }

            if (!isValid)
            {
                return Broken(verified, $"block hash mismatch at entry {current.Id}");
            }

            verified++;
            var successors = bySuccessorOf[current.BlockHash].ToList();
            if (successors.Count > 1)
            {
                return Broken(verified, $"chain forks after entry {current.Id} ({successors.Count} successors)");
            }

            current = successors.Count == 1 ? successors[0] : null;
        }

        return verified == chain.Count
            ? new AuditLogChainResult(entityName, true, chain.Count, verified, null)
            : Broken(verified, $"{chain.Count - verified} entries are not reachable from the genesis entry");
    }
}
