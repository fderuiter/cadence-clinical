"""
RTSM Randomization Subsystem
Implements pure-Python block, stratified-block, and Pocock-Simon dynamic minimization allocation strategies.
Excludes all scientific computing dependencies (NumPy, SciPy, Pandas).
"""

import random
from abc import ABC, abstractmethod
from typing import Any, Dict, List, Optional, Tuple, Union


class MissingStratificationFactorError(ValueError):
    """Exception raised when a required stratification factor is missing or null."""

    pass


class InvalidRandomizationConfigError(ValueError):
    """Exception raised when the randomization configuration is invalid."""

    pass


def get_random_generator(seed: Optional[int] = None) -> random.Random:
    """Returns a deterministic random generator if seed is provided, otherwise cryptographically secure."""
    if seed is not None:
        return random.Random(seed)
    return random.SystemRandom()


def generate_canonical_stratum_key(
    stratification_factors: Optional[Union[List[str], Dict[str, Any]]],
    covariates: Dict[str, Any],
) -> str:
    """
    Produces a stable, canonical stratum key from configured factors.
    Validates existence of each stratification factor.

    Args:
        stratification_factors: List of factor names or Dict of factors mapping to levels.
        covariates: Subject-specific covariates dictionary.

    Returns:
        A stable, canonical string key, e.g., "age_group=GE_65&gender=F".
    """
    if not stratification_factors:
        return "DEFAULT"

    if isinstance(stratification_factors, dict):
        factor_names = list(stratification_factors.keys())
    elif isinstance(stratification_factors, (list, tuple, set)):
        factor_names = list(stratification_factors)
    else:
        raise InvalidRandomizationConfigError(
            "stratification_factors must be a list, tuple, set, or dictionary"
        )

    sorted_factors = sorted(factor_names)
    parts = []
    for factor in sorted_factors:
        if factor not in covariates or covariates[factor] is None:
            raise MissingStratificationFactorError(
                f"Missing required stratification factor: '{factor}'"
            )
        val = str(covariates[factor]).strip()
        if not val:
            raise MissingStratificationFactorError(
                f"Stratification factor '{factor}' cannot be empty or blank"
            )
        parts.append(f"{factor}={val}")

    return "&".join(parts)


class AllocationStrategy(ABC):
    """Common interface for RTSM treatment allocation strategies."""

    @abstractmethod
    def allocate(
        self,
        arms_ratios: Dict[str, int],
        covariates: Dict[str, Any],
        stratification_factors: Optional[Union[List[str], Dict[str, Any]]] = None,
        state_data: Optional[Dict[str, Any]] = None,
        previous_assignments: Optional[List[Dict[str, Any]]] = None,
        seed: Optional[int] = None,
        **kwargs: Any,
    ) -> Tuple[str, Optional[Dict[str, Any]]]:
        """
        Allocates a treatment arm for a subject.

        Returns:
            A tuple containing:
              - assigned_arm (str)
              - updated_state_data (dict or None)
        """
        pass


