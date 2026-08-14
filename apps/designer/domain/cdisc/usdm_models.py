"""CDISC USDM v2.0, v3.0, and v4.0 Pydantic v2 data models.

Provides strictly-typed objects representing the Unified Study Data Model (USDM)
protocol graph structure, including study versions, study designs, encounters,
activities, biomedical concepts, and eligibility criteria.

Requirements: PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
"""

from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class Code(BaseModel):
    """USDM Code / Concept representation."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    code: str
    code_system: str = Field(alias="codeSystem")
    code_system_version: str | None = Field(default=None, alias="codeSystemVersion")
    decode: str


class SyntaxTemplate(BaseModel):
    """Syntax template definition for rules and eligibility criteria."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str | None = None
    text: str
    notes: list[str] = Field(default_factory=list)


class EligibilityCriterion(BaseModel):
    """Eligibility criterion (Inclusion or Exclusion)."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str = ""
    criterion_type: str = Field(
        default="Inclusion",
        alias="criterionType",
        description="Inclusion or Exclusion",
    )
    identifier: str | None = None
    category: str | None = None
    text: str | None = None
    text_expression: str | None = Field(default=None, alias="textExpression")
    logical_expression: str | None = Field(default=None, alias="logicalExpression")
    template: SyntaxTemplate | None = None


class BiomedicalConceptProperty(BaseModel):
    """Value-level metadata property of a CDISC USDM Biomedical Concept."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str
    label: str | None = None
    cdash_variable: str | None = Field(default=None, alias="cdashVariable")
    data_type: str = Field(default="text", alias="dataType")
    mandatory: bool = False
    range: str | None = None
    options: list[str] = Field(default_factory=list)
    config: dict[str, Any] = Field(default_factory=dict)
    grid_span: int = Field(default=12, alias="gridSpan")
    unit: str | None = None


class BiomedicalConcept(BaseModel):
    """CDISC USDM Biomedical Concept definition and CDASH domain mapping."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str
    label: str | None = None
    concept_code: str | None = Field(default=None, alias="conceptCode")
    display_name: str | None = Field(default=None, alias="displayName")
    definition: str | None = None
    cdash_domain: str | None = Field(default=None, alias="cdashDomain")
    cdash_variable: str | None = Field(default=None, alias="cdashVariable")
    data_type: str = Field(default="text", alias="dataType")
    allowable_units: list[str] = Field(default_factory=list, alias="allowableUnits")
    codelist: list[str] = Field(default_factory=list)
    properties: list[BiomedicalConceptProperty] = Field(default_factory=list)


class Activity(BaseModel):
    """Study activity or procedure definition."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str
    description: str | None = None
    cdash_domain: str | None = Field(default=None, alias="cdashDomain")
    biomedical_concept_code: str | None = Field(
        default=None, alias="biomedicalConceptCode"
    )
    biomedical_concept_ids: list[str] = Field(
        default_factory=list, alias="biomedicalConceptIds"
    )
    biomedical_concepts: list[BiomedicalConcept] = Field(
        default_factory=list, alias="biomedicalConcepts"
    )
    assigned_visit_names: list[str] = Field(
        default_factory=list, alias="assignedVisitNames"
    )
    assigned_encounter_ids: list[str] = Field(
        default_factory=list, alias="assignedEncounterIds"
    )
    defined_procedures: list[dict[str, Any]] = Field(
        default_factory=list, alias="definedProcedures"
    )


class Encounter(BaseModel):
    """Study encounter / visit definition."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str
    encounter_type: str = Field(default="Visit", alias="encounterType")
    target_day: int | None = Field(default=None, alias="targetDay")
    window_lower: int | None = Field(default=None, alias="windowLower")
    window_upper: int | None = Field(default=None, alias="windowUpper")
    window_lower_days: int | None = Field(default=None, alias="windowLowerDays")
    window_upper_days: int | None = Field(default=None, alias="windowUpperDays")
    is_mandatory: bool = Field(default=True, alias="isMandatory")
    epoch_id: str | None = Field(default=None, alias="epochId")
    epoch_name: str | None = Field(default=None, alias="epochName")
    start_date: str | None = Field(default=None, alias="startDate")
    end_date: str | None = Field(default=None, alias="endDate")


class StudyArm(BaseModel):
    """Study arm definition."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str
    arm_type: str = Field(default="Treatment", alias="armType")
    description: str | None = None
    target_sample_size: int | None = Field(default=None, alias="targetSampleSize")


class StudyEpoch(BaseModel):
    """Study epoch definition."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str
    epoch_type: str = Field(default="Screening", alias="epochType")
    sequence_number: int = Field(default=1, alias="sequenceNumber")
    sequence_index: int = Field(default=1, alias="sequenceIndex")


class StudyDesign(BaseModel):
    """Study design containing arms, epochs, encounters, activities, concepts, and criteria."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str
    design_type: str | None = Field(default=None, alias="designType")
    arms: list[StudyArm] = Field(default_factory=list)
    epochs: list[StudyEpoch] = Field(default_factory=list)
    encounters: list[Encounter] = Field(default_factory=list)
    activities: list[Activity] = Field(default_factory=list)
    biomedical_concepts: list[BiomedicalConcept] = Field(
        default_factory=list, alias="biomedicalConcepts"
    )
    eligibility_criteria: list[EligibilityCriterion] = Field(
        default_factory=list, alias="eligibilityCriteria"
    )


class StudyVersion(BaseModel):
    """Study version containing version metadata and study designs."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    version_tag: str = Field(default="1.0", alias="versionTag")
    status: str = "DRAFT"
    version_index: int = Field(default=1, alias="versionIndex")
    study_designs: list[StudyDesign] = Field(default_factory=list, alias="studyDesigns")


class USDMStudy(BaseModel):
    """Root USDM protocol study specification container."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

    id: str
    name: str = ""
    protocol_title: str = Field(default="", alias="protocolTitle")
    protocol_id: str | None = Field(default=None, alias="protocolId")
    phase: str | None = None
    therapeutic_area: str | None = Field(default=None, alias="therapeuticArea")
    usdm_version: str = Field(default="3.0", alias="usdmVersion")
    study_versions: list[StudyVersion] = Field(
        default_factory=list, alias="studyVersions"
    )
    study_designs: list[StudyDesign] = Field(default_factory=list, alias="studyDesigns")
    biomedical_concepts: list[BiomedicalConcept] = Field(
        default_factory=list, alias="biomedicalConcepts"
    )
