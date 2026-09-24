"""Domain models, enums and schemas for RTSM Adaptive Oncology Dose Escalation & RAR.

Requirements: PRD-SCM-004, ADR-2199, FDA Adaptive Design Guidance
"""

from datetime import datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, model_validator


class DoseEscalationDesignType(StrEnum):
    """Supported adaptive dose-escalation and randomization design types."""

    THREE_PLUS_THREE = "THREE_PLUS_THREE"
    BOIN = "BOIN"
    CRM = "CRM"
    RAR = "RAR"


class CohortRecommendationAction(StrEnum):
    """Automated and committee-reviewed cohort actions."""

    ESCALATE = "ESCALATE"
    DE_ESCALATE = "DE_ESCALATE"
    STAY = "STAY"
    EXPAND_COHORT = "EXPAND_COHORT"
    STOP_SAFETY = "STOP_SAFETY"
    STOP_SUCCESS = "STOP_SUCCESS"
    MTD_FOUND = "MTD_FOUND"


class CohortStatus(StrEnum):
    """Lifecycle status of a dose escalation cohort."""

    ENROLLING = "ENROLLING"
    EVALUATED = "EVALUATED"
    APPROVED = "APPROVED"
    OVERRIDDEN = "OVERRIDDEN"
    COMPLETED = "COMPLETED"
    TERMINATED = "TERMINATED"


class RARArmStatus(StrEnum):
    """Lifecycle status for a treatment arm in Response-Adaptive Randomization."""

    ACTIVE = "ACTIVE"
    DROPPED_FUTILITY = "DROPPED_FUTILITY"
    GRADUATED_SUCCESS = "GRADUATED_SUCCESS"
    TERMINATED = "TERMINATED"


class DoseLevelDef(BaseModel):
    """Definition of a discrete dose level."""

    model_config = ConfigDict(strict=True)

    index: int = Field(..., ge=1, description="1-based dose level sequence index.")
    name: str = Field(
        ..., min_length=1, description="Dose identifier / label, e.g. '10 mg/kg'."
    )
    dose_value: float = Field(
        ..., gt=0, description="Numerical dose amount (e.g. 10.0)."
    )


class BoinConfig(BaseModel):
    """Configuration parameters for Bayesian Optimal Interval (BOIN) design."""

    model_config = ConfigDict(strict=True)

    target_toxicity_rate: float = Field(
        default=0.25,
        gt=0.0,
        lt=1.0,
        description="Target Dose-Limiting Toxicity (DLT) rate phi (e.g. 0.25 or 0.30).",
    )
    factor_e: float = Field(
        default=0.6,
        gt=0.0,
        lt=1.0,
        description="Escalation boundary factor c_e such that phi_1 = c_e * phi (default 0.6).",
    )
    factor_d: float = Field(
        default=1.4,
        gt=1.0,
        description="De-escalation boundary factor c_d such that phi_2 = c_d * phi (default 1.4).",
    )
    safety_cutoff: float = Field(
        default=0.95,
        gt=0.5,
        lt=1.0,
        description="Bayesian overdose stopping probability cutoff P(p_j > phi | data) > cutoff.",
    )
    prior_alpha: float = Field(
        default=1.0,
        gt=0.0,
        description="Beta prior shape parameter alpha (default 1.0).",
    )
    prior_beta: float = Field(
        default=1.0,
        gt=0.0,
        description="Beta prior shape parameter beta (default 1.0).",
    )
    min_subjects_for_elimination: int = Field(
        default=3,
        ge=1,
        description="Minimum evaluated subjects at a dose level before overdose elimination can trigger.",
    )


