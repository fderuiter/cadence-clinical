"""RTSM Randomization Engines.

This module provides a common allocation interface and pure-Python strategy
implementations for permuted-block, stratified-block, and Pocock-Simon dynamic minimization.
"""

import abc
import random
from typing import Any

from pydantic import BaseModel, Field, model_validator


class RandomizationConfigSchema(BaseModel):
    """Configuration schema for RTSM randomization algorithms."""

    algorithm_type: str = Field(
        ...,
        description="Type of algorithm. Must be 'PERMUTED_BLOCK', 'STRATIFIED_BLOCK', or 'MINIMIZATION'.",
    )
    arms_ratios: dict[str, int] = Field(
        ...,
        description="Mapping of treatment arm names to target ratios.",
    )
    stratification_factors: list[str] | dict[str, list[str]] | None = Field(
        default=None,
        description="Active stratification factors as list of names or dict of names to allowed categories.",
    )
    block_sizes: list[int] | None = Field(
        default=None,
        description="Allowed block sizes for block-based algorithms.",
    )
    factor_weights: dict[str, float] | None = Field(
        default=None,
        description="Optional weights for stratification factors under minimization.",
    )
    p_preferred: float = Field(
        default=0.8,
        description="Pocock-Simon biased coin probability for assigning to the preferred arm.",
    )
    seed: int | None = Field(
        default=None,
        description="Optional seed for deterministic, reproducible allocations.",
    )

    @model_validator(mode="after")
    def validate_config(self) -> RandomizationConfigSchema:
        """Validates configuration parameters."""
        allowed_types = {"PERMUTED_BLOCK", "STRATIFIED_BLOCK", "MINIMIZATION"}
        if self.algorithm_type not in allowed_types:
            raise ValueError(
                f"Invalid algorithm_type: '{self.algorithm_type}'. Allowed types are {allowed_types}."
            )

        if not self.arms_ratios:
            raise ValueError("arms_ratios must not be empty.")
        for arm, ratio in self.arms_ratios.items():
            if not arm or not arm.strip():
                raise ValueError("Arm names in arms_ratios must be non-empty strings.")
            if ratio <= 0:
                raise ValueError(f"Ratio for arm '{arm}' must be a positive integer.")

        ratio_sum = sum(self.arms_ratios.values())

        if self.algorithm_type in ("PERMUTED_BLOCK", "STRATIFIED_BLOCK"):
            if self.block_sizes is None:
                self.block_sizes = [ratio_sum]
            if not self.block_sizes:
                raise ValueError(
                    "block_sizes list cannot be empty for block randomization."
                )
            for size in self.block_sizes:
                if size <= 0:
                    raise ValueError(f"Block size {size} must be a positive integer.")
                if size % ratio_sum != 0:
                    raise ValueError(
                        f"Block size {size} must be a multiple of the sum of ratios ({ratio_sum})."
                    )

        if self.algorithm_type == "STRATIFIED_BLOCK":
            if not self.stratification_factors:
                raise ValueError(
                    "stratification_factors must be provided and non-empty for STRATIFIED_BLOCK."
                )
        elif self.algorithm_type == "PERMUTED_BLOCK":
            if self.stratification_factors:
                self.stratification_factors = None

        if self.algorithm_type == "MINIMIZATION":
            if not self.stratification_factors:
                raise ValueError(
                    "stratification_factors must be provided and non-empty for MINIMIZATION."
                )
            if not (0.5 <= self.p_preferred <= 1.0):
                raise ValueError(
                    "p_preferred must be a float between 0.5 and 1.0 inclusive."
                )
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
                        raise ValueError(
                            f"Factor weight for '{factor}' must be non-negative."
                        )

        return self


def generate_canonical_stratum_key(
    subject_factors: dict[str, Any] | None,
    active_factors: list[str] | None,
) -> str:
    """Generates a stable, canonical stratum key from stratification factors."""
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
            raise ValueError(
                f"Missing required stratification factor value for: '{factor}'"
            )
        val = str(subject_factors[factor]).strip()
        parts.append(f"{factor}={val}")

    return "|".join(parts)


class RandomizationStrategy(abc.ABC):
    """Abstract base class for all allocation strategies."""

    def __init__(self, config: RandomizationConfigSchema) -> None:
        self.config = config
        if config.seed is not None:
            self.rng = random.Random(config.seed)
        else:
            self.rng = random.SystemRandom()

    @abc.abstractmethod
    def allocate(
        self,
        subject_factors: dict[str, Any] | None = None,
        sequence: list[str] | None = None,
        block_index: int = 0,
        previous_allocations: list[dict[str, Any]] | None = None,
    ) -> dict[str, Any]:
        """Performs treatment allocation based on the concrete strategy."""
        pass


class BlockAllocationBase(RandomizationStrategy):
    """Base strategy for block-based allocations (Permuted & Stratified)."""

    def _allocate_block(
        self,
        sequence: list[str] | None,
        block_index: int,
    ) -> tuple[str, list[str], int]:
        ratio_sum = sum(self.config.arms_ratios.values())
        block_sizes = self.config.block_sizes or [ratio_sum]

        # Generate a new shuffled block if sequence is missing or exhausted
        if not sequence or block_index < 0 or block_index >= len(sequence):
            block_size = self.rng.choice(block_sizes)
            new_block = []
            for arm, ratio in self.config.arms_ratios.items():
                count = int(block_size * ratio / ratio_sum)
                new_block.extend([arm] * count)

            self.rng.shuffle(new_block)
            sequence = new_block
            block_index = 0

        allocated_arm = sequence[block_index]
        updated_block_index = block_index + 1

        return allocated_arm, sequence, updated_block_index


