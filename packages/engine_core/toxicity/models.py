"""Models and schemas for the Declarative CTCAE & DAIDS Lab Toxicity Grading Engine.

Governs NCI-CTCAE v5.0 and DAIDS v2.1 laboratory toxicity scoring, Hy's Law DILI sentinels,
acute delta checks, and automated Adverse Event (AE) draft envelope generation under 21 CFR Part 11.
"""

import hashlib
import json
from datetime import UTC, datetime
from enum import IntEnum, StrEnum
from typing import Any, Literal

from pydantic import BaseModel, Field


class CTCAEGrade(IntEnum):
    """NCI-CTCAE v5.0 & DAIDS clinical severity grades."""

    GRADE_0 = 0  # Normal / Within reference range
    GRADE_1 = 1  # Mild; asymptomatic or mild symptoms; clinical or diagnostic observations only
    GRADE_2 = 2  # Moderate; minimal, local or noninvasive intervention indicated
    GRADE_3 = 3  # Severe; medically significant; hospitalization indicated
    GRADE_4 = 4  # Life-threatening consequences; urgent intervention indicated
    GRADE_5 = 5  # Death related to AE


class ToxicityCategory(StrEnum):
    """Clinical laboratory testing panels."""

    HEPATIC = "HEPATIC"
    RENAL = "RENAL"
    HEMATOLOGY = "HEMATOLOGY"
    ELECTROLYTES = "ELECTROLYTES"
    METABOLIC = "METABOLIC"
    COAGULATION = "COAGULATION"
    GENERAL = "GENERAL"


ToxicityScale = Literal["CTCAE_V5", "DAIDS_V2", "CUSTOM"]

GradingCriteriaType = Literal[
    "ULN_MULTIPLIER",
    "LLN_MULTIPLIER",
    "ABSOLUTE_RANGE",
    "DELTA_FROM_BASELINE",
    "PERCENT_CHANGE",
]


class GradeCutoff(BaseModel):
    """Specific mathematical boundary criteria for a single toxicity grade tier."""

    grade: CTCAEGrade = Field(..., description="Assigned severity grade (1-5)")
    criteria_type: GradingCriteriaType = Field(default="ULN_MULTIPLIER")
    min_multiplier: float | None = Field(
        default=None, description="Lower ratio multiplier (e.g. 1.0x ULN)"
    )
    max_multiplier: float | None = Field(
        default=None, description="Upper ratio multiplier (e.g. 3.0x ULN)"
    )
    min_value: float | None = Field(
        default=None, description="Absolute lower numerical threshold"
    )
    max_value: float | None = Field(
        default=None, description="Absolute upper numerical threshold"
    )
    description: str = Field(..., description="Clinical grading rationale text")


class ToxicityRuleDefinition(BaseModel):
    """Declarative, version-pinned specification for laboratory toxicity grading."""

    test_code: str = Field(
        ...,
        description="Canonical lab test code (e.g. 'ALT', 'CREATININE', 'PLATELETS')",
    )
    test_name: str = Field(..., description="Human-readable lab test title")
    category: ToxicityCategory = Field(default=ToxicityCategory.GENERAL)
    scale: ToxicityScale = Field(default="CTCAE_V5")
    grades: list[GradeCutoff] = Field(default_factory=list)
    meddra_term: str | None = Field(
        default=None, description="Associated MedDRA Preferred Term (PT)"
    )
    version_index: int = Field(default=1, ge=1)
    is_active: bool = Field(default=True)
    reason_for_change: str | None = Field(default=None)
    sha256_checksum: str | None = Field(default=None)

    def model_post_init(self, __context: Any) -> None:
        """Compute SHA-256 seal upon definition instantiation."""
        if not self.sha256_checksum:
            payload = {
                "test_code": self.test_code,
                "scale": self.scale,
                "version_index": self.version_index,
                "grades": [g.model_dump() for g in self.grades],
            }
            raw = json.dumps(payload, sort_keys=True, default=str)
            object.__setattr__(
                self, "sha256_checksum", hashlib.sha256(raw.encode("utf-8")).hexdigest()
            )


class AdverseEventDraftPayload(BaseModel):
    """Pre-populated Adverse Event draft envelope generated from Grade 3/4 toxicity or DILI alert."""

    test_code: str
    test_name: str
    observed_value: float
    unit: str
    grade: CTCAEGrade
    meddra_term: str | None = None
    severity: str = Field(
        ..., description="'SEVERE' for Grade 3, 'LIFE_THREATENING' for Grade 4"
    )
    is_serious: bool = True
    hys_law_indicated: bool = False
    draft_timestamp: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    requires_physician_signoff: bool = True


class ToxicityAssessmentResult(BaseModel):
    """Evaluated toxicity grading result for a laboratory observation."""

    test_code: str
    test_name: str
    observed_value: float
    unit: str
    ref_low: float | None = None
    ref_high: float | None = None
    baseline_value: float | None = None
    grade: CTCAEGrade = CTCAEGrade.GRADE_0
    scale: ToxicityScale = "CTCAE_V5"
    description: str
    is_critical_ae_trigger: bool = False
    is_hys_law_warning: bool = False
    is_delta_check_alert: bool = False
    delta_percentage: float | None = None
    ae_draft: AdverseEventDraftPayload | None = None
    eval_timestamp: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())


class HysLawEvaluation(BaseModel):
    """Evaluation result of the multi-analyte Hy's Law Drug-Induced Liver Injury (DILI) triad."""

    is_hys_law_met: bool = False
    alt_ast_ratio_uln: float
    tbil_ratio_uln: float
    alkp_ratio_uln: float
    alert_level: Literal[
        "NORMAL", "BORDERLINE_HEPATIC", "POTENTIAL_DILI_WARNING", "HYS_LAW_CRITICAL"
    ]
    rationale: str
    eval_timestamp: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
