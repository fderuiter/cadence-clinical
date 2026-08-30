"""Pydantic v2 domain schemas for the Stochastic Randomization Engine.

Covers: Stratified Permuted Block, Pocock-Simon Minimization, and Phase I/II Adaptive Dose Escalation (BOIN / 3+3).
Requirements: PRD-RTSM-001, PRD-RTSM-002, PRD-SYS-001, 21 CFR Part 11
"""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field, field_validator, model_validator


class RandomizationAlgorithmType(StrEnum):
    """Supported randomization and allocation algorithm types."""

    PERMUTED_BLOCK = "PERMUTED_BLOCK"
    STRATIFIED_BLOCK = "STRATIFIED_BLOCK"
    MINIMIZATION = "MINIMIZATION"
    BOIN = "BOIN"
    THREE_PLUS_THREE = "THREE_PLUS_THREE"
    RESPONSE_ADAPTIVE = "RESPONSE_ADAPTIVE"


class ImbalanceDistanceMetric(StrEnum):
    """Distance metrics for Pocock-Simon dynamic covariate adaptive minimization."""

    RANGE = "RANGE"  # Max - Min
    VARIANCE = "VARIANCE"  # Sample variance across arm marginal counts
    STANDARD_DEVIATION = "STANDARD_DEVIATION"  # Sample standard deviation


class DoseEscalationAction(StrEnum):
    """Next dose recommendation actions for adaptive Phase I/II dose finding."""

    ESCALATE = "ESCALATE"
    STAY = "STAY"
    DEESCALATE = "DEESCALATE"
    STOP_TRIAL = "STOP_TRIAL"
    ELIMINATE_DOSE = "ELIMINATE_DOSE"


# =========================================================================
# Configuration Schemas
# =========================================================================


class StratifiedBlockConfig(BaseModel):
    """Configuration for Permuted Block and Stratified Block Randomization."""

    algorithm_type: RandomizationAlgorithmType = Field(
        default=RandomizationAlgorithmType.STRATIFIED_BLOCK
    )
    arms_ratios: dict[str, int] = Field(
        ...,
        description="Mapping of treatment arm identifiers to integer target allocation ratios (e.g. {'ARM_A': 1, 'ARM_B': 1}).",
    )
    stratification_factors: list[str] | dict[str, list[str]] | None = Field(
        default=None,
        description="Stratification factors as list of factor names or dict mapping factor name to allowed category values.",
    )
    block_sizes: list[int] | None = Field(
        default=None,
        description="Allowed block sizes. Each block size must be a positive multiple of the sum of arm ratios.",
    )
    seed: int | None = Field(
        default=None,
        description="Optional deterministic PRNG seed for reproducible allocations.",
    )

    @model_validator(mode="after")
    def validate_block_config(self) -> StratifiedBlockConfig:
        if not self.arms_ratios:
            raise ValueError("arms_ratios must not be empty.")
        for arm, ratio in self.arms_ratios.items():
            if not arm or not arm.strip():
                raise ValueError("Arm identifiers must be non-empty strings.")
            if ratio <= 0:
                raise ValueError(f"Ratio for arm '{arm}' must be a positive integer.")

        ratio_sum = sum(self.arms_ratios.values())
        if self.block_sizes is None:
            self.block_sizes = [ratio_sum]
        if not self.block_sizes:
            raise ValueError("block_sizes list cannot be empty.")
        for size in self.block_sizes:
            if size <= 0:
                raise ValueError(f"Block size {size} must be positive.")
            if size % ratio_sum != 0:
                raise ValueError(
                    f"Block size {size} must be a multiple of the sum of ratios ({ratio_sum})."
                )

        if self.algorithm_type == RandomizationAlgorithmType.STRATIFIED_BLOCK:
            if not self.stratification_factors:
                raise ValueError(
                    "stratification_factors must be provided for STRATIFIED_BLOCK."
                )
        return self


class MinimizationConfig(BaseModel):
    """Configuration for Pocock-Simon Dynamic Covariate Adaptive Minimization."""

    algorithm_type: RandomizationAlgorithmType = Field(
        default=RandomizationAlgorithmType.MINIMIZATION
    )
    arms_ratios: dict[str, int] = Field(
        ...,
        description="Mapping of treatment arm identifiers to target ratios.",
    )
    stratification_factors: list[str] | dict[str, list[str]] = Field(
        ...,
        description="Prognostic stratification factors evaluated for marginal imbalance.",
    )
    factor_weights: dict[str, float] | None = Field(
        default=None,
        description="Optional relative importance weights for stratification factors (defaults to 1.0 per factor).",
    )
    distance_metric: ImbalanceDistanceMetric = Field(
        default=ImbalanceDistanceMetric.RANGE,
        description="Metric used to measure marginal imbalance across treatment arms.",
    )
    p_preferred: float = Field(
        default=0.8,
        description="Biased coin probability (0.5 <= p <= 1.0) for assigning subject to the arm minimizing imbalance.",
    )
    seed: int | None = Field(
        default=None,
        description="Optional deterministic PRNG seed for reproducible allocations.",
    )

    @model_validator(mode="after")
    def validate_minimization_config(self) -> MinimizationConfig:
        if not (0.5 <= self.p_preferred <= 1.0):
            raise ValueError("p_preferred must be between 0.5 and 1.0 inclusive.")
        if not self.arms_ratios:
            raise ValueError("arms_ratios must not be empty.")
        for arm, ratio in self.arms_ratios.items():
            if ratio <= 0:
                raise ValueError(f"Ratio for arm '{arm}' must be a positive integer.")

        active_factors = (
            list(self.stratification_factors.keys())
            if isinstance(self.stratification_factors, dict)
            else list(self.stratification_factors)
        )
        if not active_factors:
            raise ValueError("stratification_factors must not be empty.")

        if self.factor_weights:
            for factor, weight in self.factor_weights.items():
                if factor not in active_factors:
                    raise ValueError(
                        f"Factor weight key '{factor}' is not in stratification_factors."
                    )
                if weight < 0:
                    raise ValueError(f"Factor weight for '{factor}' must be >= 0.")
        return self