class PermutedBlockStrategy(AllocationStrategy):
    """
    Implements permuted-block randomization.
    Honors configured arms, ratios, and block definitions.
    """

    def _validate_inputs(
        self,
        arms_ratios: Dict[str, int],
        block_sizes: Optional[List[int]],
    ) -> int:
        """Validates arms, ratios, and block size compatibility."""
        if not arms_ratios or not isinstance(arms_ratios, dict):
            raise InvalidRandomizationConfigError(
                "arms_ratios must be a non-empty dictionary"
            )

        if len(arms_ratios) < 2:
            raise InvalidRandomizationConfigError(
                "RTSM requires at least two treatment arms"
            )

        for arm, ratio in arms_ratios.items():
            if not isinstance(arm, str) or not arm.strip():
                raise InvalidRandomizationConfigError(
                    "Arm names must be non-empty strings"
                )
            if not isinstance(ratio, int) or ratio <= 0:
                raise InvalidRandomizationConfigError(
                    f"Ratio for arm '{arm}' must be an integer >= 1"
                )

        sum_ratios = sum(arms_ratios.values())

        if block_sizes:
            if not isinstance(block_sizes, list):
                raise InvalidRandomizationConfigError(
                    "block_sizes must be a list of integers"
                )
            for size in block_sizes:
                if not isinstance(size, int) or size <= 0:
                    raise InvalidRandomizationConfigError(
                        "Block sizes must be positive integers"
                    )
                if size % sum_ratios != 0:
                    raise InvalidRandomizationConfigError(
                        f"Block size {size} must be a multiple of the sum of ratios ({sum_ratios})"
                    )
        return sum_ratios

    def allocate(
        self,
        arms_ratios: Dict[str, int],
        covariates: Dict[str, Any],
        stratification_factors: Optional[Union[List[str], Dict[str, Any]]] = None,
        state_data: Optional[Dict[str, Any]] = None,
        previous_assignments: Optional[List[Dict[str, Any]]] = None,
        seed: Optional[int] = None,
        **kwargs: Any,
    ) -> Tuple[str, Optional[Dict[str, Any]]]:
        block_sizes = kwargs.get("block_sizes")
        sum_ratios = self._validate_inputs(arms_ratios, block_sizes)

        # Handle defaulting for block_sizes if empty/None
        if not block_sizes:
            block_sizes = [sum_ratios]

        rng = get_random_generator(seed)

        state = state_data or {}
        sequence = state.get("sequence")
        index = state.get("index", 0)

        # Generate a new block sequence if needed
        if not sequence or index >= len(sequence):
            # Choose a block size
            chosen_size = rng.choice(block_sizes)

            # Construct base pool of allocations
            pool = []
            multiplier = chosen_size // sum_ratios
            for arm, ratio in arms_ratios.items():
                pool.extend([arm] * (ratio * multiplier))

            # Shuffle pool to form block sequence
            rng.shuffle(pool)
            sequence = pool
            index = 0

        assigned_arm = sequence[index]
        updated_state = {"sequence": sequence, "index": index + 1}
        return assigned_arm, updated_state


class StratifiedBlockStrategy(PermutedBlockStrategy):
    """
    Implements stratified permuted-block randomization.
    Enforces canonical stratum key generation and validates stratification factor existence.
    """

    def allocate(
        self,
        arms_ratios: Dict[str, int],
        covariates: Dict[str, Any],
        stratification_factors: Optional[Union[List[str], Dict[str, Any]]] = None,
        state_data: Optional[Dict[str, Any]] = None,
        previous_assignments: Optional[List[Dict[str, Any]]] = None,
        seed: Optional[int] = None,
        **kwargs: Any,
    ) -> Tuple[str, Optional[Dict[str, Any]]]:
        if not stratification_factors:
            raise InvalidRandomizationConfigError(
                "stratification_factors are required for stratified block randomization"
            )
        # Generate and validate stable canonical stratum key
        _ = generate_canonical_stratum_key(stratification_factors, covariates)

        # Delegate sequence state manipulation to the base class
        return super().allocate(
            arms_ratios=arms_ratios,
            covariates=covariates,
            stratification_factors=stratification_factors,
            state_data=state_data,
            previous_assignments=previous_assignments,
            seed=seed,
            **kwargs,
        )


