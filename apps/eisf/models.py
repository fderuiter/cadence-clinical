import uuid
from datetime import date, datetime
from typing import Any, Dict, Optional

from sqlalchemy import JSON, Date, DateTime, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class ISFDocument(Base):
    """
    Represents a site-scoped, binder-classified document stored in the eISF (electronic Investigator Site File).
    Supports versioning, metadata, and future synchronization fields.
    """

    __tablename__ = "isf_documents"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    binder_classification: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    filename: Mapped[str] = mapped_column(String(255), nullable=False)
    content: Mapped[str] = mapped_column(String, nullable=False)
    mime_type: Mapped[str] = mapped_column(String(100), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Expiration metadata fields
    issue_date: Mapped[Optional[date]] = mapped_column(Date, nullable=True, index=True)
    expiration_date: Mapped[Optional[date]] = mapped_column(
        Date, nullable=True, index=True
    )
    document_owner_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )

    # Creator and Timestamps
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)

    # Document metadata
    metadata_json: Mapped[Optional[Dict[str, Any]]] = mapped_column(JSON, nullable=True)

    # Future sync identity fields
    correlation_key: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    content_checksum: Mapped[Optional[str]] = mapped_column(String(64), nullable=True)
    sync_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    source_system: Mapped[str] = mapped_column(
        String(100), default="eISF", nullable=False
    )


class ISFAuditLog(Base):
    """
    Represents an append-only 21 CFR Part 11 compliant audit trail for eISF documents.
    Captures actor details, actions, document references, change reasons, and audit timestamps.
    """

    __tablename__ = "isf_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    actor_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    actor_role: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False)
    document_id: Mapped[Optional[str]] = mapped_column(
        String(36), nullable=True, index=True
    )
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
