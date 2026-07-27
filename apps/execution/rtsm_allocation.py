"""RTSM Randomization Strategies Module.

This module provides a unified interface and pure-Python implementations for the
following randomization algorithms:
1. Permuted Block Randomization (unstratified)
2. Stratified Block Randomization
3. Pocock-Simon Dynamic Minimization

All algorithms avoid scientific-computing dependencies (such as NumPy or Pandas)
and run entirely on standard-library primitives. They utilize cryptographically
secure randomness (CSPRNG via random.SystemRandom) by default, while supporting
deterministic seeding (via random.Random) for GxP-compliant reproducibility.
"""

import random
from typing import Any, Dict, List, Optional, Tuple, Union
from pydantic import BaseModel, Field, model_validator


class RandomizationConfigSchema(BaseModel):
    """Configuration schema for RTSM randomization algorithms.

    Performs strict validation of ratios, block sizes, stratification factors,
    and Pocock-Simon parameters to prevent runtime deviations.
    """

    algorithm_type: str = Field(
        ...,
        description="Type of algorithm. Must be 'PERMUTED_BLOCK', 'STRATIFIED_BLOCK', or 'MINIMIZATION'.",
    )
    arms_ratios: Dict[str, int] = Field(
        ...,
        description="Mapping of treatment arm names to target ratios.",
    )
    stratification_factors: Optional[Union[List[str], Dict[str, List[str]]]] = Field(
        default=None,
        description="Active stratification factors as list of names or dict of names to allowed categories.",
    )
    block_sizes: Optional[List[int]] = Field(
        default=None,
        description="Allowed block sizes for block-based algorithms.",
    )
    factor_weights: Optional[Dict[str, float]] = Field(
        default=None,
        description="Optional weights for stratification factors under minimization.",
    )
    p_preferred: float = Field(
        default=0.8,
        description="Pocock-Simon biased coin probability for assigning to the preferred arm.",
    )
    seed: Optional[int] = Field(
        default=None,
        description="Optional seed for deterministic, reproducible allocations.",
    )

    @model_validator(mode="after")
    def validate_config(self) -> "RandomizationConfigSchema":
        """Validates configuration parameters for mathematical and logical correctness.

        Raises:
            ValueError: If configurations are invalid or incompatible.
        """
        # 1. Validate algorithm_type
        allowed_types = {"PERMUTED_BLOCK", "STRATIFIED_BLOCK", "MINIMIZATION"}
        if self.algorithm_type not in allowed_types:
            raise ValueError(
                f"Invalid algorithm_type: '{self.algorithm_type}'. Allowed types are {allowed_types}."
            )

        # 2. Validate arms_ratios
        if not self.arms_ratios:
            raise ValueError("arms_ratios must not be empty.")
        for arm, ratio in self.arms_ratios.items():
            if not arm or not arm.strip():
                raise ValueError("Arm names in arms_ratios must be non-empty strings.")
            if ratio <= 0:
                raise ValueError(f"Ratio for arm '{arm}' must be a positive integer.")

        ratio_sum = sum(self.arms_ratios.values())

        # 3. Validate block-based parameters
        if self.algorithm_type in ("PERMUTED_BLOCK", "STRATIFIED_BLOCK"):
            if self.block_sizes is None:
                self.block_sizes = [ratio_sum]

            if not self.block_sizes:
                raise ValueError("block_sizes list cannot be empty for block randomization.")

            for size in self.block_sizes:
                if size <= 0:
                    raise ValueError(f"Block size {size} must be a positive integer.")
                if size % ratio_sum != 0:
                    raise ValueError(
                        f"Block size {size} must be a multiple of the sum of ratios ({ratio_sum})."
                    )

        # 4. Validate stratification factors
        if self.algorithm_type == "STRATIFIED_BLOCK":
            if not self.stratification_factors:
                raise ValueError(
                    "stratification_factors must be provided and non-empty for STRATIFIED_BLOCK."
                )
        elif self.algorithm_type == "PERMUTED_BLOCK":
            if self.stratification_factors:
                self.stratification_factors = None

        # 5. Validate minimization parameters
        if self.algorithm_type == "MINIMIZATION":
            if not self.stratification_factors:
                raise ValueError(
                    "stratification_factors must be provided and non-empty for MINIMIZATION."
                )
            if not (0.5 <= self.p_preferred <= 1.0):
                raise ValueError("p_preferred must be a float between 0.5 and 1.0 inclusive.")
            if self.factor_weights:
                active_factors_list = (
                    list(self.stratification_factors.keys())
                    if isinstance(self.stratification_factors, dict)
                    else list(self.stratification_factors)
                )
                for factor, weight in self.factor_weights.items():
                    if factor not in active_factors_list:
                        raise ValueError(
                            f"Factor weight key '{factor}' not found in stratification_factors."
                        )
                    if weight < 0:
                        raise ValueError(f"Factor weight for '{factor}' must be non-negative.")

        return self


