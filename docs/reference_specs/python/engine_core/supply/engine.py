"""Unified Predictive Supply Chain & DTP Logistics Engine Facade.

Requirements: PRD-SCM-001, PRD-SCM-002, PRD-SYS-001, GMP Annex 13, 21 CFR Part 11
"""


from packages.engine_core.supply.hierarchy import (
    MultiEchelonHierarchyEngine,
)
from packages.engine_core.supply.models import (
    EmergencyUnblindingRequest,
    EmergencyUnblindingResult,
    MultiEchelonNode,
    MultiEchelonRollup,
)
from packages.engine_core.supply.unblinding import (
    EmergencyUnblindingEngine,
)


class PredictiveSupplyEngine:
    """Unified zero-I/O entrypoint for clinical supply hierarchy and emergency unblinding."""

    @staticmethod
    def compute_hierarchy_rollup(
        study_id: str,
        nodes: list[MultiEchelonNode],
    ) -> MultiEchelonRollup:
        """Validates multi-echelon DAG and computes aggregated inventory rollups."""
        return MultiEchelonHierarchyEngine.compute_rollup(study_id, nodes)

    @staticmethod
    def execute_emergency_unblinding(
        request: EmergencyUnblindingRequest,
    ) -> EmergencyUnblindingResult:
        """Evaluates and seals an authorized 21 CFR Part 11 emergency unblinding manifest."""
        return EmergencyUnblindingEngine.execute_unblinding(request)

