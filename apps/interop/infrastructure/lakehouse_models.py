"""SQLAlchemy ORM models for the Multi-Source Clinical Lakehouse and Statistical Computing Environment (SCE).

Maps:
- lakehouse_canonical_records: Harmonized clinical observations across all streams
- lakehouse_snapshots: Immutable, version-pinned snapshot pools sealed with SHA-256
- lakehouse_snapshot_records: M:N relational link between snapshots and records
- lakehouse_audit_logs: 21 CFR Part 11 immutable audit trail for lakehouse operations
- lakehouse_statistical_programs: Version-controlled validated R & Python scripts
- lakehouse_execution_runs: Ephemeral sandbox execution run records with signed manifests
- lakehouse_database_lock_tokens: Cryptographic authorization tokens gating unblinded statistical analysis

Requirements: PRD-CDR-001, PRD-CDR-004, PRD-CDR-005, PRD-CDR-006, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import (
    JSON,
    Boolean,
    DateTime,
    Float,
    ForeignKey,
    Integer,
    String,
    Text,
    func,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from apps.interop.infrastructure.models import Base


class LakehouseCanonicalRecord(Base):
    """Persisted harmonized observation record in the Lakehouse repository."""

    __tablename__ = "lakehouse_canonical_records"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    stream_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    domain: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    item_code: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    item_name: Mapped[str] = mapped_column(String(255), nullable=False)
    observation_datetime: Mapped[datetime] = mapped_column(
        DateTime, nullable=False, index=True
    )
    numeric_value: Mapped[float | None] = mapped_column(Float, nullable=True)
    text_value: Mapped[str | None] = mapped_column(String(2000), nullable=True)
    unit: Mapped[str | None] = mapped_column(String(50), nullable=True)
    reference_low: Mapped[float | None] = mapped_column(Float, nullable=True)
    reference_high: Mapped[float | None] = mapped_column(Float, nullable=True)
    abnormal_flag: Mapped[str | None] = mapped_column(String(50), nullable=True)
    is_unblinded: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False, index=True
    )
    raw_payload: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False)
    normalized_attributes: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False)
    data_hash: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class LakehouseSnapshot(Base):
    """Immutable, version-pinned clinical data snapshot pool linked to a lock milestone."""

    __tablename__ = "lakehouse_snapshots"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    version_tag: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    milestone: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    lock_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    record_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    stream_counts: Mapped[dict[str, int]] = mapped_column(
        JSON, default=dict, nullable=False
    )
    domain_counts: Mapped[dict[str, int]] = mapped_column(
        JSON, default=dict, nullable=False
    )
    sha256_digest: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_snapshot: Mapped[str] = mapped_column(String(1000), nullable=False)
    is_locked: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    blinded_record_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    unblinded_record_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    snapshot_manifest: Mapped[dict[str, Any]] = mapped_column(
        JSON, default=dict, nullable=False
    )

    records: Mapped[list[LakehouseSnapshotRecordLink]] = relationship(
        "LakehouseSnapshotRecordLink",
        back_populates="snapshot",
        cascade="all, delete-orphan",
    )


class LakehouseSnapshotRecordLink(Base):
    """Relational association mapping individual canonical records to a version-pinned snapshot pool."""

    __tablename__ = "lakehouse_snapshot_records"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    snapshot_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("lakehouse_snapshots.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    record_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("lakehouse_canonical_records.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    record_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    is_unblinded: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    snapshot: Mapped[LakehouseSnapshot] = relationship(
        "LakehouseSnapshot", back_populates="records"
    )
    record: Mapped[LakehouseCanonicalRecord] = relationship("LakehouseCanonicalRecord")


class LakehouseStatisticalProgram(Base):
    """Version-controlled, validated statistical program definition (R or Python)."""

    __tablename__ = "lakehouse_statistical_programs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    name: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(Text, default="", nullable=False)
    language: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    version: Mapped[str] = mapped_column(String(50), default="1.0.0", nullable=False)
    source_code: Mapped[str] = mapped_column(Text, nullable=False)
    code_hash: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    lockfile_content: Mapped[str | None] = mapped_column(Text, nullable=True)
    lock_type: Mapped[str] = mapped_column(
        String(50), default="UV_LOCK", nullable=False
    )
    lock_digest: Mapped[str] = mapped_column(String(64), nullable=False)
    target_domain: Mapped[str | None] = mapped_column(String(100), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class LakehouseExecutionRun(Base):
    """Persisted record of an analytical execution run within the isolated sandbox."""

    __tablename__ = "lakehouse_execution_runs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    program_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("lakehouse_statistical_programs.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    snapshot_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("lakehouse_snapshots.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    status: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    language: Mapped[str] = mapped_column(String(50), nullable=False)
    script_code_digest: Mapped[str] = mapped_column(String(64), nullable=False)
    snapshot_digest: Mapped[str] = mapped_column(String(64), nullable=False)
    lockfile_digest: Mapped[str] = mapped_column(String(64), nullable=False)
    container_image_digest: Mapped[str] = mapped_column(String(255), nullable=False)
    started_at: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    completed_at: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    duration_ms: Mapped[float] = mapped_column(Float, nullable=False)
    exit_code: Mapped[int] = mapped_column(Integer, nullable=False)
    stdout_capture: Mapped[str] = mapped_column(Text, default="", nullable=False)
    stderr_capture: Mapped[str] = mapped_column(Text, default="", nullable=False)
    is_unblinded_run: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False, index=True
    )
    database_lock_token_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    executed_by: Mapped[str] = mapped_column(String(255), nullable=False)
    executed_by_role: Mapped[str] = mapped_column(String(255), nullable=False)
    artifacts_manifest: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, default=list, nullable=False
    )
    output_digest: Mapped[str] = mapped_column(String(64), nullable=False)
    signed_manifest: Mapped[dict[str, Any]] = mapped_column(
        JSON, default=dict, nullable=False
    )
    manifest_sha256_seal: Mapped[str] = mapped_column(
        String(64), nullable=False, index=True
    )
    signature_block: Mapped[dict[str, Any]] = mapped_column(
        JSON, default=dict, nullable=False
    )


class LakehouseDatabaseLockToken(Base):
    """Cryptographic authorization token gating unblinded statistical analysis."""

    __tablename__ = "lakehouse_database_lock_tokens"

    id: Mapped[str] = mapped_column(String(100), primary_key=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    lock_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    milestone: Mapped[str] = mapped_column(String(100), nullable=False)
    authorized_roles: Mapped[list[str]] = mapped_column(
        JSON, default=list, nullable=False
    )
    issued_to: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    issued_at: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    expires_at: Mapped[datetime] = mapped_column(DateTime, nullable=False, index=True)
    is_revoked: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False, index=True
    )
    signature_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    reason: Mapped[str] = mapped_column(String(1000), nullable=False)


class LakehouseAuditLog(Base):
    """Immutable, chronological 21 CFR Part 11 audit log of all Lakehouse & SCE operations."""

    __tablename__ = "lakehouse_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_role: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    snapshot_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    details: Mapped[dict[str, Any]] = mapped_column(JSON, default=dict, nullable=False)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    sha256_seal: Mapped[str] = mapped_column(String(64), nullable=False)