class CrmConfig(BaseModel):
    """Configuration parameters for Continual Reassessment Method (CRM / mCRM)."""

    model_config = ConfigDict(strict=True)

    target_toxicity_rate: float = Field(
        default=0.25,
        gt=0.0,
        lt=1.0,
        description="Target DLT rate phi.",
    )
    skeleton_probs: list[float] = Field(
        ...,
        min_length=2,
        description="Prior toxicity probability skeleton for each dose level (strictly increasing).",
    )
    model_type: str = Field(
        default="EMPIRIC_POWER",
        description="Dose-toxicity link model (e.g. 'EMPIRIC_POWER' where p_j(theta) = p_{0,j}^{exp(theta)}).",
    )
    prior_std: float = Field(
        default=1.34,
        gt=0.0,
        description="Prior standard deviation for model parameter theta ~ Normal(0, sigma^2).",
    )
    safety_cutoff: float = Field(
        default=0.90,
        gt=0.5,
        lt=1.0,
        description="Safety stopping threshold on dose 1 toxicity P(p_1 > phi | D) > cutoff.",
    )
    allow_dose_skipping: bool = Field(
        default=False,
        description="If False (modified CRM), forbids jumping over untried doses upon escalation.",
    )

    @model_validator(mode="after")
    def validate_skeleton(self) -> CrmConfig:
        for i in range(len(self.skeleton_probs)):
            p = self.skeleton_probs[i]
            if not (0.0 < p < 1.0):
                raise ValueError(
                    f"Skeleton probability at index {i} ({p}) must be in (0, 1)."
                )
            if i > 0 and self.skeleton_probs[i] <= self.skeleton_probs[i - 1]:
                raise ValueError(
                    "Skeleton probabilities must be strictly monotonically increasing."
                )
        return self


class ThreePlusThreeConfig(BaseModel):
    """Configuration parameters for classic 3+3 rule-based design."""

    model_config = ConfigDict(strict=True)

    initial_cohort_size: int = Field(default=3, ge=1)
    expansion_cohort_size: int = Field(default=3, ge=1)
    max_dlt_escalate: int = Field(default=0, ge=0)
    max_dlt_expand: int = Field(default=1, ge=0)


class RarConfig(BaseModel):
    """Configuration parameters for Response-Adaptive Randomization (RAR)."""

    model_config = ConfigDict(strict=True)

    burn_in_sample_per_arm: int = Field(
        default=5,
        ge=0,
        description="Initial fixed allocation sample size per arm before adaptive re-weighting activates.",
    )
    power_tuning_param: float = Field(
        default=0.5,
        ge=0.0,
        le=2.0,
        description="Tuning exponent c for allocation probability weighting w_k ~ (q_k / n_k)^c.",
    )
    min_allocation_prob: float = Field(
        default=0.05,
        ge=0.0,
        lt=0.5,
        description="Floor boundary on active arm allocation probability to prevent arm starvation.",
    )
    max_allocation_prob: float = Field(
        default=0.85,
        gt=0.5,
        le=1.0,
        description="Ceiling boundary on active arm allocation probability.",
    )
    futility_threshold: float = Field(
        default=0.05,
        ge=0.0,
        lt=0.5,
        description="Posterior probability threshold P(theta_k > theta_ctrl) < threshold to drop arm for futility.",
    )
    efficacy_threshold: float = Field(
        default=0.99,
        gt=0.5,
        le=1.0,
        description="Posterior probability threshold P(theta_k > theta_ctrl) > threshold to graduate arm for efficacy.",
    )
    prior_alpha: float = Field(default=1.0, gt=0.0)
    prior_beta: float = Field(default=1.0, gt=0.0)


