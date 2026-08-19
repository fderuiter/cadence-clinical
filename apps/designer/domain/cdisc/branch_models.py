"""Pydantic data models for Git-like protocol amendment branching and block diffing.

Requirements: PRD-SYS-001
"""

from typing import Any

from pydantic import BaseModel, Field


class ProtocolBranch(BaseModel):
    """Protocol amendment branch model.

    Requirements: PRD-SYS-001
    """

    branch_id: str = Field(..., description="Unique branch identifier")
    study_id: str = Field(..., description="Target protocol study ID")
    branch_name: str = Field(..., description="Branch name (e.g. amendment-v2.0-draft)")
    base_version: int = Field(1, description="Base baseline protocol version index")
    head_version: int = Field(1, description="Head working draft version index")
    status: str = Field(
        "draft", description="Branch lifecycle status: draft, merged, abandoned"
    )
    created_by: str = Field(..., description="Author user ID who created the branch")


class BlockDiff(BaseModel):
    """Block-level protocol node change diff representation.

    Requirements: PRD-SYS-001
    """

    block_id: str = Field(..., description="Target protocol node ID")
    block_type: str = Field(
        ...,
        description="Protocol block type: Objective, Endpoint, Arm, Epoch, Criterion, Encounter, Procedure",
    )
    change_type: str = Field(
        ..., description="Diff status: ADDED, MODIFIED, DELETED, UNCHANGED, REMOVED"
    )
    old_content: str | None = Field(
        None, description="Previous content string in baseline"
    )
    new_content: str | None = Field(
        None, description="Updated content string in amendment draft"
    )


class EntityDiff(BaseModel):
    """Entity-level visual diff with semantic tokens.

    Requirements: PRD-SYS-001
    """

    entity_id: str = Field(..., description="Unique entity identifier")
    entity_type: str = Field(
        ...,
        description="Entity type: Arm, Epoch, Encounter, Activity, Criterion, Form, Rule",
    )
    name: str = Field(..., description="Display name of the entity")
    change_type: str = Field(
        ..., description="Diff status: ADDED, REMOVED, MODIFIED, PRESERVED, UNCHANGED"
    )
    spec: str | None = Field(None, description="Specification or subtitle details")
    schedule: str | None = Field(None, description="Schedule timing / epoch details")
    delta_note: str | None = Field(
        None, description="Specific note describing the delta"
    )
    old_value: Any | None = Field(None, description="Previous baseline value")
    new_value: Any | None = Field(None, description="Amended new value")


class SchemaRevisionSummary(BaseModel):
    """Breakdown of schema revisions across USDM domain layers.

    Requirements: PRD-SYS-001
    """

    arms: dict[str, int] = Field(
        default_factory=lambda: {
            "added": 0,
            "removed": 0,
            "modified": 0,
            "unchanged": 0,
        }
    )
    epochs: dict[str, int] = Field(
        default_factory=lambda: {
            "added": 0,
            "removed": 0,
            "modified": 0,
            "unchanged": 0,
        }
    )
    encounters: dict[str, int] = Field(
        default_factory=lambda: {
            "added": 0,
            "removed": 0,
            "modified": 0,
            "unchanged": 0,
        }
    )
    activities: dict[str, int] = Field(
        default_factory=lambda: {
            "added": 0,
            "removed": 0,
            "modified": 0,
            "unchanged": 0,
        }
    )
    eligibility_criteria: dict[str, int] = Field(
        default_factory=lambda: {
            "added": 0,
            "removed": 0,
            "modified": 0,
            "unchanged": 0,
        }
    )
    forms: dict[str, int] = Field(
        default_factory=lambda: {
            "added": 0,
            "removed": 0,
            "modified": 0,
            "unchanged": 0,
        }
    )


class AmendmentImpactSummary(BaseModel):
    """Calculated operational and clinical burden impact of a protocol amendment.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """

    base_version: str = Field(..., description="Baseline protocol version tag")
    amended_version: str = Field(..., description="Amended protocol version tag")
    burden_delta: float = Field(
        0.0, description="Quantitative burden score delta (+/- burden index)"
    )
    affected_visits_count: int = Field(0, description="Total affected visits count")
    affected_visits: list[str] = Field(
        default_factory=list, description="Names of affected visits/encounters"
    )
    affected_activities_count: int = Field(
        0, description="Total affected procedures count"
    )
    affected_activities: list[str] = Field(
        default_factory=list, description="Names of affected activities/procedures"
    )
    schema_revisions: SchemaRevisionSummary = Field(
        default_factory=SchemaRevisionSummary,
        description="Detailed schema revision breakdown",
    )
    is_substantial: bool = Field(
        False, description="Whether amendment is substantial vs administrative"
    )
    requires_reconsent: bool = Field(
        False,
        description="Whether mandatory subject re-consent is mandated (PRD-SUB-007)",
    )
    estimated_cost_usd: float = Field(
        0.0, description="Estimated total cost impact in USD"
    )
    narrative_summary: str = Field(
        "", description="Executive clinical narrative summary of changes"
    )