def generate_canonical_stratum_key(
    subject_factors: Optional[Dict[str, Any]],
    active_factors: Optional[List[str]],
) -> str:
    """Generates a stable, canonical stratum key from stratification factors.

    The active factors are sorted alphabetically to guarantee stable, canonical representation.

    Args:
        subject_factors: Subject-level baseline covariate dictionary.
        active_factors: Active stratification factor names.

    Returns:
        The canonical stratum key string, or 'DEFAULT' if no factors are active.

    Raises:
        ValueError: If subject factors are missing or required factor values are absent.
    """
    if not active_factors:
        return "DEFAULT"

    sorted_factors = sorted(active_factors)

    if not subject_factors:
        raise ValueError(
            "Subject stratification factors are missing, but stratification is configured."
        )

    parts = []
    for factor in sorted_factors:
        if factor not in subject_factors or subject_factors[factor] is None:
            raise ValueError(f"Missing required stratification factor value for: '{factor}'")
        val = str(subject_factors[factor]).strip()
        parts.append(f"{factor}={val}")

    return "|".join(parts)


def allocate_block(
    sequence: Optional[List[str]],
    block_index: int,
    arms_ratios: Dict[str, int],
    block_sizes: List[int],
    rng: Union[random.Random, random.SystemRandom],
) -> Tuple[str, List[str], int]:
    """Allocates a treatment arm using permuted block randomization.

    Args:
        sequence: The existing block sequence, or None/empty if not yet initialized.
        block_index: The current block index in the sequence.
        arms_ratios: Target allocation ratios per arm.
        block_sizes: Allowed block sizes.
        rng: The random number generator instance to use for deterministic choice/shuffling.

    Returns:
        A tuple of (allocated_arm, updated_sequence, updated_block_index).
    """
    ratio_sum = sum(arms_ratios.values())

    # Generate a new shuffled block if sequence is missing, index is invalid, or exhausted
    if not sequence or block_index < 0 or block_index >= len(sequence):
        # 1. Choose block size from block_sizes
        block_size = rng.choice(block_sizes)

        # 2. Build block treatments list according to arms and ratios
        new_block = []
        for arm, ratio in arms_ratios.items():
            count = int(block_size * ratio / ratio_sum)
            new_block.extend([arm] * count)

        # 3. Shuffle block deterministically using rng
        rng.shuffle(new_block)

        # 4. Update sequence and reset index
        sequence = new_block
        block_index = 0

    allocated_arm = sequence[block_index]
    updated_block_index = block_index + 1

    return allocated_arm, sequence, updated_block_index


def allocate_minimization(
    subject_factors: Dict[str, Any],
    previous_allocations: List[Dict[str, Any]],
    arms_ratios: Dict[str, int],
    active_factors: List[str],
    factor_weights: Optional[Dict[str, float]],
    p_preferred: float,
    rng: Union[random.Random, random.SystemRandom],
) -> str:
    """Allocates a treatment arm using Pocock-Simon dynamic minimization.

    Evaluates baseline covariates of previous allocations, calculates imbalance ranges,
    normalizes counts by target ratios to handle uneven allocation ratios, and applies
    factor weights.

    Args:
        subject_factors: Dict of the new subject's factors/covariates.
        previous_allocations: List of previous allocations (each with keys 'strat_factors' or
            'subject_factors', and 'allocation').
        arms_ratios: Target allocation ratios per arm.
        active_factors: List of active stratification factor names.
        factor_weights: Optional dictionary of weights per stratification factor.
        p_preferred: Pocock-Simon biased coin probability for assigning to the preferred arm.
        rng: The random number generator instance.

    Returns:
        The selected treatment arm (str).

    Raises:
        ValueError: If a required stratification factor is missing from subject_factors.
    """
    weights = factor_weights or {}
    arms = list(arms_ratios.keys())
    K = len(arms)

    # Imbalance score for each potential arm assignment
    imbalance_scores: Dict[str, float] = {}

    for candidate_arm in arms:
        total_imbalance = 0.0

        # Sum imbalance over all active stratification factors
        for factor in active_factors:
            new_subj_val = subject_factors.get(factor)
            if new_subj_val is None:
                raise ValueError(
                    f"Subject is missing value for stratification factor: '{factor}'"
                )

            # 1. Compute baseline counts of previous subjects having the same factor value
            baseline_counts = {arm: 0 for arm in arms}
            for prev in previous_allocations:
                prev_factors = (
                    prev.get("strat_factors")
                    or prev.get("subject_factors")
                    or prev.get("factors")
                    or {}
                )
                prev_arm = prev.get("allocation")
                if prev_arm in baseline_counts and prev_factors.get(factor) == new_subj_val:
                    baseline_counts[prev_arm] += 1

            # 2. Add the hypothetical assignment to candidate_arm
            hypothetical_counts = {arm: count for arm, count in baseline_counts.items()}
            hypothetical_counts[candidate_arm] += 1

            # 3. Normalize counts by ratios to handle uneven allocation ratios
            normalized_counts = [
                hypothetical_counts[arm] / arms_ratios[arm] for arm in arms
            ]

            # 4. Calculate imbalance range
            imbalance_range = max(normalized_counts) - min(normalized_counts)

            # 5. Apply factor weight
            factor_weight = weights.get(factor, 1.0)
            total_imbalance += imbalance_range * factor_weight

        imbalance_scores[candidate_arm] = total_imbalance

    # Find the minimum score(s)
    min_score = min(imbalance_scores.values())

    # Identify best arms (with the minimum score) and other arms
    best_arms = [arm for arm, score in imbalance_scores.items() if score == min_score]
    other_arms = [arm for arm, score in imbalance_scores.items() if score > min_score]

    M = len(best_arms)
    O = len(other_arms)

    # Compute selection probabilities
    # Best arms share p_preferred equally
    # Other arms share (1 - p_preferred) equally
    probabilities = {}
    if O == 0:
        # All arms are tied
        for arm in arms:
            probabilities[arm] = 1.0 / K
    else:
        for arm in best_arms:
            probabilities[arm] = p_preferred / M
        for arm in other_arms:
            probabilities[arm] = (1.0 - p_preferred) / O

    # Choose arm based on computed probabilities
    population = list(probabilities.keys())
    weights_list = [probabilities[arm] for arm in population]

    selected_arm = rng.choices(population, weights=weights_list, k=1)[0]
    return selected_arm


