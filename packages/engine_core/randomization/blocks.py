"""Stratified and Permuted Block Randomization strategy implementation.

Requirements: PRD-RTSM-001, PRD-SYS-001, 21 CFR Part 11
"""

import hashlib
import random
from typing import Any

from packages.engine_core.randomization.models import (
    StratifiedBlockConfig,
    SubjectAllocationRequest,
    SubjectAllocationResult,
)


def generate_canonical_stratum_key(
    subject_factors: dict[str, Any] | None,
    active_factors: list[str] | None,
) -> str:
    """Generates a stable, canonical stratum key from stratification factors.

    Format: 'FACTOR_A=VALUE_A|FACTOR_B=VALUE_B' (alphabetically sorted by factor name).
    Returns 'DEFAULT' if no active stratification factors are configured.
    """
    if not active_factors:
        return "DEFAULT"

    sorted_factors = sorted(active_factors)
    if not subject_factors:
        raise ValueError(
            "Subject stratification factors are missing, but stratification is configured."
        )

    parts: list[str] = []
    for factor in sorted_factors:
        if factor not in subject_factors or subject_factors[factor] is None:
            raise ValueError(
                f"Missing required stratification factor value for: '{factor}'"
            )
        val = str(subject_factors[factor]).strip()
        parts.append(f"{factor}={val}")

    return "|".join(parts)


def generate_permuted_block(
    arms_ratios: dict[str, int],
    block_size: int,
    rng: random.Random,
) -> list[str]:
    """Generates a single balanced permuted block of treatment arms.

    Args:
        arms_ratios: Map of arm name to relative ratio integer.
        block_size: Total slots in the block (must be multiple of sum(arms_ratios.values())).
        rng: Deterministic pseudo-random number generator instance.

    Returns:
        A shuffled list of arm names representing one complete block.
    """
    ratio_sum = sum(arms_ratios.values())
    multiplier = block_size // ratio_sum

    block: list[str] = []
    for arm, ratio in arms_ratios.items():
        block.extend([arm] * (ratio * multiplier))

    rng.shuffle(block)
    return block


def generate_stratum_block_sequence(
    config: StratifiedBlockConfig,
    stratum_key: str,
    target_count: int,
) -> tuple[list[str], list[tuple[int, int]]]:
    """Deterministically pre-generates a sequence of blocks for a given stratum.

    Args:
        config: Stratified block configuration containing arms_ratios, block_sizes, and seed.
        stratum_key: Canonical stratum key.
        target_count: Number of allocations needed (generates enough blocks to cover target_count).

    Returns:
        tuple of (full_allocation_sequence, list of (block_index, block_size))
    """
    # Create deterministic seed combining config seed and stratum_key
    base_seed_str = f"{config.seed or 42}:{stratum_key}"
    seed_hash = int(hashlib.sha256(base_seed_str.encode("utf-8")).hexdigest()[:8], 16)
    rng = random.Random(seed_hash)

    allowed_block_sizes = config.block_sizes or [sum(config.arms_ratios.values())]

    full_allocations: list[str] = []
    block_metadata: list[tuple[int, int]] = []

    block_idx = 0
    while len(full_allocations) < target_count:
        chosen_size = rng.choice(allowed_block_sizes)
        block = generate_permuted_block(config.arms_ratios, chosen_size, rng)
        full_allocations.extend(block)
        block_metadata.append((block_idx, chosen_size))
        block_idx += 1

    return full_allocations, block_metadata


class StratifiedBlockAllocator:
    """Zero-I/O allocator executing Stratified Permuted Block Randomization."""

    @staticmethod
    def allocate(
        config: StratifiedBlockConfig,
        request: SubjectAllocationRequest,
    ) -> SubjectAllocationResult:
        """Allocates a subject to a treatment arm within their discrete stratum.

        Args:
            config: Block configuration.
            request: Allocation request containing subject factors and historical allocations.

        Returns:
            SubjectAllocationResult with assigned arm, block indices, and cryptographic verification hash.
        """
        active_factors = (
            list(config.stratification_factors.keys())
            if isinstance(config.stratification_factors, dict)
            else config.stratification_factors
        )
        stratum_key = generate_canonical_stratum_key(
            request.stratification_factors, active_factors
        )

        current_history = request.current_stratum_allocations
        next_allocation_idx = len(current_history)

        # Generate enough blocks to cover next_allocation_idx + 1
        needed_count = max(next_allocation_idx + 1, 100)
        full_sequence, block_metadata = generate_stratum_block_sequence(
            config, stratum_key, needed_count
        )

        assigned_arm = full_sequence[next_allocation_idx]

        # Determine which block index this allocation fell into
        cumulative_slots = 0
        current_block_idx = 0
        for b_idx, b_size in block_metadata:
            if cumulative_slots <= next_allocation_idx < cumulative_slots + b_size:
                current_block_idx = b_idx
                break
            cumulative_slots += b_size

        # Create cryptographic proof checksum
        checksum_payload = (
            f"{request.study_id}:{request.subject_id}:{stratum_key}:"
            f"{next_allocation_idx}:{current_block_idx}:{assigned_arm}"
        )
        checksum = hashlib.sha256(checksum_payload.encode("utf-8")).hexdigest()

        return SubjectAllocationResult(
            study_id=request.study_id,
            subject_id=request.subject_id,
            assigned_arm=assigned_arm,
            stratum_key=stratum_key,
            algorithm_type=config.algorithm_type,
            allocation_index=next_allocation_idx,
            block_index=current_block_idx,
            imbalance_score=None,
            deterministic_checksum=checksum,
            decision_rationale=(
                f"Assigned to arm '{assigned_arm}' via {config.algorithm_type} "
                f"in stratum '{stratum_key}' (block #{current_block_idx}, slot #{next_allocation_idx})."
            ),
        )
