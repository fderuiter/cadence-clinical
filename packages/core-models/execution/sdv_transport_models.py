"""Pydantic transport schemas for CRA monitoring bulk SDV and query generation APIs.

Requirements: PRD-SYS-001
"""

from typing import List, Optional

from pydantic import BaseModel, Field


class BulkSdvSignOffRequest(BaseModel):
    """Request payload for CRA bulk Source Data Verification (SDV) sign-off.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target subject ID")
    scope: str = Field(..., description="Verification scope: FIELD, PAGE, or VISIT")
    target_ids: List[str] = Field(
        ..., description="List of target artifact or observation IDs to verify"
    )
    reason_for_change: str = Field(
        ..., description="GxP reason for change/signing reason"
    )
    site_id: Optional[str] = Field(None, description="Optional target site ID")


class BulkSdvSignOffResponse(BaseModel):
    """Response payload following successful bulk SDV sign-off execution.

    Requirements: PRD-SYS-001
    """

    signed_count: int = Field(
        ..., description="Total number of targets successfully verified and signed"
    )
    signed_target_ids: List[str] = Field(
        ..., description="List of target IDs successfully verified"
    )
    skipped_target_ids: List[str] = Field(
        ..., description="List of target IDs skipped during verification"
    )
    content_digest: str = Field(
        ..., description="SHA-256 digest of signed data for GxP verification"
    )
    timestamp_utc: str = Field(
        ..., description="UTC ISO timestamp of bulk SDV sign-off execution"
    )
    audit_tx: str = Field(..., description="Immutable GxP audit ledger transaction ID")


class QueryTargetDescriptor(BaseModel):
    """Descriptor for coordinates identifying where a query should be generated.

    Requirements: PRD-SYS-001
    """

    subject_id: str = Field(..., description="Target subject ID")
    visit_id: Optional[str] = Field(None, description="Target visit ID coordinate")
    domain: Optional[str] = Field(
        None, description="Target domain coordinate (e.g., AE, VS, LB)"
    )
    test_code: Optional[str] = Field(None, description="Target test code coordinate")
    observation_id: Optional[str] = Field(
        None, description="Target clinical observation ID coordinate"
    )
    explanation: str = Field(..., description="The query text explanation or comment")


class BulkQueryGenerationRequest(BaseModel):
    """Request payload for bulk clinical query generation.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    site_id: Optional[str] = Field(None, description="Optional target site ID")
    subject_id: Optional[str] = Field(None, description="Optional target subject ID")
    query_targets: List[QueryTargetDescriptor] = Field(
        ..., description="List of query target descriptors to generate queries for"
    )
    reason_for_change: str = Field(
        ..., description="GxP reason for change for query generation auditing"
    )


class BulkQueryGenerationResponse(BaseModel):
    """Response payload following bulk query generation execution.

    Requirements: PRD-SYS-001
    """

    generated_count: int = Field(
        ..., description="Total number of queries successfully generated"
    )
    generated_query_ids: List[str] = Field(
        ..., description="List of generated clinical query IDs"
    )
    skipped_targets: List[QueryTargetDescriptor] = Field(
        ...,
        description="List of target descriptors skipped because they already have an active query",
    )
    timestamp_utc: str = Field(
        ..., description="UTC ISO timestamp of bulk query generation execution"
    )
