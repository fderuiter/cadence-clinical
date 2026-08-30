"""Unified Stochastic Randomization Engine Facade.

Requirements: PRD-RTSM-001, PRD-RTSM-002, PRD-SYS-001, 21 CFR Part 11
"""

from collections.abc import Sequence

from packages.engine_core.randomization.blocks import (
    StratifiedBlockAllocator,
    generate_canonical_stratum_key,
)
from packages.engine_core.randomization.boin import (
    BOINDoseEscalationEngine,
    ThreePlusThreeDoseEscalationEngine,
    calculate_boin_boundaries,
    calculate_posterior_prob_toxic,
)
from packages.engine_core.randomization.minimization import (
    PocockSimonMinimizer,
)
from packages.engine_core.randomization.models import (
    BOINConfig,
    BOINDoseRecommendation,
    DoseCohortState,
    DoseLevelDefinition,
    MinimizationConfig,
    StratifiedBlockConfig,
    SubjectAllocationRequest,
    SubjectAllocationResult,
    ThreePlusThreeConfig,
)


class StochasticRandomizationEngine:
    """Unified zero-I/O entrypoint for clinical randomization and dose escalation algorithms."""

    @staticmethod
    def allocate_block(
        config: StratifiedBlockConfig,
        request: SubjectAllocationRequest,
    ) -> SubjectAllocationResult:
        """Allocates subject using Stratified Permuted Block Randomization."""
        return StratifiedBlockAllocator.allocate(config, request)

    @staticmethod
    def allocate_minimization(
        config: MinimizationConfig,
        request: SubjectAllocationRequest,
    ) -> SubjectAllocationResult:
        """Allocates subject using Pocock-Simon Dynamic Covariate Adaptive Minimization."""
        return PocockSimonMinimizer.allocate(config, request)

    @staticmethod
    def evaluate_boin_dose(
        config: BOINConfig,
        dose_levels: Sequence[DoseLevelDefinition],
        current_state: DoseCohortState,
        all_dose_states: Sequence[DoseCohortState] | None = None,
    ) -> BOINDoseRecommendation:
        """Evaluates Bayesian Optimal Interval (BOIN) dose-finding recommendation."""
        return BOINDoseEscalationEngine.evaluate_dose_level(
            config, dose_levels, current_state, all_dose_states
        )

    @staticmethod
    def evaluate_3plus3_dose(
        config: ThreePlusThreeConfig,
        dose_levels: Sequence[DoseLevelDefinition],
        current_state: DoseCohortState,
    ) -> BOINDoseRecommendation:
        """Evaluates classical 3+3 rule-based dose escalation recommendation."""
        return ThreePlusThreeDoseEscalationEngine.evaluate_cohort(
            config, dose_levels, current_state
        )

    @staticmethod
    def compute_stratum_key(
        subject_factors: dict,
        active_factors: list[str] | None,
    ) -> str:
        """Generates canonical alphabetically-sorted stratum key."""
        return generate_canonical_stratum_key(subject_factors, active_factors)

    @staticmethod
    def get_boin_boundaries(target_dlt_rate: float) -> tuple[float, float]:
        """Calculates optimal BOIN escalation/de-escalation boundaries (lambda_e, lambda_d)."""
        return calculate_boin_boundaries(target_dlt_rate)

    @staticmethod
    def get_posterior_toxicity_probability(
        observed_dlts: int,
        evaluated_subjects: int,
        target_dlt_rate: float,
    ) -> float:
        """Calculates Bayesian posterior probability of excessive toxicity."""
        return calculate_posterior_prob_toxic(
            observed_dlts, evaluated_subjects, target_dlt_rate
        )
