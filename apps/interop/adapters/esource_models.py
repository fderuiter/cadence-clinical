"""SQLAlchemy ORM models for SMART on FHIR eSource Ingestion Pipeline and CRC Reconciliation.

Maps:
- ehr_endpoints: Registered hospital EHR endpoints with SMART backend authorization configs
- ehr_reconciliation_candidates: Candidate EHR source data items for non-destructive CRC review

Requirements: PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import JSON, Boolean, DateTime, Float, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from apps.interop.adapters.models import Base


class EhrEndpointRecord(Base):
    """Persisted hospital EHR endpoint with SMART Backend Services configuration."""

    __tablename__ = "ehr_endpoints"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    system_type: Mapped[str] = mapped_column(
        String(50), default="SMART_FHIR_GENERIC", nullable=False, index=True
    )
    fhir_base_url: Mapped[str] = mapped_column(String(500), nullable=False)
    token_url: Mapped[str] = mapped_column(String(500), nullable=False)
    client_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    public_key_jwks_url: Mapped[str | None] = mapped_column(String(500), nullable=True)
    scopes: Mapped[list[str]] = mapped_column(JSON, nullable=False)
    polling_interval_sec: Mapped[int] = mapped_column(
        Integer, default=300, nullable=False
    )
    is_active: Mapped[bool] = mapped_column(
        Boolean, default=True, nullable=False, index=True
    )
    last_polled_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    reason_for_change: Mapped[str] = mapped_column(
        String(1000), default="Hospital EHR endpoint configuration", nullable=False
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class EhrReconciliationCandidateRecord(Base):
    """Persisted candidate eSource data record extracted from EHR awaiting CRC reconciliation.

    Maintains complete source data provenance and audit attribution under 21 CFR Part 11.
    """

    __tablename__ = "ehr_reconciliation_candidates"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_pseudonym: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    ehr_system_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    fhir_resource_type: Mapped[str] = mapped_column(
        String(100), nullable=False, index=True
    )
    fhir_resource_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    source_timestamp: Mapped[datetime] = mapped_column(
        DateTime, nullable=False, index=True
    )
    target_domain: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    target_variable: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    cdash_testcd: Mapped[str | None] = mapped_column(String(100), nullable=True)
    cdash_test: Mapped[str | None] = mapped_column(String(255), nullable=True)
    candidate_value: Mapped[Any] = mapped_column(JSON, nullable=False)
    candidate_unit: Mapped[str | None] = mapped_column(String(50), nullable=True)
    current_ecrf_value: Mapped[Any | None] = mapped_column(JSON, nullable=True)
    current_ecrf_updated_at: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    discrepancy_type: Mapped[str] = mapped_column(
        String(50), default="NEW_FIELD", nullable=False
    )
    confidence_score: Mapped[float] = mapped_column(Float, default=1.0, nullable=False)
    mapping_provenance: Mapped[str] = mapped_column(String(500), nullable=False)
    raw_fhir_resource: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING_REVIEW", nullable=False, index=True
    )
    resolution_action: Mapped[str | None] = mapped_column(String(50), nullable=True)
    resolution_reason: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    resolved_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    resolved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    electronic_signature: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )
    clinical_query_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
    )

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="esource_ingestion_pipeline", nullable=False
    )
    reason_for_change: Mapped[str] = mapped_column(
        String(1000),
        default="Automated SMART on FHIR eSource Ingestion",
        nullable=False,
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
