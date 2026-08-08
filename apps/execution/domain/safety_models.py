"""Pydantic data models for Serious Adverse Event (SAE) cases and ICH E2B(R3) safety reporting.

Requirements: PRD-SYS-001
"""

from enum import StrEnum

from pydantic import BaseModel, Field


class SeriousnessCriteriaEnum(StrEnum):
    """ICH E2B(R3) seriousness criteria.

    Requirements: PRD-SYS-001
    """

    DEATH = "DEATH"
    LIFE_THREATENING = "LIFE_THREATENING"
    HOSPITALIZATION = "HOSPITALIZATION"
    DISABILITY = "DISABILITY"
    CONGENITAL_ANOMALY = "CONGENITAL_ANOMALY"
    OTHER_MEDICALLY_IMPORTANT = "OTHER_MEDICALLY_IMPORTANT"


class CausalityEnum(StrEnum):
    """WHO-UMC / ICH causality assessment categories.

    Requirements: PRD-SYS-001
    """

    CERTAIN = "CERTAIN"
    PROBABLE = "PROBABLE"
    POSSIBLE = "POSSIBLE"
    UNLIKELY = "UNLIKELY"
    UNRELATED = "UNRELATED"


class SAECaseRecord(BaseModel):
    """Serious Adverse Event (SAE) case record representing an Individual Case Safety Report (ICSR).

    Requirements: PRD-SYS-001
    """

    case_id: str = Field(..., description="Unique SAE case record identifier")
    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target subject ID")
    safety_report_id: str = Field(
        ..., description="E2B(R3) Safety Report Unique Identifier"
    )
    reaction_pt: str = Field(
        ..., description="MedDRA Preferred Term (PT) for reaction/event"
    )
    meddra_code: str = Field(..., description="8-digit MedDRA LLT/PT code")
    onset_date: str = Field(..., description="ISO 8601 AE/SAE onset date string")
    seriousness_criteria: SeriousnessCriteriaEnum = Field(
        ..., description="Primary seriousness criterion"
    )
    causality: CausalityEnum = Field(
        CausalityEnum.POSSIBLE, description="Investigator causality assessment"
    )
    expedited_reporting_required: bool = Field(
        False, description="True if 7/15-day expedited reporting required"
    )
    parsed_at: str | None = Field(
        None, description="UTC ISO timestamp of E2B XML ingestion"
    )