class AdaptiveTrialConfigDTO(BaseModel):
    """Comprehensive DTO for adaptive trial configuration."""

    model_config = ConfigDict(strict=True)

    study_id: str = Field(..., min_length=1)
    design_type: str = Field(
        ...,
        min_length=1,
        description="Design type: 'THREE_PLUS_THREE', 'BOIN', 'CRM', 'RAR'",
    )
    dose_levels: list[DoseLevelDef] = Field(default_factory=list)
    boin_config: BoinConfig | None = None
    crm_config: CrmConfig | None = None
    three_plus_three_config: ThreePlusThreeConfig | None = None
    rar_config: RarConfig | None = None
    cohort_size: int = Field(default=3, ge=1)
    max_sample_size: int | None = Field(default=None, ge=1)
    is_active: bool = True

    @model_validator(mode="after")
    def validate_design_configs(self) -> AdaptiveTrialConfigDTO:
        allowed = {"THREE_PLUS_THREE", "BOIN", "CRM", "RAR"}
        if self.design_type not in allowed:
            raise ValueError(
                f"Invalid design_type '{self.design_type}'. Must be one of {allowed}."
            )

        if self.design_type in ("THREE_PLUS_THREE", "BOIN", "CRM"):
            if not self.dose_levels:
                raise ValueError(
                    f"dose_levels must be defined for design type '{self.design_type}'."
                )
            indices = [d.index for d in self.dose_levels]
            if indices != list(range(1, len(self.dose_levels) + 1)):
                raise ValueError(
                    "Dose level indices must be contiguous 1-indexed (1, 2, ..., K)."
                )

        if self.design_type == "BOIN" and self.boin_config is None:
            self.boin_config = BoinConfig()
        elif self.design_type == "CRM":
            if self.crm_config is None:
                raise ValueError("crm_config is required when design_type is 'CRM'.")
            if len(self.crm_config.skeleton_probs) != len(self.dose_levels):
                raise ValueError(
                    f"Skeleton probability count ({len(self.crm_config.skeleton_probs)}) must match dose level count ({len(self.dose_levels)})."
                )
        elif (
            self.design_type == "THREE_PLUS_THREE"
            and self.three_plus_three_config is None
        ):
            self.three_plus_three_config = ThreePlusThreeConfig()
        elif self.design_type == "RAR" and self.rar_config is None:
            self.rar_config = RarConfig()

        return self


class CohortEvaluationInput(BaseModel):
    """Input payload to evaluate an active cohort and compute adaptive dose recommendations."""

    model_config = ConfigDict(strict=True)

    study_id: str
    cohort_number: int = Field(..., ge=1)
    dose_level_index: int = Field(..., ge=1)
    subjects_evaluated: int = Field(..., ge=1)
    dlts_observed: int = Field(..., ge=0)
    subject_ids: list[str] = Field(default_factory=list)
    dlt_subject_ids: list[str] = Field(default_factory=list)

    @model_validator(mode="after")
    def validate_dlts(self) -> CohortEvaluationInput:
        if self.dlts_observed > self.subjects_evaluated:
            raise ValueError("dlts_observed cannot exceed subjects_evaluated.")
        return self


class DoseLevelEvaluationSummary(BaseModel):
    """Summary of subjects and DLTs evaluated at a specific dose level."""

    model_config = ConfigDict(strict=True)

    dose_level_index: int
    dose_name: str
    dose_value: float
    total_enrolled: int
    total_dlts: int
    observed_dlt_rate: float
    posterior_dlt_rate: float | None = None
    is_eliminated_for_safety: bool = False
    overdose_probability: float | None = None


class CohortEvaluationResult(BaseModel):
    """Output recommendation and Bayesian metrics from a cohort evaluation."""

    model_config = ConfigDict(strict=True)

    study_id: str
    cohort_number: int
    evaluated_dose_level_index: int
    evaluated_dose_name: str
    subjects_in_cohort: int
    dlts_in_cohort: int
    recommendation_action: str
    recommended_dose_level_index: int | None = None
    recommended_dose_name: str | None = None
    lambda_e: float | None = None
    lambda_d: float | None = None
    target_toxicity_rate: float | None = None
    overdose_probability: float | None = None
    is_safety_stop_triggered: bool = False
    is_mtd_declared: bool = False
    mtd_dose_level_index: int | None = None
    mtd_dose_name: str | None = None
    dose_summaries: list[DoseLevelEvaluationSummary] = Field(default_factory=list)
    rationale: str


