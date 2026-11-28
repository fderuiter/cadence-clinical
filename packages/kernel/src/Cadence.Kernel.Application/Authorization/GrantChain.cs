using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Authorization;

/// <summary>Checks a grant together with every grant above it (K-AUTH-3).</summary>
public static class GrantChain
{
    /// <summary>
    /// True when <paramref name="grant"/> and every grant above it are in effect at <paramref name="at"/> as known at
    /// <paramref name="knownAt"/>. A chain deeper than <see cref="AuthorityEvaluator.MaxDelegationDepth"/>, or one
    /// naming a parent that does not exist, is not in effect.
    /// </summary>
    public static async Task<bool> InEffectAsync(
        IAuthorityStore store, TenantId tenant, Grant grant, DateTimeOffset at, DateTimeOffset knownAt, CancellationToken cancellationToken)
    {
        var current = grant;
        for (var depth = 0; depth <= AuthorityEvaluator.MaxDelegationDepth; depth++)
        {
            if (!current.IsInEffect(at, knownAt))
            {
                return false;
            }

            if (current.ParentGrantId is not { } parentId)
            {
                return true;
            }

            if (await store.FindGrantAsync(tenant, parentId, cancellationToken).ConfigureAwait(false) is not { } parent)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>
    /// Returns the ids of grants in <paramref name="grants"/> that are in effect at <paramref name="at"/> as known at
    /// <paramref name="knownAt"/> together with every grant above them, fetching parent grants in bulk per delegation
    /// level up to <see cref="AuthorityEvaluator.MaxDelegationDepth"/> (K-AUTH-3).
    /// </summary>
    public static async Task<IReadOnlySet<GrantId>> FilterInEffectAsync(
        IAuthorityStore store,
        TenantId tenant,
        IReadOnlyList<Grant> grants,
        DateTimeOffset at,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var active = grants.Where(g => g.IsInEffect(at, knownAt)).ToList();
        if (active.Count == 0)
        {
            return FrozenSet<GrantId>.Empty;
        }

        var byId = new Dictionary<GrantId, Grant>(grants.Count);
        foreach (var grant in grants)
        {
            byId.TryAdd(grant.Id, grant);
        }

        var queried = new HashSet<GrantId>(byId.Keys);
        var frontier = active
            .Where(g => g.ParentGrantId is not null)
            .Select(g => g.ParentGrantId!.Value)
            .Distinct()
            .ToList();

        for (var depth = 0; depth < AuthorityEvaluator.MaxDelegationDepth && frontier.Count > 0; depth++)
        {
            var missing = frontier.Where(id => !queried.Contains(id)).ToList();
            if (missing.Count > 0)
            {
                queried.UnionWith(missing);
                var fetched = await store.FindGrantsAsync(tenant, missing, cancellationToken).ConfigureAwait(false);
                foreach (var (id, parent) in fetched)
                {
                    byId[id] = parent;
                }
            }

            var next = new HashSet<GrantId>();
            foreach (var parentId in frontier)
            {
                if (byId.TryGetValue(parentId, out var parent)
                    && parent.IsInEffect(at, knownAt)
                    && parent.ParentGrantId is { } grandParentId)
                {
                    next.Add(grandParentId);
                }
            }

            frontier = [.. next];
        }

        return active
            .Where(grant => IsChainInEffect(grant, byId, at, knownAt))
            .Select(grant => grant.Id)
            .ToHashSet();
    }

    private static bool IsChainInEffect(Grant grant, IReadOnlyDictionary<GrantId, Grant> byId, DateTimeOffset at, DateTimeOffset knownAt)
    {
        var current = grant;
        for (var depth = 0; depth <= AuthorityEvaluator.MaxDelegationDepth; depth++)
        {
            if (!current.IsInEffect(at, knownAt))
            {
                return false;
            }

            if (current.ParentGrantId is not { } parentId)
            {
                return true;
            }

            if (!byId.TryGetValue(parentId, out var parent))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }
}