class DoseLevelDefinition(BaseModel):
    """Specification of a discrete dose level in Phase I/II dose escalation."""

    dose_index: int = Field(..., ge=0, description="Zero-indexed dose level tier.")
    dose_amount: float = Field(..., gt=0, description="Dose magnitude.")
    dose_unit: str = Field(..., description="Dose unit (e.g. mg, mg/kg, mg/m^2).")
    formulation: str | None = Field(
        default=None, description="Optional dosage form description."
    )


class BOINConfig(BaseModel):
    """Configuration for Bayesian Optimal Interval (BOIN) Adaptive Dose Finding."""

    algorithm_type: RandomizationAlgorithmType = Field(
        default=RandomizationAlgorithmType.BOIN
    )
    target_dlt_rate: float = Field(
        default=0.25,
        gt=0.0,
        lt=1.0,
        description="Target Dose-Limiting Toxicity (DLT) probability phi (e.g. 0.25, 0.30).",
    )
    escalation_boundary: float | None = Field(
        default=None,
        description="Escalation boundary lambda_e. If None, auto-calculated from target_dlt_rate.",
    )
    deescalation_boundary: float | None = Field(
        default=None,
        description="De-escalation boundary lambda_d. If None, auto-calculated from target_dlt_rate.",
    )
    stopping_cutoff: float = Field(
        default=0.95,
        ge=0.80,
        le=0.99,
        description="Bayesian posterior toxicity probability threshold Pr(p_j > phi | data) to eliminate dose and stop if lowest dose is toxic.",
    )
    cohort_size: int = Field(
        default=3,
        ge=1,
        description="Standard cohort increment size for dose escalation steps.",
    )
    max_dose_subjects: int = Field(
        default=12,
        ge=3,
        description="Maximum subjects evaluated at any single dose level before MTD selection.",
    )

    @field_validator("target_dlt_rate")
    @classmethod
    def validate_target(cls, v: float) -> float:
        if not (0.05 <= v <= 0.60):
            raise ValueError("target_dlt_rate must be between 0.05 and 0.60.")
        return v


class ThreePlusThreeConfig(BaseModel):
    """Configuration for classical 3+3 algorithmic rule-based dose escalation."""

    algorithm_type: RandomizationAlgorithmType = Field(
        default=RandomizationAlgorithmType.THREE_PLUS_THREE
    )
    cohort_size: int = Field(default=3, ge=1)
    expansion_cohort_size: int = Field(default=6, ge=2)


# =========================================================================
# State & Request/Result Models
# =========================================================================


class DoseCohortState(BaseModel):
    """Observed clinical outcome state for a specific dose level."""

    dose_index: int = Field(..., ge=0)
    dose_amount: float = Field(..., gt=0)
    dose_unit: str = Field(...)
    evaluated_subjects: int = Field(
        default=0, ge=0, description="Total subjects evaluable for DLT (n_j)."
    )
    observed_dlts: int = Field(
        default=0, ge=0, description="Total subjects experiencing DLT (d_j)."
    )
    is_eliminated: bool = Field(
        default=False,
        description="Whether dose level has been eliminated due to excessive toxicity.",
    )

    @model_validator(mode="after")
    def validate_counts(self) -> DoseCohortState:
        if self.observed_dlts > self.evaluated_subjects:
            raise ValueError("observed_dlts cannot exceed evaluated_subjects.")
        return self

    @property
    def empirical_dlt_rate(self) -> float:
        """Returns the empirical DLT rate d_j / n_j."""
        if self.evaluated_subjects == 0:
            return 0.0
        return round(self.observed_dlts / self.evaluated_subjects, 4)


class BOINDoseRecommendation(BaseModel):
    """Decision output from the BOIN or 3+3 Dose Escalation Engine."""

    current_dose_index: int
    current_dose_amount: float
    current_dose_unit: str
    action: DoseEscalationAction
    recommended_dose_index: int | None = None
    recommended_dose_amount: float | None = None
    empirical_dlt_rate: float
    posterior_prob_toxic: float | None = None
    is_trial_stopped: bool = False
    rationale: str


class SubjectAllocationRequest(BaseModel):
    """Request payload for subject randomization."""

    study_id: str
    subject_id: str
    site_id: str
    stratification_factors: dict[str, Any] = Field(default_factory=dict)
    current_stratum_allocations: list[str] = Field(
        default_factory=list,
        description="Historical sequence of arm assignments within the subject's stratum (for block algorithms).",
    )
    all_subject_factors: list[dict[str, Any]] = Field(
        default_factory=list,
        description="Historical factor profiles and arm assignments of previously randomized subjects (for minimization).",
    )


class SubjectAllocationResult(BaseModel):
    """Result payload from the Stochastic Randomization Engine."""

    study_id: str
    subject_id: str
    assigned_arm: str
    stratum_key: str
    algorithm_type: RandomizationAlgorithmType
    allocation_index: int
    block_index: int | None = None
    imbalance_score: float | None = None
    deterministic_checksum: str
    decision_rationale: str