class CohortTransitionRequest(BaseModel):
    """Payload to confirm or override an automated cohort recommendation."""

    model_config = ConfigDict(strict=True)

    study_id: str
    cohort_number: int = Field(..., ge=1)
    decision_action: str
    target_dose_level_index: int = Field(..., ge=1)
    override_reason: str | None = None
    electronic_signature: str | None = None


class CohortTransitionLogResponse(BaseModel):
    """21 CFR Part 11 audited transition log record."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    cohort_number: int
    from_dose_level: int
    to_dose_level: int
    action_taken: str
    algorithm_recommendation: str
    override_occurred: bool
    override_reason: str | None = None
    evaluated_dlt_rate: float | None = None
    posterior_toxicity_estimates: dict[str, Any] | None = None
    authorized_by: str
    transitioned_at: datetime
    signature_token: str | None = None
    cryptographic_checksum: str | None = None


class RARArmEvaluationDTO(BaseModel):
    """State and metrics for an individual arm under Response-Adaptive Randomization."""

    model_config = ConfigDict(strict=True)

    arm_id: str
    arm_name: str
    is_control: bool
    status: str
    enrolled_count: int
    response_count: int
    posterior_alpha: float
    posterior_beta: float
    posterior_mean: float
    allocation_probability: float
    superiority_vs_control_prob: float | None = None


class RAREvaluationResult(BaseModel):
    """Overall RAR probability adjustment output."""

    model_config = ConfigDict(strict=True)

    study_id: str
    total_enrolled: int
    arms: list[RARArmEvaluationDTO]
    allocation_probabilities: dict[str, float]
    dropped_arms: list[str] = Field(default_factory=list)
    graduated_arms: list[str] = Field(default_factory=list)
    rationale: str
    updated_at: datetime


class RARExpandOrTerminateArmRequest(BaseModel):
    """Request to seamlessly add or terminate a treatment arm with zero study downtime."""

    model_config = ConfigDict(strict=True)

    study_id: str
    arm_id: str
    arm_name: str
    is_control: bool = False
    action: str = Field(
        ..., description="'ADD_ARM', 'DROP_FUTILITY', 'GRADUATE_SUCCESS', 'TERMINATE'"
    )
    prior_alpha: float = 1.0
    prior_beta: float = 1.0
    reason: str


class BoinBoundariesCalculationRequest(BaseModel):
    """Direct calculation request for theoretical BOIN boundaries."""

    model_config = ConfigDict(strict=True)

    target_toxicity_rate: float = Field(default=0.25, gt=0.0, lt=1.0)
    factor_e: float = Field(default=0.6, gt=0.0, lt=1.0)
    factor_d: float = Field(default=1.4, gt=1.0)


class BoinBoundariesCalculationResponse(BaseModel):
    """Response containing exact theoretical BOIN boundaries."""

    model_config = ConfigDict(strict=True)

    target_toxicity_rate: float
    phi_1: float
    phi_2: float
    lambda_e: float
    lambda_d: float
    description: str


class CrmPosteriorCalculationRequest(BaseModel):
    """Direct calculation request for CRM posterior curve."""

    model_config = ConfigDict(strict=True)

    skeleton_probs: list[float]
    target_toxicity_rate: float = 0.25
    prior_std: float = 1.34
    dose_evaluations: list[dict[str, int]] = Field(
        ...,
        description="List of dicts per dose level: [{'dose_index': 1, 'enrolled': 3, 'dlts': 0}, ...]",
    )
    current_dose_index: int = 1


class CrmPosteriorCalculationResponse(BaseModel):
    """Response containing calculated CRM posterior probabilities and recommended next dose."""

    model_config = ConfigDict(strict=True)

    target_toxicity_rate: float
    current_dose_index: int
    recommended_dose_index: int
    recommendation_action: CohortRecommendationAction
    posterior_mean_probabilities: list[float]
    overdose_probability_dose_1: float
    is_safety_stop_triggered: bool
    rationale: str
