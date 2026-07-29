"""
Structured clinical protocol block authoring and ICH M11 document models.

This module provides the core Pydantic v2 domain models for managing protocol blocks,
hierarchical section skeletons, and GxP compliant optimistic locking, audits, and
selective lineage metadata tracking.
"""

from enum import Enum
from typing import Any, Dict, List, Literal, Optional, Union

# Import shared Part 11 audit fields from sibling module
from audit import AuditFields
from pydantic import BaseModel, Field
from typing_extensions import Annotated


class BlockType(str, Enum):
    """
    Discriminator enum for supported protocol block variants.
    """

    NARRATIVE = "narrative"
    OBJECTIVE = "objective"
    ELIGIBILITY = "eligibility"
    SOA_DERIVED = "soa_derived"


class ProtocolBlock(AuditFields):
    """
    Base domain model for all clinical trial protocol blocks.
    Enforces stable block_id, ordering, nesting, audit, and locking fields.
    """

    block_id: str = Field(
        ...,
        description="Stable unique block identifier, preserved across versions.",
    )
    block_type: BlockType = Field(
        ...,
        description="The block type discriminator.",
    )
    order: int = Field(
        ...,
        description="Sequential order value used to determine display and compilation hierarchy.",
    )
    parent_id: Optional[str] = Field(
        None,
        description="Optional parent block identifier to support hierarchical nested block structures.",
    )
    provenance_metadata: Optional[Dict[str, Any]] = Field(
        None,
        description="Optional metadata tracking structural or ingestion lineage.",
    )
    locked_by: Optional[str] = Field(
        None,
        description="Unique identifier of the user currently holding a lock on this block.",
    )
    lock_token: Optional[str] = Field(
        None,
        description="Transient lock token value for optimistic concurrency controls.",
    )
    derived_from_soa: bool = Field(
        default=False,
        description="Flag indicating if this block is dynamically derived from Schedule of Activities (SoA) mutations.",
    )


class NarrativeBlock(ProtocolBlock):
    """
    Protocol block variant representing general clinical or narrative text blocks.
    """

    block_type: Literal[BlockType.NARRATIVE] = BlockType.NARRATIVE
    title: Optional[str] = Field(
        None,
        description="Optional title or heading of the narrative block.",
    )
    text: str = Field(
        ...,
        description="The rich clinical trial narrative description text.",
    )


class ObjectiveBlock(ProtocolBlock):
    """
    Protocol block variant directly mapped to a structured Study Objective.
    """

    block_type: Literal[BlockType.OBJECTIVE] = BlockType.OBJECTIVE
    objective_id: str = Field(
        ...,
        description="Stable identifier of the linked objective.",
    )
    text: str = Field(
        ...,
        description="The narrative or detail text describing the study objective.",
    )


class EligibilityBlock(ProtocolBlock):
    """
    Protocol block variant directly mapped to a versioned study eligibility criterion.
    """

    block_type: Literal[BlockType.ELIGIBILITY] = BlockType.ELIGIBILITY
    criterion_id: str = Field(
        ...,
        description="Stable identifier of the associated eligibility criterion node.",
    )
    criterion_type: str = Field(
        ...,
        description="Inclusion or exclusion classification of the associated criterion.",
    )
    text: str = Field(
        ...,
        description="The human-readable description of the eligibility rule.",
    )


class SoADerivedBlock(ProtocolBlock):
    """
    Protocol block variant selectively derived from Schedule of Activities (SoA) elements.
    """

    block_type: Literal[BlockType.SOA_DERIVED] = BlockType.SOA_DERIVED
    derived_from_soa: bool = Field(
        default=True,
        description="Always True for SoA-derived blocks to trace automatic lineage.",
    )
    source_entity_id: str = Field(
        ...,
        description="Stable identifier of the source SoA element (arm, procedure, timing window).",
    )
    source_entity_type: str = Field(
        ...,
        description="Entity type classification of the source (e.g. 'arm', 'procedure', 'timing_window').",
    )
    text: str = Field(
        ...,
        description="The auto-generated summary or structured description text representing this SoA entity.",
    )


# Annotated Union representing API-friendly discriminated block structures
ProtocolBlockUnion = Annotated[
    Union[NarrativeBlock, ObjectiveBlock, EligibilityBlock, SoADerivedBlock],
    Field(discriminator="block_type"),
]


class ICHSection(BaseModel):
    """
    Self-referential recursive Pydantic model for representing sequence-ordered ICH M11 document skeletons.
    """

    section_id: str = Field(
        ...,
        description="Stable unique identifier representing the document section.",
    )
    title: str = Field(
        ...,
        description="The display heading or title of the section.",
    )
    order: int = Field(
        ...,
        description="Sequential ordering rank of the section within its hierarchy level.",
    )
    children: List["ICHSection"] = Field(
        default_factory=list,
        description="Nested child subsections belonging to this section.",
    )


# Rebuild the self-referential model in Pydantic v2
ICHSection.model_rebuild()


def build_canonical_ich_skeleton() -> List[ICHSection]:
    """
    Factory function producing the canonical, sequence-ordered tree skeleton
    for ICH M11 compliant clinical trial protocols.
    """
    return [
        ICHSection(
            section_id="sec_1",
            title="Protocol Synopsis",
            order=1,
            children=[],
        ),
        ICHSection(
            section_id="sec_2",
            title="Introduction",
            order=2,
            children=[
                ICHSection(
                    section_id="sec_2_1",
                    title="Study Rationale",
                    order=1,
                    children=[],
                ),
                ICHSection(
                    section_id="sec_2_2",
                    title="Background and Scientific Rationale",
                    order=2,
                    children=[],
                ),
            ],
        ),
        ICHSection(
            section_id="sec_3",
            title="Study Objectives and Endpoints",
            order=3,
            children=[],
        ),
        ICHSection(
            section_id="sec_4",
            title="Study Design",
            order=4,
            children=[
                ICHSection(
                    section_id="sec_4_1",
                    title="Overall Design Description",
                    order=1,
                    children=[],
                ),
                ICHSection(
                    section_id="sec_4_2",
                    title="Scientific Rationale for Study Design",
                    order=2,
                    children=[],
                ),
            ],
        ),
        ICHSection(
            section_id="sec_5",
            title="Study Population and Eligibility",
            order=5,
            children=[],
        ),
        ICHSection(
            section_id="sec_6",
            title="Study Interventions",
            order=6,
            children=[],
        ),
    ]


# Global constant holding the canonical schema sequence tree
CANONICAL_ICH_SKELETON: List[ICHSection] = build_canonical_ich_skeleton()
