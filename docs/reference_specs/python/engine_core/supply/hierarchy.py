"""Multi-Echelon Supply Hierarchy tree validation and recursive inventory rollups.

Requirements: PRD-SCM-001, GMP Annex 13
"""

from packages.engine_core.supply.models import (
    MultiEchelonNode,
    MultiEchelonRollup,
    SupplyNodeType,
)


class InvalidHierarchyError(Exception):
    """Raised when circular references or invalid echelon parent relationships exist."""

    pass


class MultiEchelonHierarchyEngine:
    """Zero-I/O Multi-Echelon Supply Network Tree Evaluator."""

    VALID_PARENT_TIERS: dict[SupplyNodeType, set[SupplyNodeType]] = {
        SupplyNodeType.PRIMARY_MANUFACTURER: {SupplyNodeType.PRIMARY_MANUFACTURER},
        SupplyNodeType.CENTRAL_DEPOT: {
            SupplyNodeType.PRIMARY_MANUFACTURER,
            SupplyNodeType.CENTRAL_DEPOT,
        },
        SupplyNodeType.REGIONAL_DEPOT: {
            SupplyNodeType.PRIMARY_MANUFACTURER,
            SupplyNodeType.CENTRAL_DEPOT,
            SupplyNodeType.REGIONAL_DEPOT,
        },
        SupplyNodeType.CLINICAL_SITE: {
            SupplyNodeType.PRIMARY_MANUFACTURER,
            SupplyNodeType.CENTRAL_DEPOT,
            SupplyNodeType.REGIONAL_DEPOT,
            SupplyNodeType.CLINICAL_SITE,
        },
    }

    @classmethod
    def validate_hierarchy_dag(cls, nodes: list[MultiEchelonNode]) -> None:
        """Validates that supply nodes form a valid Directed Acyclic Graph (DAG).

        Raises:
            InvalidHierarchyError: If cycles exist, invalid parent relationships occur, or duplicate node_ids exist.
        """
        node_map: dict[str, MultiEchelonNode] = {}
        for n in nodes:
            if n.node_id in node_map:
                raise InvalidHierarchyError(f"Duplicate supply node_id: '{n.node_id}'")
            node_map[n.node_id] = n

        # 1. Validate parent references and tier rules
        for n in nodes:
            if n.parent_node_id:
                if n.parent_node_id not in node_map:
                    raise InvalidHierarchyError(
                        f"Supply node '{n.node_id}' references non-existent parent '{n.parent_node_id}'."
                    )
                parent = node_map[n.parent_node_id]
                allowed_parents = cls.VALID_PARENT_TIERS.get(n.node_type, set())
                if parent.node_type not in allowed_parents:
                    raise InvalidHierarchyError(
                        f"Invalid echelon relationship: '{n.node_type}' node '{n.node_id}' cannot have "
                        f"parent '{parent.node_type}' node '{parent.node_id}'."
                    )

        # 2. Cycle detection via DFS
        visited: set[str] = set()
        recursion_stack: set[str] = set()

        def dfs(node_id: str):
            visited.add(node_id)
            recursion_stack.add(node_id)

            curr = node_map[node_id]
            if curr.parent_node_id:
                p_id = curr.parent_node_id
                if p_id not in visited:
                    dfs(p_id)
                elif p_id in recursion_stack:
                    raise InvalidHierarchyError(
                        f"Cycle detected in supply hierarchy involving node '{node_id}' and parent '{p_id}'."
                    )

            recursion_stack.remove(node_id)

        for n in nodes:
            if n.node_id not in visited:
                dfs(n.node_id)

    @classmethod
    def compute_rollup(
        cls,
        study_id: str,
        nodes: list[MultiEchelonNode],
    ) -> MultiEchelonRollup:
        """Validates hierarchy and calculates global and tier-level inventory rollups."""
        cls.validate_hierarchy_dag(nodes)

        total_on_hand = sum(n.on_hand_qty for n in nodes)
        total_reserved = sum(n.reserved_qty for n in nodes)
        total_in_transit = sum(n.in_transit_qty for n in nodes)

        tier_counts: dict[str, int] = {}
        tier_on_hand: dict[str, int] = {}

        for tier in SupplyNodeType:
            tier_counts[tier.value] = 0
            tier_on_hand[tier.value] = 0

        for n in nodes:
            t_val = n.node_type.value
            tier_counts[t_val] += 1
            tier_on_hand[t_val] += n.on_hand_qty

        return MultiEchelonRollup(
            study_id=study_id,
            total_nodes=len(nodes),
            total_on_hand=total_on_hand,
            total_reserved=total_reserved,
            total_in_transit=total_in_transit,
            tier_counts=tier_counts,
            tier_on_hand=tier_on_hand,
            nodes=nodes,
        )
