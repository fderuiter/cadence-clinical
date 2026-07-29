"""
Protocol document rendering architecture and content contract.

This module provides the shared Pydantic v2 domain models and presentation-oriented
view models for automated protocol narrative, synopsis, and Schedule of Activities (SoA)
documents in compliance with FDA 21 CFR Part 11 and CDISC USDM.
"""

from datetime import datetime, timezone
from typing import List, Optional

import usdm_model
from pydantic import BaseModel, Field, model_validator


class ExportMetadata(BaseModel):
    """
    Standard 21 CFR Part 11 compliant metadata fields for persisted or exported
    protocol documents.
    """

    creator: str = Field(
        ...,
        description="The unique identifier (e.g. username/OIDC user_id) of the user who generated/exported the document.",
    )
    timestamp: datetime = Field(
        default_factory=lambda: datetime.now(timezone.utc),
        description="Chronological UTC timestamp when the document export was requested.",
    )
    change_reason: Optional[str] = Field(
        None,
        description="Mandatory explanation or audit justification for creating or mutating this document version (required if version_index > 1).",
    )
    version_index: int = Field(
        default=1,
        description="Sequential version index or counter, starting at 1.",
    )

    @model_validator(mode="after")
    def validate_version_metadata(self) -> "ExportMetadata":
        """
        Ensures that change_reason is non-empty/non-blank for version index > 1
        to satisfy strict 21 CFR Part 11 compliance.
        """
        if self.version_index < 1:
            raise ValueError("version_index must be greater than or equal to 1")
        if self.version_index > 1:
            if not self.change_reason or not self.change_reason.strip():
                raise ValueError(
                    "change_reason is required and must be non-empty for follow-up versions (version_index > 1)"
                )
        return self


class NarrativeItemView(BaseModel):
    """
    Presentation view of a single narrative content block (e.g., paragraph, list item, or note).
    """

    id: str = Field(
        ..., description="Unique identifier for the narrative content item."
    )
    name: Optional[str] = Field(None, description="Optional name/tag for the item.")
    text: str = Field(..., description="The narrative text content.")
    order: int = Field(..., description="Sequential sorting order within its section.")


class NarrativeSectionView(BaseModel):
    """
    Presentation view of a nested or top-level section of the protocol narrative.
    """

    section_id: str = Field(..., description="Unique identifier of the section.")
    section_number: Optional[str] = Field(
        None,
        description="Formatted section hierarchy identifier (e.g., '1.1', '2.3.1').",
    )
    title: str = Field(..., description="The heading or title of the section.")
    items: List[NarrativeItemView] = Field(
        default_factory=list,
        description="List of narrative content items belonging directly to this section.",
    )
    subsections: List["NarrativeSectionView"] = Field(
        default_factory=list,
        description="Subsections nested inside this section.",
    )
    order: int = Field(..., description="Sequential sorting order within its parent.")
    derived_from_soa: bool = Field(
        False, description="Flag indicating selective lineage."
    )


class SynopsisView(BaseModel):
    """
    High-level, presentation-oriented clinical trial protocol synopsis view.
    """

    study_id: str = Field(..., description="The unique study identifier.")
    protocol_title: str = Field(..., description="The formal title of the protocol.")
    protocol_number: Optional[str] = Field(
        None, description="Sponsor protocol identification number."
    )
    sponsor_name: Optional[str] = Field(None, description="Name of the study sponsor.")
    phase: Optional[str] = Field(
        None, description="Clinical trial phase (e.g. Phase I, Phase II)."
    )
    objectives: List[str] = Field(
        default_factory=list,
        description="Key objectives of the clinical trial represented as strings.",
    )
    study_design_type: Optional[str] = Field(
        None,
        description="The structural design type (e.g., Randomized, Double-Blind, Parallel).",
    )
    population: Optional[str] = Field(
        None, description="Summary of target study population and eligibility criteria."
    )
    sample_size: Optional[int] = Field(
        None, description="Planned total sample size of trial subjects."
    )
    duration: Optional[str] = Field(
        None, description="Planned duration of participant involvement."
    )
    interventions: List[str] = Field(
        default_factory=list,
        description="Summary list of study interventions/treatments.",
    )


