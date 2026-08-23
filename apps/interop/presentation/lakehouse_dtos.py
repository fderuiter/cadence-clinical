"""Pydantic v2 transport DTOs for the Multi-Source Clinical Lakehouse and Version-Pinned Snapshots.

Requirements: PRD-CDR-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.interop.domain.lakehouse_models import (
    CanonicalClinicalRecord,
    ClinicalSnapshot,
    LakehouseMetrics,
    LockMilestoneType,
    SnapshotFilterCriteria,
    SnapshotVerificationResult,
)


class CreateSnapshotRequest(BaseModel):
    """Request payload to pool lakehouse records into an immutable version-pinned snapshot."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(
        ..., min_length=1, description="Clinical study identifier (e.g. STUDY-901)"
    )
    version_tag: str = Field(
        ..., min_length=1, description="Unique version tag (e.g. v1.0.0-INTERIM-1)"
    )
    milestone: LockMilestoneType = Field(
        ..., description="Target database lock milestone"
    )
    lock_id: str | None = Field(None, description="Optional associated DataLock ID")
    reason_for_snapshot: str = Field(
        ..., min_length=1, description="Formal GxP justification for snapshot creation"
    )
    filter_criteria: SnapshotFilterCriteria | None = Field(
        None,
        description="Optional stream, domain, subject, and time cutoff filtering criteria",
    )


class SnapshotResponse(BaseModel):
    """Response containing created or retrieved snapshot details."""

    model_config = ConfigDict(extra="forbid")

    snapshot: ClinicalSnapshot = Field(
        ..., description="Snapshot metadata and cryptographic seal"
    )
    message: str = Field(
        "Snapshot created and sealed successfully", description="Status message"
    )


class SnapshotListResponse(BaseModel):
    """Response containing list of snapshots for a study."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study identifier")
    snapshots: list[ClinicalSnapshot] = Field(
        default_factory=list, description="List of version-pinned snapshots"
    )
    total: int = Field(0, ge=0, description="Total snapshots found")


class SnapshotDataQueryRequest(BaseModel):
    """Request payload for querying observations inside a version-pinned snapshot pool."""

    model_config = ConfigDict(extra="forbid")

    filter_criteria: SnapshotFilterCriteria | None = Field(
        None, description="Optional slice and filter criteria"
    )
    request_unblinded: bool = Field(
        False, description="Whether caller requests unblinded access (requires role)"
    )


class SnapshotDataQueryResponse(BaseModel):
    """Response containing pooled canonical observations from a snapshot."""

    model_config = ConfigDict(extra="forbid")

    snapshot_id: str = Field(..., description="Snapshot identifier")
    sha256_digest: str = Field(
        ..., description="Cryptographic SHA-256 seal of snapshot pool"
    )
    total_records: int = Field(0, ge=0, description="Count of records returned")
    is_masked: bool = Field(
        False, description="True if any unblinded records were masked for blinded role"
    )
    masked_count: int = Field(0, ge=0, description="Count of unblinded records masked")
    records: list[CanonicalClinicalRecord] = Field(
        default_factory=list, description="Harmonized clinical records"
    )


class IngestResponse(BaseModel):
    """Response returned after ingesting heterogeneous stream records."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study identifier")
    stream_type: str = Field(..., description="Ingested stream type")
    records_ingested: int = Field(
        ..., ge=0, description="Number of canonical records created and persisted"
    )
    unblinded_records: int = Field(
        0, ge=0, description="Number of unblinded records identified"
    )
    record_ids: list[str] = Field(
        default_factory=list, description="List of generated canonical record UUIDs"
    )


class VerifySnapshotResponse(BaseModel):
    """Response for cryptographic SHA-256 integrity verification."""

    model_config = ConfigDict(extra="forbid")

    result: SnapshotVerificationResult = Field(
        ..., description="Integrity verification result"
    )


class SnapshotAuditTrailResponse(BaseModel):
    """Response containing 21 CFR Part 11 immutable audit trail entries for a snapshot."""

    model_config = ConfigDict(extra="forbid")

    snapshot_id: str = Field(..., description="Snapshot identifier")
    audit_entries: list[dict[str, Any]] = Field(
        default_factory=list, description="Audit trail entries"
    )
    total_entries: int = Field(0, ge=0, description="Total audit records")


class LakehouseMetricsResponse(BaseModel):
    """Response containing aggregated lakehouse metrics."""

    model_config = ConfigDict(extra="forbid")

    metrics: LakehouseMetrics = Field(
        ..., description="Real-time lakehouse repository metrics"
    )
