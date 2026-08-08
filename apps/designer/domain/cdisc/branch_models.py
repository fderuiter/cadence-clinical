"""Pydantic data models for Git-like protocol amendment branching and block diffing.

Requirements: PRD-SYS-001
"""

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
        description="Protocol block type: Objective, Endpoint, Arm, Epoch, Criterion",
    )
    change_type: str = Field(
        ..., description="Diff status: ADDED, MODIFIED, DELETED, UNCHANGED"
    )
    old_content: str | None = Field(
        None, description="Previous content string in baseline"
    )
    new_content: str | None = Field(
        None, description="Updated content string in amendment draft"
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
