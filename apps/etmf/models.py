import uuid
from datetime import datetime
from typing import Any, Dict, Optional

from sqlalchemy import JSON, DateTime, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class ExpectedDocument(Base):
    """
    Represents an Expected Document List (EDL) rule that specifies required
    artifact types for a given study/site and milestone.
    """

    __tablename__ = "tmf_expected_documents"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    milestone: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    artifact_type: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    zone: Mapped[Optional[int]] = mapped_column(Integer, nullable=True)
    section: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)
    metadata_json: Mapped[Optional[Dict[str, Any]]] = mapped_column(JSON, nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class DocumentStatus:
    DRAFT = "DRAFT"
    TECHNICAL_QC = "TECHNICAL_QC"
    CLINICAL_QC = "CLINICAL_QC"
    APPROVED = "APPROVED"
    ARCHIVED = "ARCHIVED"
    REJECTED = "REJECTED"


class TMFDocument(Base):
    """
    Represents an archived document in the electronic Trial Master File (eTMF)
    structured on the DIA TMF Reference Model (Zones 1-11).
    """

    __tablename__ = "tmf_documents"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    zone: Mapped[int] = mapped_column(Integer, nullable=False, index=True)
    section: Mapped[str] = mapped_column(String(255), nullable=False)
    artifact_type: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    filename: Mapped[str] = mapped_column(String(255), nullable=False)
    content: Mapped[str] = mapped_column(String, nullable=False)
    mime_type: Mapped[str] = mapped_column(String(100), nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="DRAFT", nullable=False)
    metadata_json: Mapped[Optional[Dict[str, Any]]] = mapped_column(JSON, nullable=True)


class DocumentQCTransition(Base):
    """
    Represents an append-only historical record of a document's QC state transitions,
    complying with 21 CFR Part 11 auditing requirements.
    """

    __tablename__ = "tmf_document_qc_transitions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    document_id: Mapped[str] = mapped_column(String(36), nullable=False, index=True)
    from_status: Mapped[str] = mapped_column(String(50), nullable=False)
    to_status: Mapped[str] = mapped_column(String(50), nullable=False)
    actor_id: Mapped[str] = mapped_column(String(255), nullable=False)
    actor_role: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )


class TMFAuditLog(Base):
    """
    Represents an immutable, chronological record of all document views,
    downloads, and administrative actions performed on the eTMF repository.
    """

    __tablename__ = "tmf_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_role: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False)
    document_id: Mapped[Optional[str]] = mapped_column(
        String(36), nullable=True, index=True
    )
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    cryptographic_seal: Mapped[Optional[str]] = mapped_column(String(64), nullable=True)


class TMFAuditLedgerSeal(Base):
    """
    Represents a cryptographic block seal for the eTMF audit logs.
    """

    __tablename__ = "tmf_audit_ledger_seals"

    block_index: Mapped[int] = mapped_column(
        Integer, primary_key=True, autoincrement=True
    )
    previous_block_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    current_block_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    sealed_record_count: Mapped[int] = mapped_column(Integer, nullable=False)
    merkle_root_hash: Mapped[str] = mapped_column(String(64), nullable=False)
