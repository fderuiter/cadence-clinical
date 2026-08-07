"""CDISC USDM v2.0 and v3.0 Pydantic v2 data models.

Provides strictly-typed objects representing the Unified Study Data Model (USDM)
protocol graph structure, including study designs, encounters, activities, and
eligibility criteria.

Requirements: PRD-SYS-001
"""

from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class Code(BaseModel):
    """USDM Code / Concept representation."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    code: str
    code_system: str = Field(alias="codeSystem")
    code_system_version: str | None = Field(default=None, alias="codeSystemVersion")
    decode: str


class SyntaxTemplate(BaseModel):
    """Syntax template definition for rules and eligibility criteria."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    id: str
    name: str | None = None
    text: str
    notes: list[str] = Field(default_factory=list)


class EligibilityCriterion(BaseModel):
    """Eligibility criterion (Inclusion or Exclusion)."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    id: str
    name: str
    criterion_type: str = Field(
        alias="criterionType", description="Inclusion or Exclusion"
    )
    category: str | None = None
    text: str | None = None
    template: SyntaxTemplate | None = None


class Activity(BaseModel):
    """Study activity or procedure definition."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    id: str
    name: str
    description: str | None = None
    defined_procedures: list[dict[str, Any]] = Field(
        default_factory=list, alias="definedProcedures"
    )


class Encounter(BaseModel):
    """Study encounter / visit definition."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    id: str
    name: str
    encounter_type: str = Field(default="Visit", alias="encounterType")
    start_date: str | None = Field(default=None, alias="startDate")
    end_date: str | None = Field(default=None, alias="endDate")


class StudyArm(BaseModel):
    """Study arm definition."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    id: str
    name: str
    arm_type: str = Field(default="Treatment", alias="armType")
    description: str | None = None


class StudyEpoch(BaseModel):
    """Study epoch definition."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    id: str
    name: str
    epoch_type: str = Field(default="Screening", alias="epochType")
    sequence_number: int = Field(default=1, alias="sequenceNumber")


class StudyDesign(BaseModel):
    """Study design containing arms, epochs, encounters, activities, and criteria."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    id: str
    name: str
    design_type: str | None = Field(default=None, alias="designType")
    arms: list[StudyArm] = Field(default_factory=list)
    epochs: list[StudyEpoch] = Field(default_factory=list)
    encounters: list[Encounter] = Field(default_factory=list)
    activities: list[Activity] = Field(default_factory=list)
    eligibility_criteria: list[EligibilityCriterion] = Field(
        default_factory=list, alias="eligibilityCriteria"
    )


class USDMStudy(BaseModel):
    """Root USDM protocol study specification container."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    id: str
    name: str
    protocol_title: str = Field(alias="protocolTitle")
    usdm_version: str = Field(default="3.0", alias="usdmVersion")
    study_designs: list[StudyDesign] = Field(default_factory=list, alias="studyDesigns")
