"""Pydantic transport schemas for bulk SDV sign-off and query generation REST API.

Requirements: PRD-SYS-001
"""

import uuid
from typing import List, Optional, Any

from pydantic import BaseModel, Field, model_validator


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
        default="CRA Source Data Verification Sign-off",
        description="21 CFR Part 11 signature purpose/meaning"
    )
    site_id: Optional[str] = Field(
        None, description="Optional site identifier for the targets"
    )


class BulkSdvSignOffResponse(BaseModel):
    """Response payload following bulk SDV sign-off execution.

    Requirements: PRD-SYS-001
    """

    bulk_id: str = Field(
        default_factory=lambda: f"bulk_{uuid.uuid4().hex[:8]}",
        description="Unique identifier for the bulk operation"
    )
    content_digest: str = Field(..., description="SHA-256 digest of bulk signed data")
    timestamp_utc: str = Field(
        ..., description="UTC ISO timestamp of signature execution"
    )
    audit_tx: str = Field(
        default_factory=lambda: f"tx_{uuid.uuid4().hex[:12]}",
        description="Immutable GxP audit ledger transaction ID"
    )
    verified_count: int = Field(
        default=0,
        description="Total number of successfully signed/verified SDV items"
    )
    verified_target_ids: List[str] = Field(
        default_factory=list,
        description="List of target IDs that were successfully signed"
    )
    skipped_targets: List[dict] = Field(
        default_factory=list,
        description="List of target IDs that were skipped, each recording the target_id and a skip reason"
    )

    # Legacy fields kept for backward compatibility
    signed_count: int = Field(
        default=0,
        description="Total number of successfully signed SDV items"
    )
    signed_target_ids: List[str] = Field(
        default_factory=list,
        description="List of target IDs that were successfully signed"
    )
    skipped_target_ids: List[str] = Field(
        default_factory=list,
        description="List of target IDs that were skipped or already signed"
    )

    @model_validator(mode="after")
    def sync_verified_fields(self) -> "BulkSdvSignOffResponse":
        """Synchronize new and legacy fields on instantiation."""
        if self.verified_count == 0 and self.signed_count != 0:
            self.verified_count = self.signed_count
        elif self.signed_count == 0 and self.verified_count != 0:
            self.signed_count = self.verified_count

        if not self.verified_target_ids and self.signed_target_ids:
            self.verified_target_ids = list(self.signed_target_ids)
        elif not self.signed_target_ids and self.verified_target_ids:
            self.signed_target_ids = list(self.verified_target_ids)

        if not self.skipped_targets and self.skipped_target_ids:
            self.skipped_targets = [
                {"target_id": tid, "reason": "Already verified"}
                for tid in self.skipped_target_ids
            ]
        elif not self.skipped_target_ids and self.skipped_targets:
            self.skipped_target_ids = [
                item.get("target_id") or item.get("id") or str(item)
                for item in self.skipped_targets
            ]

        return self


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
        None, description="Optional target unique clinical observation ID"
    )
    form_id: Optional[str] = Field(
        None, description="Optional target eCRF form ID"
    )
    field_id: Optional[str] = Field(
        None, description="Optional target eCRF field ID"
    )
    explanation: Optional[str] = Field(
        None,
        description="Optional contextual explanation/issue description triggering query generation",
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

    batch_id: str = Field(
        default_factory=lambda: f"batch_{uuid.uuid4().hex[:8]}",
        description="Unique identifier for the bulk query generation batch"
    )
    audit_tx: str = Field(
        default_factory=lambda: f"tx_{uuid.uuid4().hex[:12]}",
        description="Immutable GxP audit ledger transaction ID"
    )
    generated_query_ids: List[str] = Field(
        ..., description="List of generated unique query IDs"
    )
    skipped_targets: List[Any] = Field(
        default_factory=list,
        description="List of targets that were skipped with their skip reasons"
    )

    # Legacy fields kept for backward compatibility
    generated_count: Optional[int] = Field(
        None,
        description="Total number of generated queries"
    )
    timestamp_utc: Optional[str] = Field(
        None,
        description="UTC ISO timestamp of query generation execution"
    )

    @model_validator(mode="after")
    def sync_query_fields(self) -> "BulkQueryGenerationResponse":
        """Synchronize new and legacy fields on instantiation."""
        if self.generated_count is None and self.generated_query_ids:
            self.generated_count = len(self.generated_query_ids)
        return self