class SoAHeaderArm(BaseModel):
    """
    Presentation header representing a trial Study Arm.
    """

    arm_id: str = Field(..., description="Unique arm identifier.")
    arm_name: str = Field(
        ..., description="Name of the study arm (e.g., Active, Placebo)."
    )
    sequence: int = Field(..., description="Sequence number of the arm.")


class SoAHeaderEpoch(BaseModel):
    """
    Presentation header representing a trial Study Epoch.
    """

    epoch_id: str = Field(..., description="Unique epoch identifier.")
    epoch_name: str = Field(
        ..., description="Name of the study epoch (e.g., Treatment, Follow-up)."
    )
    sequence: int = Field(..., description="Sequence number of the epoch.")
    arm_id: Optional[str] = Field(None, description="Optional associated arm ID.")


class SoAHeaderEncounter(BaseModel):
    """
    Presentation header representing a visit or Encounter within a Study Epoch.
    """

    encounter_id: str = Field(..., description="Unique encounter/visit identifier.")
    encounter_name: str = Field(..., description="Name of the encounter/visit.")
    epoch_id: str = Field(..., description="Associated study epoch identifier.")
    sequence: int = Field(..., description="Sequence number of the encounter/visit.")
    arm_id: Optional[str] = Field(None, description="Optional associated arm ID.")


class SoACellView(BaseModel):
    """
    An individual cell within the SoA matrix indicating applicability of an activity at an encounter.
    """

    activity_id: str = Field(..., description="Target activity/procedure identifier.")
    encounter_id: str = Field(..., description="Target encounter/visit identifier.")
    epoch_id: str = Field(..., description="Associated study epoch identifier.")
    is_applicable: bool = Field(
        ...,
        description="Whether the activity is planned to occur during this encounter.",
    )
    details: Optional[str] = Field(
        None, description="Optional timing windows, constraints, or instruction notes."
    )
    arm_id: Optional[str] = Field(None, description="Optional associated arm ID.")
    derived_from_soa: bool = Field(
        False, description="Flag indicating selective lineage."
    )


class SoARowView(BaseModel):
    """
    A single row in the SoA matrix table representing a specific activity and its cell mappings.
    """

    activity_id: str = Field(..., description="Unique activity/procedure identifier.")
    activity_name: str = Field(
        ..., description="Name or label of the activity/procedure."
    )
    cells: List[SoACellView] = Field(
        default_factory=list,
        description="Applicability cell mapping for each encounter column.",
    )
    derived_from_soa: bool = Field(
        False, description="Flag indicating selective lineage."
    )


class SoAMatrixView(BaseModel):
    """
    Presentation view of the Schedule of Activities (SoA) matrix table.
    """

    epochs: List[SoAHeaderEpoch] = Field(
        default_factory=list,
        description="Ordered list of Study Epoch columns.",
    )
    encounters: List[SoAHeaderEncounter] = Field(
        default_factory=list,
        description="Ordered list of Encounter/Visit sub-columns.",
    )
    rows: List[SoARowView] = Field(
        default_factory=list,
        description="Ordered list of row-wise activity procedures.",
    )
    arms: List[SoAHeaderArm] = Field(
        default_factory=list,
        description="Ordered list of Study Arm columns.",
    )


class RenderedProtocolDocument(BaseModel):
    """
    The parent, standard wrapper representing the entire rendered clinical protocol document,
    enforcing clean presentation structures alongside the official CDISC USDM source study model.
    """

    metadata: ExportMetadata = Field(
        ..., description="21 CFR Part 11 compliant document version metadata."
    )
    synopsis: SynopsisView = Field(
        ..., description="The presentation synopsis overview."
    )
    narrative_sections: List[NarrativeSectionView] = Field(
        default_factory=list,
        description="The ordered and structured narrative sections.",
    )
    soa_matrix: SoAMatrixView = Field(
        ...,
        description="The structured Schedule of Activities (SoA) presentation matrix.",
    )
    source_study: Optional[usdm_model.Study] = Field(
        None,
        description="Optional backup reference to the official, full CDISC USDM source model.",
    )
