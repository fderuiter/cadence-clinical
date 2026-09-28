"""Pocock-Simon Dynamic Covariate Adaptive Minimization engine.

Requirements: PRD-RTSM-001, PRD-SYS-001, 21 CFR Part 11
"""

import hashlib
import math
import random
from typing import Any

from packages.engine_core.randomization.models import (
    ImbalanceDistanceMetric,
    MinimizationConfig,
    RandomizationAlgorithmType,
    SubjectAllocationRequest,
    SubjectAllocationResult,
)


class PocockSimonMinimizer:
    """Zero-I/O Dynamic Covariate Adaptive Minimization engine."""

    @staticmethod
    def calculate_marginal_counts(
        arms: list[str],
        active_factors: list[str],
        subject_factors: dict[str, Any],
        historical_subjects: list[dict[str, Any]],
    ) -> dict[str, dict[str, int]]:
        """Computes marginal counts across arms for each factor level matching the current subject.

        Returns:
            dict mapping factor_name -> dict mapping arm -> count of historical subjects matching that factor level.
        """
        # Initialize counts: factor -> arm -> 0
        marginal_counts: dict[str, dict[str, int]] = {
            factor: {arm: 0 for arm in arms} for factor in active_factors
        }

        for subj in historical_subjects:
            assigned_arm = subj.get("assigned_arm") or subj.get("arm")
            if assigned_arm not in arms:
                continue

            factors = subj.get("stratification_factors") or subj.get("factors") or subj
            for factor in active_factors:
                target_val = subject_factors.get(factor)
                subj_val = factors.get(factor)
                if (
                    target_val is not None
                    and str(subj_val).strip() == str(target_val).strip()
                ):
                    marginal_counts[factor][assigned_arm] += 1

        return marginal_counts

    @staticmethod
    def compute_distance(
        counts: list[float],
        metric: ImbalanceDistanceMetric,
    ) -> float:
        """Computes distance / imbalance metric across arm counts."""
        if not counts:
            return 0.0

        if metric == ImbalanceDistanceMetric.RANGE:
            return max(counts) - min(counts)

        n = len(counts)
        mean = sum(counts) / n
        variance = sum((x - mean) ** 2 for x in counts) / n

        if metric == ImbalanceDistanceMetric.VARIANCE:
            return variance

        if metric == ImbalanceDistanceMetric.STANDARD_DEVIATION:
            return math.sqrt(variance)

        return max(counts) - min(counts)

    @classmethod
    def evaluate_candidate_imbalances(
        cls,
        config: MinimizationConfig,
        subject_factors: dict[str, Any],
        historical_subjects: list[dict[str, Any]],
    ) -> dict[str, float]:
        """Calculates total weighted imbalance score for each candidate arm assignment.

        Returns:
            dict mapping arm name -> total weighted imbalance score if subject were assigned to that arm.
        """
        arms = list(config.arms_ratios.keys())
        active_factors = (
            list(config.stratification_factors.keys())
            if isinstance(config.stratification_factors, dict)
            else list(config.stratification_factors)
        )
        factor_weights = config.factor_weights or {}

        # 1. Get baseline marginal counts for matching factor levels
        baseline_counts = cls.marginal_counts(
            arms, active_factors, subject_factors, historical_subjects
        )

        imbalance_scores: dict[str, float] = {}

        # 2. Evaluate each candidate arm hypothetically
        for candidate_arm in arms:
            total_imbalance = 0.0

            for factor in active_factors:
                weight = factor_weights.get(factor, 1.0)
                # Create hypothetical counts adding 1 to candidate_arm
                hypothetical_arm_counts = []
                for arm in arms:
                    cnt = baseline_counts[factor][arm]
                    if arm == candidate_arm:
                        cnt += 1
                    # Normalize by arm ratio if non-1:1 allocation
                    ratio = config.arms_ratios.get(arm, 1)
                    hypothetical_arm_counts.append(cnt / ratio)

                factor_dist = cls.compute_distance(
                    hypothetical_arm_counts, config.distance_metric
                )
                total_imbalance += weight * factor_dist

            imbalance_scores[candidate_arm] = round(total_imbalance, 4)

        return imbalance_scores

    @classmethod
    def marginal_counts(
        cls,
        arms: list[str],
        active_factors: list[str],
        subject_factors: dict[str, Any],
        historical_subjects: list[dict[str, Any]],
    ) -> dict[str, dict[str, int]]:
        return cls.calculate_marginal_counts(
            arms, active_factors, subject_factors, historical_subjects
        )

    @classmethod
    def allocate(
        cls,
        config: MinimizationConfig,
        request: SubjectAllocationRequest,
    ) -> SubjectAllocationResult:
        """Executes Pocock-Simon dynamic minimization allocation with biased coin assignment."""
        active_factors = (
            list(config.stratification_factors.keys())
            if isinstance(config.stratification_factors, dict)
            else list(config.stratification_factors)
        )

        # Validate that required subject factors are present
        for factor in active_factors:
            if factor not in request.stratification_factors:
                raise ValueError(
                    f"Missing required stratification factor value for: '{factor}'"
                )

        arms = list(config.arms_ratios.keys())
        imbalance_scores = cls.evaluate_candidate_imbalances(
            config, request.stratification_factors, request.all_subject_factors
        )

        # Find arm(s) with minimum imbalance
        min_score = min(imbalance_scores.values())
        preferred_arms = [
            arm
            for arm, score in imbalance_scores.items()
            if math.isclose(score, min_score, rel_tol=1e-5)
        ]
        non_preferred_arms = [arm for arm in arms if arm not in preferred_arms]

        # Seed PRNG deterministically
        seed_str = (
            f"{config.seed or 101}:{request.study_id}:{request.subject_id}:"
            f"{len(request.all_subject_factors)}"
        )
        seed_hash = int(hashlib.sha256(seed_str.encode("utf-8")).hexdigest()[:8], 16)
        rng = random.Random(seed_hash)

        # Biased coin allocation
        # If all arms are tied (e.g. at the start), assign uniformly
        if len(preferred_arms) == len(arms) or not non_preferred_arms:
            assigned_arm = rng.choice(preferred_arms)
        else:
            p_pref = config.p_preferred
            coin = rng.random()
            if coin < p_pref:
                assigned_arm = rng.choice(preferred_arms)
            else:
                assigned_arm = rng.choice(non_preferred_arms)

        allocation_index = len(request.all_subject_factors)
        stratum_parts = [
            f"{f}={request.stratification_factors[f]}" for f in sorted(active_factors)
        ]
        stratum_key = "|".join(stratum_parts)

        checksum_payload = (
            f"{request.study_id}:{request.subject_id}:{assigned_arm}:"
            f"{allocation_index}:{imbalance_scores[assigned_arm]}"
        )
        checksum = hashlib.sha256(checksum_payload.encode("utf-8")).hexdigest()

        return SubjectAllocationResult(
            study_id=request.study_id,
            subject_id=request.subject_id,
            assigned_arm=assigned_arm,
            stratum_key=stratum_key,
            algorithm_type=RandomizationAlgorithmType.MINIMIZATION,
            allocation_index=allocation_index,
            block_index=None,
            imbalance_score=imbalance_scores[assigned_arm],
            deterministic_checksum=checksum,
            decision_rationale=(
                f"Assigned to arm '{assigned_arm}' via Pocock-Simon Minimization ({config.distance_metric.value}). "
                f"Imbalance scores: {imbalance_scores}. Preferred: {preferred_arms} (p={config.p_preferred})."
            ),
        )