class RTSMAllocator:
    """Unified allocator for RTSM randomization algorithms.

    Provides a clean, unified strategy interface for selecting and executing
    permuted block, stratified block, and Pocock-Simon minimization allocation strategies.
    """

    def __init__(self, config: RandomizationConfigSchema) -> None:
        """Initializes the RTSMAllocator with the provided configuration.

        Args:
            config: A validated RandomizationConfigSchema object.
        """
        self.config = config
        if config.seed is not None:
            self.rng = random.Random(config.seed)
        else:
            self.rng = random.SystemRandom()

    def allocate(
        self,
        subject_factors: Optional[Dict[str, Any]] = None,
        sequence: Optional[List[str]] = None,
        block_index: int = 0,
        previous_allocations: Optional[List[Dict[str, Any]]] = None,
    ) -> Dict[str, Any]:
        """Performs treatment allocation based on the configured strategy.

        Args:
            subject_factors: Dict of stratification factors for the subject (required for stratified/minimization)
            sequence: For block-based strategies, the current block sequence from database
            block_index: For block-based strategies, the current block index in the sequence
            previous_allocations: For minimization strategy, a list of dicts with previous allocations

        Returns:
            A dictionary containing:
            - "allocation" (str): Assigned treatment arm.
            - "stratum_key" (str): Canonical stratum key (or "DEFAULT").
            - "updated_sequence" (List[str], optional): Only returned for block-based strategies.
            - "updated_block_index" (int, optional): Only returned for block-based strategies.

        Raises:
            ValueError: If required inputs are missing or invalid for the selected algorithm.
        """
        # Determine active stratification factors
        active_factors = []
        if self.config.stratification_factors:
            if isinstance(self.config.stratification_factors, dict):
                active_factors = list(self.config.stratification_factors.keys())
            else:
                active_factors = list(self.config.stratification_factors)

        # 1. Generate canonical stratum key
        if self.config.algorithm_type == "PERMUTED_BLOCK":
            # Unstratified block randomization ignores subject_factors
            stratum_key = "DEFAULT"
        else:
            stratum_key = generate_canonical_stratum_key(subject_factors, active_factors)

        # 2. Route to appropriate algorithm
        if self.config.algorithm_type in ("PERMUTED_BLOCK", "STRATIFIED_BLOCK"):
            if self.config.block_sizes is None:
                raise ValueError("block_sizes must be configured for block-based randomization.")

            allocated_arm, updated_seq, updated_idx = allocate_block(
                sequence=sequence,
                block_index=block_index,
                arms_ratios=self.config.arms_ratios,
                block_sizes=self.config.block_sizes,
                rng=self.rng,
            )
            return {
                "allocation": allocated_arm,
                "stratum_key": stratum_key,
                "updated_sequence": updated_seq,
                "updated_block_index": updated_idx,
            }

        elif self.config.algorithm_type == "MINIMIZATION":
            if previous_allocations is None:
                previous_allocations = []
            if subject_factors is None:
                raise ValueError("subject_factors must be provided for minimization.")

            allocated_arm = allocate_minimization(
                subject_factors=subject_factors,
                previous_allocations=previous_allocations,
                arms_ratios=self.config.arms_ratios,
                active_factors=active_factors,
                factor_weights=self.config.factor_weights,
                p_preferred=self.config.p_preferred,
                rng=self.rng,
            )
            return {
                "allocation": allocated_arm,
                "stratum_key": stratum_key,
            }

        else:
            raise ValueError(f"Unsupported algorithm type: '{self.config.algorithm_type}'")
