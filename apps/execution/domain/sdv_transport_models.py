"""Pydantic transport schemas for bulk SDV sign-off and query generation REST API.

Requirements: PRD-SYS-001
"""

# Phase 1 — Backend Contracts and Domain Support (PRD-SYS-001)
import enum
import uuid

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
    target_ids: list[str] = Field(
        ...,
        description="List of target database or artifact IDs corresponding to the scope",
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )
    site_id: str | None = Field(
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

    bulk_id: str | None = Field(
        None, description="Unique bulk signature operation identifier"
    )
    content_digest: str = Field(..., description="SHA-256 digest of bulk signed data")
    timestamp_utc: str = Field(
        ..., description="UTC ISO timestamp of signature execution"
    )
    audit_tx: str = Field(..., description="Immutable GxP audit ledger transaction ID")
    verified_count: int | None = Field(
        None, description="Total number of successfully verified SDV items"
    )
    verified_target_ids: list[str] | None = Field(
        None, description="List of target IDs that were successfully signed"
    )
    skipped_targets: list[dict] | None = Field(
        default_factory=list,
        description="List of skipped targets with details on skip reasons",
    )

    signed_count: int = Field(
        ..., description="Total number of successfully signed SDV items"
    )
    signed_target_ids: list[str] = Field(
        ..., description="List of target IDs that were successfully signed"
    )
    skipped_target_ids: list[str] = Field(
        ...,
        description="List of target IDs that were skipped or already signed",
    )


class QueryTargetDescriptor(BaseModel):
    """Coordinate fields representing the specific target of a query.

    Requirements: PRD-SYS-001
    """

    study_id: str | None = Field(None, description="Target study trial identifier")
    subject_id: str = Field(..., description="Target clinical trial subject ID")
    visit_id: str = Field(..., description="Target visit identifier")
    domain: str = Field(..., description="Target SDTM domain code")
    test_code: str = Field(..., description="Target clinical test code")
    observation_id: str | None = Field(
        None, description="Optional target unique clinical observation ID"
    )
    form_id: str | None = Field(None, description="Optional form identifier")
    field_id: str | None = Field(None, description="Optional field identifier")
    explanation: str | None = Field(
        None,
        description="Contextual explanation/issue description triggering query generation",
    )


class BulkQueryGenerationRequest(BaseModel):
    """Request payload to execute bulk clinical query generation.

    Requirements: PRD-SYS-001
    """

    study_id: str | None = Field(None, description="Target protocol study ID")
    site_id: str | None = Field(None, description="Optional target site identifier")
    subject_id: str | None = Field(
        None, description="Optional target subject identifier"
    )
    targets: list[QueryTargetDescriptor] = Field(
        ..., description="List of query target coordinate fields and explanations"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class BulkQueryGenerationResponse(BaseModel):
    """Response payload following bulk query generation execution.

    Requirements: PRD-SYS-001
    """

    batch_id: str | None = Field(None, description="Unique bulk query batch identifier")
    audit_tx: str | None = Field(
        None, description="Immutable GxP audit ledger transaction ID"
    )
    generated_count: int = Field(..., description="Total number of generated queries")
    generated_query_ids: list[str] = Field(
        ..., description="List of generated unique query IDs"
    )
    skipped_targets: list[QueryTargetDescriptor] = Field(
        default_factory=list,
        description="List of target descriptors that were skipped due to already having an active query",
    )

    # Legacy fields for backward compatibility during phased rollouts
    generated_count: int | None = Field(
        None, description="Legacy generated query count"
    )
    timestamp_utc: str | None = Field(
        None, description="UTC ISO timestamp of query generation execution"
    )


class SdvFlagSeverity(enum.StrEnum):
    """SDV flag severity levels.

    Requirements: PRD-SYS-001
    """

    MINOR = "MINOR"
    MAJOR = "MAJOR"
    CRITICAL = "CRITICAL"


class FlagTargetDescriptor(BaseModel):
    """Coordinate fields representing the specific target of an item-level SDV flag.

    Requirements: PRD-SYS-001
    """

    target_id: str = Field(
        ..., description="The unique database or identifier of the item being flagged"
    )
    observation_id: str | None = Field(
        None, description="Optional associated clinical observation ID"
    )
    form_id: str | None = Field(None, description="Optional associated eCRF form ID")
    field_id: str | None = Field(None, description="Optional associated form field ID")
    flag_reason: str = Field(
        ..., description="The per-item reasoning/justification for applying this flag"
    )
    flag_severity: SdvFlagSeverity = Field(
        ..., description="The severity level of the item-level SDV flag"
    )


class SdvFlagRequest(BaseModel):
    """Payload to request the addition of item-level SDV flags.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target subject ID")
    scope: str = Field(..., description="SDV scope boundary: FIELD, PAGE, or VISIT")
    targets: list[FlagTargetDescriptor] = Field(
        ..., description="List of target descriptors representing items to flag"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )
    site_id: str | None = Field(
        None, description="Optional site identifier for the targets"
    )
    signing_reason: str = Field(
        default="CRA/monitor-gated bulk SDV sign-off",
        description="GxP Part 11 signature meaning or reason",
    )


class SdvResolveRequest(BaseModel):
    """Payload to request the resolution of item-level SDV flags.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target subject ID")
    scope: str = Field(..., description="SDV scope boundary: FIELD, PAGE, or VISIT")
    targets: list[FlagTargetDescriptor] | None = Field(
        None, description="Optional list of target descriptors to resolve"
    )
    target_ids: list[str] | None = Field(
        None, description="Optional flat list of target identifiers to resolve"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )
    site_id: str | None = Field(
        None, description="Optional site identifier for the targets"
    )


class SdvFlagResponse(BaseModel):
    """Response payload following the application of item-level SDV flags.

    Requirements: PRD-SYS-001
    """

    flag_id: str | None = Field(
        None, description="Unique item-level flag operation identifier"
    )
    content_digest: str = Field(..., description="SHA-256 digest of flagged data")
    timestamp_utc: str = Field(..., description="UTC ISO timestamp of flag execution")
    audit_tx: str = Field(..., description="Immutable GxP audit ledger transaction ID")
    flagged_count: int = Field(
        ..., description="Total number of successfully flagged SDV items"
    )
    flagged_target_ids: list[str] = Field(
        ..., description="List of target IDs that were successfully flagged"
    )
    skipped_target_ids: list[str] = Field(
        ...,
        description="List of target IDs that were skipped or already flagged",
    )


class SdvResolveResponse(BaseModel):
    """Response payload following the resolution of item-level SDV flags.

    Requirements: PRD-SYS-001
    """

    resolution_id: str | None = Field(
        None, description="Unique item-level resolution operation identifier"
    )
    content_digest: str = Field(..., description="SHA-256 digest of resolved data")
    timestamp_utc: str = Field(
        ..., description="UTC ISO timestamp of resolution execution"
    )
    audit_tx: str = Field(..., description="Immutable GxP audit ledger transaction ID")
    resolved_count: int = Field(
        ..., description="Total number of successfully resolved SDV items"
    )
    resolved_target_ids: list[str] = Field(
        ..., description="List of target IDs that were successfully resolved"
    )
    skipped_target_ids: list[str] = Field(
        ...,
        description="List of target IDs that were skipped or already resolved",
    )