class MigrationDirective(BaseModel):
    """Directive for in-flight subject migration and version transition.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """

    directive_id: str = Field(..., description="Unique migration directive ID")
    action: str = Field(
        ...,
        description="Directive action: RECONSENT_GATE, SCHEMA_UPGRADE, PRESERVE_HISTORICAL",
    )
    description: str = Field(..., description="Detailed directive description")
    affected_cohort: str = Field(
        "ACTIVE", description="Target subject cohort: ACTIVE, COMPLETED, ALL"
    )
    target_version: str = Field(..., description="Target protocol version tag")


class BranchAmendmentRequest(BaseModel):
    """Payload to create an immutable amendment branch.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """

    study_id: str = Field(..., description="Target study ID")
    base_version_tag: str = Field(
        "1.0.0", description="Base approved version tag to branch from"
    )
    amendment_type: str = Field(
        "minor", description="Amendment classification: 'major' or 'minor'"
    )
    requires_reconsent: bool = Field(
        False, description="Flag indicating if subject re-consent is mandated"
    )
    change_reason: str = Field(
        ..., description="GxP 21 CFR Part 11 change justification reason"
    )
    branch_name: str | None = Field(
        None, description="Optional custom branch name (e.g. amendment-v2.0-draft)"
    )


class BranchAmendmentResponse(BaseModel):
    """Response after creating an immutable amendment branch.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target study ID")
    branch_id: str = Field(..., description="Unique branch identifier")
    branch_name: str = Field(..., description="Branch name")
    base_version_tag: str = Field(..., description="Base version tag")
    new_version_tag: str = Field(..., description="New draft version tag")
    version_id: str = Field(..., description="New version node identifier")
    status: str = Field(..., description="Lifecycle status (e.g. DRAFT_AMENDMENT)")
    requires_reconsent: bool = Field(..., description="Re-consent requirement flag")
    created_by: str = Field(..., description="User ID who created branch")
    created_at: str = Field(..., description="UTC ISO creation timestamp")


class SemanticDiffRequest(BaseModel):
    """Request to compute semantic multi-layer amendment diff.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Protocol study identifier")
    base_version_tag: str = Field(..., description="Base version tag")
    amended_version_tag: str = Field(..., description="Amended target version tag")
    base_payload: dict[str, Any] | None = Field(
        None, description="Optional explicit base protocol payload"
    )
    draft_payload: dict[str, Any] | None = Field(
        None, description="Optional explicit amended protocol payload"
    )


class SemanticDiffResponse(BaseModel):
    """Multi-layer visual and semantic protocol diff response.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """

    study_id: str = Field(..., description="Protocol study identifier")
    base_version_tag: str = Field(..., description="Baseline version tag")
    amended_version_tag: str = Field(..., description="Amended version tag")
    usdm_graph_diffs: list[EntityDiff] = Field(
        default_factory=list, description="USDM Graph node diffs"
    )
    soa_matrix_diffs: list[EntityDiff] = Field(
        default_factory=list, description="Schedule of Activities matrix diffs"
    )
    eligibility_diffs: list[EntityDiff] = Field(
        default_factory=list, description="Eligibility criteria diffs"
    )
    ecrf_form_diffs: list[EntityDiff] = Field(
        default_factory=list, description="eCRF form and field diffs"
    )
    impact_summary: AmendmentImpactSummary = Field(
        ..., description="Calculated amendment impact summary"
    )
    migration_directives: list[MigrationDirective] = Field(
        default_factory=list, description="Automated in-flight migration directives"
    )


class AmendmentComparisonResponse(BaseModel):
    """Comparative visual diff response between base and draft branches.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Protocol study ID")
    source_branch: str = Field(..., description="Source baseline branch")
    target_branch: str = Field(..., description="Target amendment branch")
    diffs: list[BlockDiff] = Field(
        default_factory=list, description="List of block-level diffs"
    )
    total_changes: int = Field(
        0, description="Total number of modified or added blocks"
    )
