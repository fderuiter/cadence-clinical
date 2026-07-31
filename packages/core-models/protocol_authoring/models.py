"""
Structured clinical protocol block authoring and ICH M11 document models.

This module provides the core Pydantic v2 domain models for managing protocol blocks,
hierarchical section skeletons, and GxP compliant optimistic locking, audits, and
selective lineage metadata tracking.
"""

from datetime import datetime, timezone
from enum import Enum
from typing import Any, Dict, List, Literal, Optional, Union

# Import shared Part 11 audit fields from sibling module
from audit import AuditFields
from datetime_helpers import AwareDatetime
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
    section_id: Optional[str] = Field(
        None,
        description="Optional section identifier representing which ICH M11 section this block belongs to.",
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


class SectionReviewStatus(str, Enum):
    """
    Standard review statuses representing the lifecycle of an ICH section.
    """

    DRAFT = "DRAFT"
    IN_REVIEW = "IN_REVIEW"
    LOCKED = "LOCKED"
    APPROVED = "APPROVED"


class Comment(BaseModel):
    """
    Represent an individual block-anchored user review comment.
    """

    comment_id: str = Field(..., description="Unique comment identifier.")
    thread_id: str = Field(..., description="Linked thread identifier.")
    text: str = Field(..., description="Comment text body.")
    created_by: str = Field(..., description="Author user ID.")
    created_at: AwareDatetime = Field(
        default_factory=lambda: datetime.now(timezone.utc),
        description="Creation timestamp.",
    )
    updated_at: Optional[AwareDatetime] = Field(
        None,
        description="Optional modification timestamp.",
    )
    version_index: int = Field(
        default=1,
        description="Sequential version index for GxP auditing.",
    )


class CommentThread(BaseModel):
    """
    Represents a collection of ordered review comments anchored to a specific block and section.
    """

    thread_id: str = Field(..., description="Unique thread identifier.")
    block_id: str = Field(..., description="Anchor block identifier.")
    section_id: str = Field(..., description="Anchor section identifier.")
    study_id: str = Field(..., description="Associated study identifier.")
    status: str = Field(
        "open",
        description="Thread resolution status (open, resolved).",
    )
    created_by: str = Field(..., description="Thread creator user ID.")
    created_at: AwareDatetime = Field(
        default_factory=lambda: datetime.now(timezone.utc),
        description="Creation timestamp.",
    )
    block_version_index: int = Field(
        ...,
        description="The block's version_index at the time of thread creation.",
    )
    comments: List[Comment] = Field(
        default_factory=list,
        description="Ordered list of comments.",
    )


class SuggestionStatus(str, Enum):
    """
    Statuses for suggestion workflows.
    """

    PENDING = "pending"
    ACCEPTED = "accepted"
    REJECTED = "rejected"


class Suggestion(BaseModel):
    """
    Represents a proposed collaborative content replacement suggestion for a block.
    """

    suggestion_id: str = Field(..., description="Unique suggestion identifier.")
    block_id: str = Field(..., description="Anchor block identifier.")
    study_id: str = Field(..., description="Associated study identifier.")
    suggested_text: str = Field(..., description="Proposed replacement text.")
    original_text: str = Field(..., description="Original block text at proposed time.")
    status: SuggestionStatus = Field(
        SuggestionStatus.PENDING,
        description="Current suggestion status.",
    )
    created_by: str = Field(..., description="Proposer user ID.")
    created_at: AwareDatetime = Field(
        default_factory=lambda: datetime.now(timezone.utc),
        description="Creation timestamp.",
    )
    reason: str = Field(..., description="Rationale for the suggestion.")
    decision_reason: Optional[str] = Field(
        None,
        description="Rationale for acceptance or rejection.",
    )
    decided_by: Optional[str] = Field(None, description="User ID of decider.")
    decided_at: Optional[AwareDatetime] = Field(
        None,
        description="Timestamp of decision.",
    )
    block_version_index: int = Field(
        ...,
        description="The block's version_index at the time of proposing.",
    )
    version_index: int = Field(default=1, description="Sequential version index.")


class SectionReviewTransition(BaseModel):
    """
    Represents an immutable, audited, Part 11 compliant transition of a section review status.
    """

    transition_id: str = Field(
        ...,
        description="Unique transition tracking identifier.",
    )
    section_id: str = Field(..., description="Anchor section identifier.")
    study_id: str = Field(..., description="Associated study identifier.")
    from_status: SectionReviewStatus = Field(..., description="Source review status.")
    to_status: SectionReviewStatus = Field(
        ...,
        description="Destination review status.",
    )
    actor_id: str = Field(..., description="User ID executing status transition.")
    actor_role: str = Field(
        ...,
        description="Role string used to authorize status transition.",
    )
    reason_for_change: str = Field(
        ...,
        description="Part 11 change reason justification.",
    )
    timestamp: AwareDatetime = Field(
        default_factory=lambda: datetime.now(timezone.utc),
        description="Transition timestamp.",
    )
