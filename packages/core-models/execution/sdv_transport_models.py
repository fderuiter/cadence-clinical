"""Pydantic transport schemas for bulk SDV sign-off and query generation REST API.

Requirements: PRD-SYS-001
"""

import uuid
from typing import Any, List, Optional

from pydantic import BaseModel, Field


def generate_audit_tx() -> str:
    """Generate GxP audit ledger transaction ID following: tx_{hex[:12]}"""
    return f"tx_{uuid.uuid4().hex[:12]}"


def generate_identifier(prefix: str) -> str:
    """Generate unique identifier following: {prefix}_{hex[:8]}"""
    return f"{prefix}_{uuid.uuid4().hex[:8]}"


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
    site_id: Optional[str] = Field(
        None, description="Optional site identifier for the targets"
    )
    signing_reason: str = Field(
        default="CRA/monitor-gated bulk SDV sign-off",
        description="GxP Part 11 signature meaning or reason",
    )


class BulkSdvSignOffResponse(BaseModel):
    """Response payload following bulk SDV sign-off execution.

    Requirements: PRD-SYS-001
    """

    bulk_id: Optional[str] = Field(
        None, description="Unique bulk signature operation identifier"
    )
    content_digest: str = Field(..., description="SHA-256 digest of bulk signed data")
    timestamp_utc: str = Field(
        ..., description="UTC ISO timestamp of signature execution"
    )
    audit_tx: str = Field(..., description="Immutable GxP audit ledger transaction ID")
    verified_count: Optional[int] = Field(
        None, description="Total number of successfully verified SDV items"
    )
    verified_target_ids: Optional[List[str]] = Field(
        None, description="List of target IDs that were successfully signed"
    )
    skipped_targets: Optional[List[dict]] = Field(
        default_factory=list,
        description="List of skipped targets with details on skip reasons",
    )

    signed_count: int = Field(
        ..., description="Total number of successfully signed SDV items"
    )
    signed_target_ids: List[str] = Field(
        ..., description="List of target IDs that were successfully signed"
    )
    skipped_target_ids: List[str] = Field(
        ...,
        description="List of target IDs that were skipped or already signed",
    )


class QueryTargetDescriptor(BaseModel):
    """Coordinate fields representing the specific target of a query.

    Requirements: PRD-SYS-001
    """

    study_id: Optional[str] = Field(None, description="Target study trial identifier")
    subject_id: str = Field(..., description="Target clinical trial subject ID")
    visit_id: str = Field(..., description="Target visit identifier")
    domain: str = Field(..., description="Target SDTM domain code")
    test_code: str = Field(..., description="Target clinical test code")
    observation_id: Optional[str] = Field(
        None, description="Optional target unique clinical observation ID"
    )
    form_id: Optional[str] = Field(None, description="Optional form identifier")
    field_id: Optional[str] = Field(None, description="Optional field identifier")
    explanation: Optional[str] = Field(
        None,
        description="Contextual explanation/issue description triggering query generation",
    )


class BulkQueryGenerationRequest(BaseModel):
    """Request payload to execute bulk clinical query generation.

    Requirements: PRD-SYS-001
    """

    study_id: Optional[str] = Field(None, description="Target protocol study ID")
    site_id: Optional[str] = Field(None, description="Optional target site identifier")
    subject_id: Optional[str] = Field(
        None, description="Optional target subject identifier"
    )
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

    batch_id: Optional[str] = Field(
        None, description="Unique bulk query batch identifier"
    )
    audit_tx: Optional[str] = Field(
        None, description="Immutable GxP audit ledger transaction ID"
    )
    generated_query_ids: List[str] = Field(
        ..., description="List of generated unique query IDs"
    )
    skipped_targets: List[Any] = Field(
        default_factory=list,
        description="List of target descriptors that were skipped",
    )

    # Legacy fields for backward compatibility during phased rollouts
    generated_count: Optional[int] = Field(
        None, description="Legacy generated query count"
    )
    timestamp_utc: Optional[str] = Field(
        None, description="UTC ISO timestamp of query generation execution"
    )