class MinimizationStrategy(AllocationStrategy):
    """
    Implements Pocock-Simon dynamic minimization.
    Evaluates imbalance across configured factors, scales for unequal ratios,
    and applies weighted probability selection.
    """

    def allocate(
        self,
        arms_ratios: Dict[str, int],
        covariates: Dict[str, Any],
        stratification_factors: Optional[Union[List[str], Dict[str, Any]]] = None,
        state_data: Optional[Dict[str, Any]] = None,
        previous_assignments: Optional[List[Dict[str, Any]]] = None,
        seed: Optional[int] = None,
        **kwargs: Any,
    ) -> Tuple[str, Optional[Dict[str, Any]]]:
        if not stratification_factors:
            raise InvalidRandomizationConfigError(
                "stratification_factors are required for minimization strategy"
            )
        if not arms_ratios or not isinstance(arms_ratios, dict):
            raise InvalidRandomizationConfigError(
                "arms_ratios must be a non-empty dictionary"
            )
        if len(arms_ratios) < 2:
            raise InvalidRandomizationConfigError(
                "Minimization requires at least two treatment arms"
            )

        # Validate that all required stratification factors exist in covariates
        if isinstance(stratification_factors, dict):
            factor_names = list(stratification_factors.keys())
        elif isinstance(stratification_factors, (list, tuple, set)):
            factor_names = list(stratification_factors)
        else:
            raise InvalidRandomizationConfigError(
                "stratification_factors must be a list, tuple, set, or dictionary"
            )

        for factor in factor_names:
            if factor not in covariates or covariates[factor] is None:
                raise MissingStratificationFactorError(
                    f"Missing required stratification factor: '{factor}'"
                )
            if not str(covariates[factor]).strip():
                raise MissingStratificationFactorError(
                    f"Stratification factor '{factor}' cannot be empty or blank"
                )

        # Retrieve/validate probability weight p
        p = kwargs.get("probability", 0.8)
        if not isinstance(p, (int, float)) or not (0.0 <= p <= 1.0):
            raise InvalidRandomizationConfigError(
                "probability weight must be a float between 0.0 and 1.0"
            )

        # Retrieve factor weights (default to 1)
        factor_weights = kwargs.get("factor_weights") or {f: 1 for f in factor_names}

        # Initialize previous assignments if None
        assignments = previous_assignments or []

        # Map candidate arms to their computed total imbalance
        arms = sorted(list(arms_ratios.keys()))
        imbalances = {}

        for cand_arm in arms:
            total_dispersion = 0.0
            for factor in factor_names:
                weight = factor_weights.get(factor, 1)
                target_val = covariates[factor]

                # Count previous subjects on each arm with the same value for this factor
                counts = {arm: 0 for arm in arms}
                for assign in assignments:
                    assign_covs = assign.get("covariates") or {}
                    assign_arm = assign.get("assigned_arm")
                    if assign_arm in counts and assign_covs.get(factor) == target_val:
                        counts[assign_arm] += 1

                # Hypothetically add the new subject to cand_arm
                counts[cand_arm] += 1

                # Apply scaling factor for unequal ratios
                scaled_counts = [counts[arm] / arms_ratios[arm] for arm in arms]

                # Compute range-based dispersion: max - min
                dispersion = max(scaled_counts) - min(scaled_counts)
                total_dispersion += weight * dispersion

            imbalances[cand_arm] = total_dispersion

        # Identify arm(s) with minimum imbalance
        min_imbalance = min(imbalances.values())
        best_arms = [arm for arm, imb in imbalances.items() if imb == min_imbalance]
        other_arms = [arm for arm in arms if arm not in best_arms]

        # Calculate assignment probabilities
        probabilities = {}
        if not other_arms:
            # All arms have equal imbalance (including first subject case)
            for arm in arms:
                probabilities[arm] = 1.0 / len(arms)
        else:
            # Distribute probability 'p' among best arms, and '1-p' among the other arms
            for arm in best_arms:
                probabilities[arm] = p / len(best_arms)
            for arm in other_arms:
                probabilities[arm] = (1.0 - p) / len(other_arms)

        # Perform probabilistic selection
        rng = get_random_generator(seed)
        r = rng.random()
        cum_sum = 0.0
        selected_arm = arms[-1]  # fallback
        for arm in arms:
            cum_sum += probabilities[arm]
            if r < cum_sum:
                selected_arm = arm
                break

        return selected_arm, None


class RTSMAllocationEngine:
    """Unified engine to select and execute any of the three allocation strategies."""

    _strategies: Dict[str, AllocationStrategy] = {
        "PERMUTED_BLOCK": PermutedBlockStrategy(),
        "STRATIFIED_BLOCK": StratifiedBlockStrategy(),
        "MINIMIZATION": MinimizationStrategy(),
    }

    @classmethod
    def get_strategy(cls, algorithm_type: str) -> AllocationStrategy:
        """Retrieves the allocation strategy corresponding to algorithm_type."""
        canonical_type = str(algorithm_type).strip().upper()
        if canonical_type not in cls._strategies:
            raise InvalidRandomizationConfigError(
                f"Unsupported or invalid algorithm_type: '{algorithm_type}'. "
                f"Supported types are: {list(cls._strategies.keys())}"
            )
        return cls._strategies[canonical_type]

    @classmethod
    def allocate(
        cls,
        algorithm_type: str,
        arms_ratios: Dict[str, int],
        covariates: Dict[str, Any],
        stratification_factors: Optional[Union[List[str], Dict[str, Any]]] = None,
        state_data: Optional[Dict[str, Any]] = None,
        previous_assignments: Optional[List[Dict[str, Any]]] = None,
        seed: Optional[int] = None,
        **kwargs: Any,
    ) -> Tuple[str, Optional[Dict[str, Any]]]:
        """Convenience method to retrieve strategy and perform allocation."""
        strategy = cls.get_strategy(algorithm_type)
        return strategy.allocate(
            arms_ratios=arms_ratios,
            covariates=covariates,
            stratification_factors=stratification_factors,
            state_data=state_data,
            previous_assignments=previous_assignments,
            seed=seed,
            **kwargs,
        )
