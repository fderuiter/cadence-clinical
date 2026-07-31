"""Pydantic transport schemas for bulk SDV sign-off and query generation REST API.

Requirements: PRD-SYS-001
"""

from typing import List, Optional, Dict, Any
from pydantic import BaseModel, Field


class BulkSdvSignOffRequest(BaseModel):
    """Request payload to execute bulk SDV sign-offs.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target subject ID")
    scope: str = Field(..., description="SDV scope boundary: FIELD, PAGE, or VISIT")
    target_ids: List[str] = Field(
        ...,
        description="List of target database or artifact IDs corresponding to the scope",
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )
    signing_reason: str = Field(
        "CRA bulk sign-off", description="21 CFR Part 11 signature purpose/meaning"
    )
    site_id: Optional[str] = Field(
        None, description="Optional site identifier for the targets"
    )


class BulkSdvSignOffResponse(BaseModel):
    """Response payload following bulk SDV sign-off execution.

    Requirements: PRD-SYS-001
    """

    bulk_id: str = Field(..., description="Unique bulk sign-off ID")
    content_digest: str = Field(..., description="SHA-256 digest of bulk signed data")
    timestamp_utc: str = Field(
        ..., description="UTC ISO timestamp of signature execution"
    )
    audit_tx: str = Field(..., description="Immutable GxP audit ledger transaction ID")
    verified_count: int = Field(
        ..., description="Total number of successfully signed SDV items"
    )
    verified_target_ids: List[str] = Field(
        ..., description="List of target IDs that were successfully signed"
    )
    skipped_targets: List[Dict[str, Any]] = Field(
        ..., description="List of skipped targets with reasons"
    )


class QueryTargetDescriptor(BaseModel):
    """Coordinate fields representing the specific target of a query.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target clinical trial subject ID")
    visit_id: str = Field(..., description="Target visit identifier")
    domain: str = Field(..., description="Target SDTM domain code")
    test_code: str = Field(..., description="Target clinical test code")
    observation_id: Optional[str] = Field(
        None, description="Target unique clinical observation ID"
    )
    form_id: Optional[str] = Field(None, description="Target unique clinical form ID")
    field_id: Optional[str] = Field(
        None, description="Target unique clinical field ID"
    )
    explanation: str = Field(
        ...,
        description="Contextual explanation/issue description triggering query generation",
    )


class BulkQueryGenerationRequest(BaseModel):
    """Request payload to execute bulk clinical query generation.

    Requirements: PRD-SYS-001
    """

    targets: List[QueryTargetDescriptor] = Field(
        ..., description="List of query target coordinate fields and explanations"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class BulkQueryGenerationResponse(BaseModel):
    """Response payload following bulk query generation execution.

    Requirements: PRD-SYS-001
    """

    batch_id: str = Field(..., description="Unique batch generation ID")
    audit_tx: str = Field(..., description="Immutable GxP audit ledger transaction ID")
    generated_query_ids: List[str] = Field(
        ..., description="List of generated unique query IDs"
    )
    skipped_targets: List[Dict[str, Any]] = Field(
        ..., description="List of target descriptors that were skipped"
    )
