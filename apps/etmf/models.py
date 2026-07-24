import uuid
from datetime import datetime
from enum import Enum
from typing import Any, Dict, List, Optional

from sqlalchemy import JSON, DateTime, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class DocumentStatus(str, Enum):
    DRAFT = "DRAFT"
    TECHNICAL_QC = "TECHNICAL_QC"
    CLINICAL_QC = "CLINICAL_QC"
    APPROVED = "APPROVED"
    ARCHIVED = "ARCHIVED"
    REJECTED = "REJECTED"


# Map defining allowed forward and rejection status transitions.
ALLOWED_TRANSITIONS = {
    DocumentStatus.DRAFT: [DocumentStatus.TECHNICAL_QC],
    DocumentStatus.TECHNICAL_QC: [DocumentStatus.CLINICAL_QC, DocumentStatus.REJECTED],
    DocumentStatus.CLINICAL_QC: [DocumentStatus.APPROVED, DocumentStatus.REJECTED],
    DocumentStatus.APPROVED: [DocumentStatus.ARCHIVED],
    DocumentStatus.REJECTED: [DocumentStatus.DRAFT],
    DocumentStatus.ARCHIVED: [],
}

# Map defining stage-to-required-role gates.
STAGE_REQUIRED_ROLES = {
    DocumentStatus.DRAFT: ["author", "data_manager", "sponsor_dm", "admin"],
    DocumentStatus.TECHNICAL_QC: ["technical_qc_reviewer", "technical_qc", "admin"],
    DocumentStatus.CLINICAL_QC: ["clinical_qc_reviewer", "clinical_qc", "admin"],
    DocumentStatus.APPROVED: ["approver", "admin"],
    DocumentStatus.ARCHIVED: ["approver", "admin"],
    DocumentStatus.REJECTED: [
        "technical_qc_reviewer",
        "clinical_qc_reviewer",
        "technical_qc",
        "clinical_qc",
        "admin",
    ],
}


def validate_qc_transition(from_status: str, to_status: str) -> bool:
    """
    Validates a requested transition against a document's current status.
    """
    try:
        from_enum = DocumentStatus(from_status)
        to_enum = DocumentStatus(to_status)
    except ValueError:
        return False
    return to_enum in ALLOWED_TRANSITIONS.get(from_enum, [])


def validate_qc_role(current_status: str, user_roles: List[str]) -> bool:
    """
    Enforces the stage to required-role gate.
    """
    try:
        current_enum = DocumentStatus(current_status)
    except ValueError:
        return False
    required = STAGE_REQUIRED_ROLES.get(current_enum, [])
    normalized_roles = [r.strip().lower() for r in user_roles]
    return any(role in required for role in normalized_roles)


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
    metadata_json: Mapped[Optional[Dict[str, Any]]] = mapped_column(JSON, nullable=True)

    # eTMF Document Status
    status: Mapped[str] = mapped_column(
        String(50), default="DRAFT", nullable=False
    )  # DRAFT, TECHNICAL_QC, CLINICAL_QC, APPROVED, ARCHIVED, REJECTED


class DocumentQCTransition(Base):
    """
    Represents an immutable, chronological record of Quality Control (QC) status transitions
    for eTMF documents, capturing standard metadata for 21 CFR Part 11 compliance.
    """

    __tablename__ = "tmf_document_qc_transitions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    document_id: Mapped[str] = mapped_column(String(36), nullable=False, index=True)
    from_status: Mapped[str] = mapped_column(String(50), nullable=False)
    to_status: Mapped[str] = mapped_column(String(50), nullable=False)
    user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    user_role: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(255), nullable=False)
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
