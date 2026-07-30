import enum
import uuid
from datetime import datetime
from typing import Any, Dict, Optional

from sqlalchemy import (
    DDL,
    JSON,
    CheckConstraint,
    DateTime,
    ForeignKey,
    Index,
    Integer,
    String,
    UniqueConstraint,
    event,
    func,
)
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


class TMFDocumentType:
    FORM_1572 = "FORM_1572"
    FINANCIAL_DISCLOSURE = "FINANCIAL_DISCLOSURE"
    PROTOCOL_SIGNOFF = "PROTOCOL_SIGNOFF"


class DocumentStatus(str, enum.Enum):
    DRAFT = "DRAFT"
    TECHNICAL_QC = "TECHNICAL_QC"
    CLINICAL_QC = "CLINICAL_QC"
    APPROVED = "APPROVED"
    ARCHIVED = "ARCHIVED"
    REJECTED = "REJECTED"
    SIGNED = "SIGNED"


def is_site_level_artifact(
    artifact_type: str, artifact_code: Optional[str] = None
) -> bool:
    """
    Determines if an eTMF artifact or code is expected at site-level (True) or study-level (False).
    Used to prevent silent scope inference and properly quarantine unassigned legacy records.
    """
    site_artifacts = {
        "fda form 1572",
        "financial disclosure",
        "investigator cv",
        "delegation of authority log",
        "site signature page",
        "site feasibility survey",
    }
    site_codes_prefix = {
        "05.02",
        "04.01",
        "05.01",
    }  # Zone 5 Investigator Qualification, Zone 4 regulatory

    art_lower = artifact_type.strip().lower()
    if art_lower in site_artifacts:
        return True
    if artifact_code:
        # Check if the code starts with any of our site prefixes
        for prefix in site_codes_prefix:
            if artifact_code.startswith(prefix):
                return True
    return False


class TMFDocument(Base):
    """
    Represents an archived document in the electronic Trial Master File (eTMF)
    structured on the DIA TMF Reference Model (Zones 1-11).

    The status field represents the current state of the document within the
    validated state machine of the eTMF QC review lifecycle (e.g. DRAFT, TECHNICAL_QC, etc.).
    """

    __tablename__ = "tmf_documents"

    __table_args__ = (
        CheckConstraint(
            "status IN ('DRAFT', 'TECHNICAL_QC', 'CLINICAL_QC', 'APPROVED', 'ARCHIVED', 'REJECTED', 'SIGNED')",
            name="chk_tmf_document_status",
        ),
    )

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
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
    taxonomy_version: Mapped[str] = mapped_column(
        String(50), default="v3.2.0", nullable=False
    )
    artifact_code: Mapped[str] = mapped_column(
        String(50), default="01.01.01", nullable=False, index=True
    )
    metadata_json: Mapped[Optional[Dict[str, Any]]] = mapped_column(JSON, nullable=True)

    # Signature and lifecycle fields
    document_type: Mapped[Optional[str]] = mapped_column(
        String(50), nullable=True, index=True
    )
    approval_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    signature_manifestation: Mapped[Optional[Dict[str, Any]]] = mapped_column(
        JSON, nullable=True
    )
    signer: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)
    signing_timestamp: Mapped[Optional[datetime]] = mapped_column(
        DateTime, nullable=True
    )

    # Redaction-related fields
    is_redacted: Mapped[bool] = mapped_column(default=False, nullable=False)
    redaction_source_id: Mapped[Optional[str]] = mapped_column(
        String(36), nullable=True, index=True
    )
    redaction_manifest_json: Mapped[Optional[Dict[str, Any]]] = mapped_column(
        JSON, nullable=True
    )


class DocumentQCTransition(Base):
    """
    Represents an append-only historical record of a document's QC state transitions,
    complying with 21 CFR Part 11 auditing requirements.
    """

    __tablename__ = "tmf_document_qc_transitions"

    __table_args__ = (
        UniqueConstraint(
            "document_id", "transition_sequence", name="uq_document_transition_sequence"
        ),
        Index(
            "ix_tmf_document_qc_transitions_doc_seq",
            "document_id",
            "transition_sequence",
        ),
    )

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    document_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("tmf_documents.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    transition_sequence: Mapped[int] = mapped_column(Integer, nullable=False)
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


# Trigger listener setup for SQLite immutability
trigger_update_sqlite = DDL("""
CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_update
BEFORE UPDATE ON tmf_document_qc_transitions
BEGIN
    SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be updated.');
END;
""")

trigger_delete_sqlite = DDL("""
CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_delete
BEFORE DELETE ON tmf_document_qc_transitions
BEGIN
    SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be deleted.');
END;
""")

trigger_func_pg = DDL("""
CREATE OR REPLACE FUNCTION block_qc_transition_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only.';
END;
$$ LANGUAGE plpgsql;
""")

trigger_update_pg = DDL("""
CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_update
BEFORE UPDATE ON tmf_document_qc_transitions
FOR EACH ROW EXECUTE FUNCTION block_qc_transition_mutation();
""")

trigger_delete_pg = DDL("""
CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_delete
BEFORE DELETE ON tmf_document_qc_transitions
FOR EACH ROW EXECUTE FUNCTION block_qc_transition_mutation();
""")

event.listen(
    DocumentQCTransition.__table__,
    "after_create",
    trigger_update_sqlite.execute_if(dialect="sqlite"),
)
event.listen(
    DocumentQCTransition.__table__,
    "after_create",
    trigger_delete_sqlite.execute_if(dialect="sqlite"),
)
event.listen(
    DocumentQCTransition.__table__,
    "after_create",
    trigger_func_pg.execute_if(dialect="postgresql"),
)
event.listen(
    DocumentQCTransition.__table__,
    "after_create",
    trigger_update_pg.execute_if(dialect="postgresql"),
)
event.listen(
    DocumentQCTransition.__table__,
    "after_create",
    trigger_delete_pg.execute_if(dialect="postgresql"),
)


# Model event listeners for SQLAlchemy session/mapper validation
@event.listens_for(DocumentQCTransition, "before_update")
def prevent_qc_transition_update(mapper, connection, target):
    raise RuntimeError(
        "IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be updated."
    )


@event.listens_for(DocumentQCTransition, "before_delete")
def prevent_qc_transition_delete(mapper, connection, target):
    raise RuntimeError(
        "IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be deleted."
    )