class PermutedBlockStrategy(BlockAllocationBase):
    """Strategy for unstratified permuted block allocation."""

    def allocate(
        self,
        subject_factors: dict[str, Any] | None = None,
        sequence: list[str] | None = None,
        block_index: int = 0,
        previous_allocations: list[dict[str, Any]] | None = None,
    ) -> dict[str, Any]:
        allocated_arm, updated_seq, updated_idx = self._allocate_block(
            sequence=sequence,
            block_index=block_index,
        )
        return {
            "allocation": allocated_arm,
            "stratum_key": "DEFAULT",
            "updated_sequence": updated_seq,
            "updated_block_index": updated_idx,
        }


class StratifiedBlockStrategy(BlockAllocationBase):
    """Strategy for stratified block allocation."""

    def allocate(
        self,
        subject_factors: dict[str, Any] | None = None,
        sequence: list[str] | None = None,
        block_index: int = 0,
        previous_allocations: list[dict[str, Any]] | None = None,
    ) -> dict[str, Any]:
        active_factors = []
        if self.config.stratification_factors:
            if isinstance(self.config.stratification_factors, dict):
                active_factors = list(self.config.stratification_factors.keys())
            else:
                active_factors = list(self.config.stratification_factors)

        stratum_key = generate_canonical_stratum_key(subject_factors, active_factors)
        allocated_arm, updated_seq, updated_idx = self._allocate_block(
            sequence=sequence,
            block_index=block_index,
        )
        return {
            "allocation": allocated_arm,
            "stratum_key": stratum_key,
            "updated_sequence": updated_seq,
            "updated_block_index": updated_idx,
        }


class PocockSimonMinimizationStrategy(RandomizationStrategy):
    """Strategy for Pocock-Simon dynamic minimization."""

    def allocate(
        self,
        subject_factors: dict[str, Any] | None = None,
        sequence: list[str] | None = None,
        block_index: int = 0,
        previous_allocations: list[dict[str, Any]] | None = None,
    ) -> dict[str, Any]:
        if subject_factors is None:
            raise ValueError("subject_factors must be provided for minimization.")
        if previous_allocations is None:
            previous_allocations = []

        active_factors = []
        if self.config.stratification_factors:
            if isinstance(self.config.stratification_factors, dict):
                active_factors = list(self.config.stratification_factors.keys())
            else:
                active_factors = list(self.config.stratification_factors)

        stratum_key = generate_canonical_stratum_key(subject_factors, active_factors)

        weights = self.config.factor_weights or {}
        arms = list(self.config.arms_ratios.keys())
        k_arms = len(arms)

        # Imbalance score for each potential arm assignment
        imbalance_scores: dict[str, float] = {}

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
                    if (
                        prev_arm in baseline_counts
                        and prev_factors.get(factor) == new_subj_val
                    ):
                        baseline_counts[prev_arm] += 1

                # 2. Add the hypothetical assignment to candidate_arm
                hypothetical_counts = {
                    arm: count for arm, count in baseline_counts.items()
                }
                hypothetical_counts[candidate_arm] += 1

                # 3. Normalize counts by ratios to handle uneven allocation ratios
                normalized_counts = [
                    hypothetical_counts[arm] / self.config.arms_ratios[arm]
                    for arm in arms
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
        best_arms = [
            arm for arm, score in imbalance_scores.items() if score == min_score
        ]
        other_arms = [
            arm for arm, score in imbalance_scores.items() if score > min_score
        ]

        m_best = len(best_arms)
        num_other = len(other_arms)

        # Compute selection probabilities
        probabilities = {}
        if num_other == 0:
            for arm in arms:
                probabilities[arm] = 1.0 / k_arms
        else:
            for arm in best_arms:
                probabilities[arm] = self.config.p_preferred / m_best
            for arm in other_arms:
                probabilities[arm] = (1.0 - self.config.p_preferred) / num_other

        # Choose arm based on computed probabilities
        population = list(probabilities.keys())
        weights_list = [probabilities[arm] for arm in population]

        selected_arm = self.rng.choices(population, weights=weights_list, k=1)[0]
        return {
            "allocation": selected_arm,
            "stratum_key": stratum_key,
        }


class RTSMAllocator:
    """Unified allocator coordinating multiple randomization strategies."""

    def __init__(self, config: RandomizationConfigSchema) -> None:
        self.config = config
        if config.algorithm_type == "PERMUTED_BLOCK":
            self.strategy: RandomizationStrategy = PermutedBlockStrategy(config)
        elif config.algorithm_type == "STRATIFIED_BLOCK":
            self.strategy = StratifiedBlockStrategy(config)
        elif config.algorithm_type == "MINIMIZATION":
            self.strategy = PocockSimonMinimizationStrategy(config)
        else:
            raise ValueError(f"Unsupported algorithm type: '{config.algorithm_type}'")

    def allocate(
        self,
        subject_factors: dict[str, Any] | None = None,
        sequence: list[str] | None = None,
        block_index: int = 0,
        previous_allocations: list[dict[str, Any]] | None = None,
    ) -> dict[str, Any]:
        """Delegate allocation to the configured strategy."""
        return self.strategy.allocate(
            subject_factors=subject_factors,
            sequence=sequence,
            block_index=block_index,
            previous_allocations=previous_allocations,
        )
