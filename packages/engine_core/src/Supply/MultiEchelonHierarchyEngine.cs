using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Domain.Supply;

/// <summary>
/// Raised when a supply network is not a valid hierarchy: a duplicate node, a parent that does not exist, a parent in
/// a tier the node may not sit under, or a cycle.
/// </summary>
public sealed class InvalidHierarchyException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public InvalidHierarchyException()
        : base("The supply hierarchy is invalid.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public InvalidHierarchyException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and the exception that caused it.</summary>
    public InvalidHierarchyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Validates a multi-echelon supply network (manufacturer, central depot, regional depot, clinical site) and rolls up
/// its inventory. Port of <c>packages/engine_core/supply/hierarchy.py</c> (#5046); no I/O.
/// </summary>
public static class MultiEchelonHierarchyEngine
{
    /// <summary>
    /// The tiers each tier may sit under. A node may have a parent in its own tier or any tier above it, never below.
    /// </summary>
    public static readonly IReadOnlyDictionary<SupplyNodeType, IReadOnlySet<SupplyNodeType>> ValidParentTiers =
        new Dictionary<SupplyNodeType, IReadOnlySet<SupplyNodeType>>
        {
            [SupplyNodeType.PRIMARY_MANUFACTURER] = new HashSet<SupplyNodeType> { SupplyNodeType.PRIMARY_MANUFACTURER },
            [SupplyNodeType.CENTRAL_DEPOT] = new HashSet<SupplyNodeType>
            {
                SupplyNodeType.PRIMARY_MANUFACTURER,
                SupplyNodeType.CENTRAL_DEPOT,
            },
            [SupplyNodeType.REGIONAL_DEPOT] = new HashSet<SupplyNodeType>
            {
                SupplyNodeType.PRIMARY_MANUFACTURER,
                SupplyNodeType.CENTRAL_DEPOT,
                SupplyNodeType.REGIONAL_DEPOT,
            },
            [SupplyNodeType.CLINICAL_SITE] = new HashSet<SupplyNodeType>
            {
                SupplyNodeType.PRIMARY_MANUFACTURER,
                SupplyNodeType.CENTRAL_DEPOT,
                SupplyNodeType.REGIONAL_DEPOT,
                SupplyNodeType.CLINICAL_SITE,
            },
        };

    /// <summary>
    /// Checks that <paramref name="nodes"/> form a forest: unique node IDs, parents that exist and sit in an allowed
    /// tier, and no cycles. Checks run in that order, so the first problem found is the one reported.
    /// </summary>
    /// <exception cref="InvalidHierarchyException">The network is not a valid hierarchy.</exception>
    public static void ValidateHierarchy(IReadOnlyList<MultiEchelonNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var byId = new Dictionary<string, MultiEchelonNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!byId.TryAdd(node.NodeId, node))
            {
                throw new InvalidHierarchyException($"Duplicate supply node_id: '{node.NodeId}'");
            }
        }

        foreach (var node in nodes.Where(n => !string.IsNullOrEmpty(n.ParentNodeId)))
        {
            if (!byId.TryGetValue(node.ParentNodeId!, out var parent))
            {
                throw new InvalidHierarchyException(
                    $"Supply node '{node.NodeId}' references non-existent parent '{node.ParentNodeId}'.");
            }

            if (!ValidParentTiers[node.NodeType].Contains(parent.NodeType))
            {
                throw new InvalidHierarchyException(
                    $"Invalid echelon relationship: '{node.NodeType}' node '{node.NodeId}' cannot have "
                    + $"parent '{parent.NodeType}' node '{parent.NodeId}'.");
            }
        }

        // Each node has at most one parent, so walking up from every node in list order finds the same cycle the
        // Python depth-first search reported: the first node whose parent is already on the current walk.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in nodes)
        {
            var walk = new HashSet<string>(StringComparer.Ordinal);
            var current = start;
            while (visited.Add(current.NodeId))
            {
                walk.Add(current.NodeId);
                if (string.IsNullOrEmpty(current.ParentNodeId)) break;

                string parentId = current.ParentNodeId;
                if (walk.Contains(parentId))
                {
                    throw new InvalidHierarchyException(
                        $"Cycle detected in supply hierarchy involving node '{current.NodeId}' and parent '{parentId}'.");
                }

                current = byId[parentId];
            }
        }
    }

    /// <summary>
    /// Validates <paramref name="nodes"/> and totals their on-hand, reserved and in-transit quantities, with the node
    /// count and on-hand quantity of every tier (tiers with no nodes report zero).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A node has a storage capacity below 1 or a negative quantity.</exception>
    /// <exception cref="InvalidHierarchyException">The network is not a valid hierarchy.</exception>
    public static MultiEchelonRollup ComputeRollup(string studyId, IReadOnlyList<MultiEchelonNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (var node in nodes)
        {
            ValidateQuantities(node);
        }

        ValidateHierarchy(nodes);

        var tierCounts = Enum.GetValues<SupplyNodeType>().ToDictionary(t => t.ToString(), _ => 0, StringComparer.Ordinal);
        var tierOnHand = new Dictionary<string, int>(tierCounts, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            string tier = node.NodeType.ToString();
            tierCounts[tier]++;
            tierOnHand[tier] += node.OnHandQty;
        }

        return new MultiEchelonRollup(
            StudyId: studyId,
            TotalNodes: nodes.Count,
            TotalOnHand: nodes.Sum(n => n.OnHandQty),
            TotalReserved: nodes.Sum(n => n.ReservedQty),
            TotalInTransit: nodes.Sum(n => n.InTransitQty),
            TierCounts: tierCounts,
            TierOnHand: tierOnHand,
            Nodes: [.. nodes]);
    }

    // The Python model enforced these limits when a node was built (storage_capacity > 0, quantities >= 0); the C#
    // record has no validation, so the engine checks them when it runs, as PredictiveDemandForecastingEngine does.
    private static void ValidateQuantities(MultiEchelonNode node)
    {
        if (node.StorageCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(node), node.StorageCapacity, $"Supply node '{node.NodeId}' needs a storage capacity greater than 0.");
        }

        if (node.OnHandQty < 0 || node.ReservedQty < 0 || node.InTransitQty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(node), $"Supply node '{node.NodeId}' has a negative on-hand, reserved or in-transit quantity.");
        }
    }
}
