import enum
import uuid
from datetime import date, datetime
from typing import Any

from sqlalchemy import (
    DDL,
    JSON,
    CheckConstraint,
    Date,
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

from packages.database import IntegrationOutboxMixin


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
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    milestone: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    artifact_type: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    zone: Mapped[int | None] = mapped_column(Integer, nullable=True)
    section: Mapped[str | None] = mapped_column(String(255), nullable=True)
    metadata_json: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)

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


class DocumentStatus(enum.StrEnum):
    DRAFT = "DRAFT"
    TECHNICAL_QC = "TECHNICAL_QC"
    CLINICAL_QC = "CLINICAL_QC"
    APPROVED = "APPROVED"
    ARCHIVED = "ARCHIVED"
    REJECTED = "REJECTED"
    SIGNED = "SIGNED"


def is_site_level_artifact(
    artifact_type: str, artifact_code: str | None = None
) -> bool:
    from apps.etmf.models import is_site_level_artifact as _impl

    return _impl(artifact_type, artifact_code)


class TMFDocument(Base):
    """
    Represents an archived document in the electronic Trial Master File (eTMF)
    structured on the DIA TMF Reference Model (Zones 1-11).
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
    idempotency_key: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    zone: Mapped[int] = mapped_column(Integer, nullable=False, index=True)
    section: Mapped[str] = mapped_column(String(255), nullable=False)
    artifact_type: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    filename: Mapped[str] = mapped_column(String(255), nullable=False)
    _content: Mapped[str] = mapped_column("content", String, nullable=False)
    mime_type: Mapped[str] = mapped_column(String(100), nullable=False)

    @property
    def content(self) -> str:
        if not self._content:
            return self._content

        mime_lower = self.mime_type.lower().strip() if self.mime_type else ""
        is_binary = (
            "pdf" in mime_lower
            or "wordprocessingml" in mime_lower
            or "docx" in mime_lower
            or mime_lower == "application/octet-stream"
        )
        if is_binary:
            import base64

            try:
                decoded_bytes = base64.b64decode(self._content)
                return decoded_bytes.decode("utf-8")
            except Exception:
                return self._content
        return self._content

    @content.setter
    def content(self, value: str) -> None:
        self._content = value

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
    metadata_json: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)

    issue_date: Mapped[date | None] = mapped_column(Date, nullable=True, index=True)
    expiration_date: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True, index=True
    )
    document_owner_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )

    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    protocol_version_tag: Mapped[str | None] = mapped_column(String(50), nullable=True)
    protocol_version_index: Mapped[int | None] = mapped_column(Integer, nullable=True)
    protocol_version_status: Mapped[str | None] = mapped_column(
        String(50), nullable=True
    )

    document_type: Mapped[str | None] = mapped_column(
        String(50), nullable=True, index=True
    )
    approval_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    signature_manifestation: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )
    signer: Mapped[str | None] = mapped_column(String(255), nullable=True)
    signing_timestamp: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    is_redacted: Mapped[bool] = mapped_column(default=False, nullable=False)
    redaction_source_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
    )
    redaction_manifest_json: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )

    correlation_key: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    content_checksum: Mapped[str | None] = mapped_column(String(64), nullable=True)
    source_system: Mapped[str | None] = mapped_column(String(255), nullable=True)
    sync_status: Mapped[str | None] = mapped_column(String(50), nullable=True)


class DocumentQCTransition(Base):
    """
    Represents an append-only historical record of a document's QC state transitions.
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
    document_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
    )
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    cryptographic_seal: Mapped[str | None] = mapped_column(String(64), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)


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


class DocumentExpirationAlertState(Base):
    """
    Tracks persistent warning/expiration alerts generated for eTMF documents to avoid duplication.
    """

    __tablename__ = "tmf_document_expiration_alert_states"

    __table_args__ = (
        UniqueConstraint(
            "document_id", "warning_window", name="uq_tmf_doc_expiration_alert_state"
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
    warning_window: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    alerted_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )

    dispatched: Mapped[bool] = mapped_column(default=False, nullable=False, index=True)
    notification_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    attempts: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    last_error: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


trigger_update_sqlite = DDL("""
CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_update
BEFORE UPDATE ON tmf_document_qc_transitions
BEGIN
    SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be updated.');
END;""")

trigger_delete_sqlite = DDL("""
CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_delete
BEFORE DELETE ON tmf_document_qc_transitions
BEGIN
    SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be deleted.');
END;""")

trigger_func_pg = DDL("""
CREATE OR REPLACE FUNCTION block_qc_transition_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only.';
END;
$$ LANGUAGE plpgsql;""")

trigger_update_pg_drop = DDL("""
DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_update ON tmf_document_qc_transitions;
""")

trigger_update_pg_create = DDL("""
CREATE TRIGGER tmf_document_qc_transitions_no_update
BEFORE UPDATE ON tmf_document_qc_transitions
FOR EACH ROW EXECUTE FUNCTION block_qc_transition_mutation();""")

trigger_delete_pg_drop = DDL("""
DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_delete ON tmf_document_qc_transitions;
""")

trigger_delete_pg_create = DDL("""
CREATE TRIGGER tmf_document_qc_transitions_no_delete
BEFORE DELETE ON tmf_document_qc_transitions
FOR EACH ROW EXECUTE FUNCTION block_qc_transition_mutation();""")

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
    trigger_update_pg_drop.execute_if(dialect="postgresql"),
)
event.listen(
    DocumentQCTransition.__table__,
    "after_create",
    trigger_update_pg_create.execute_if(dialect="postgresql"),
)
event.listen(
    DocumentQCTransition.__table__,
    "after_create",
    trigger_delete_pg_drop.execute_if(dialect="postgresql"),
)
event.listen(
    DocumentQCTransition.__table__,
    "after_create",
    trigger_delete_pg_create.execute_if(dialect="postgresql"),
)


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


trigger_document_delete_sqlite = DDL("""
CREATE TRIGGER IF NOT EXISTS tmf_documents_no_delete
BEFORE DELETE ON tmf_documents
BEGIN
    SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: eTMF documents are immutable and cannot be deleted.');
END;""")

trigger_document_func_pg = DDL("""
CREATE OR REPLACE FUNCTION block_document_deletion()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'IMMUTABILITY_VIOLATION: eTMF documents are immutable and cannot be deleted.';
END;
$$ LANGUAGE plpgsql;""")

trigger_document_delete_pg_drop = DDL("""
DROP TRIGGER IF EXISTS tmf_documents_no_delete ON tmf_documents;
""")

trigger_document_delete_pg_create = DDL("""
CREATE TRIGGER tmf_documents_no_delete
BEFORE DELETE ON tmf_documents
FOR EACH ROW EXECUTE FUNCTION block_document_deletion();""")

event.listen(
    TMFDocument.__table__,
    "after_create",
    trigger_document_delete_sqlite.execute_if(dialect="sqlite"),
)
event.listen(
    TMFDocument.__table__,
    "after_create",
    trigger_document_func_pg.execute_if(dialect="postgresql"),
)
event.listen(
    TMFDocument.__table__,
    "after_create",
    trigger_document_delete_pg_drop.execute_if(dialect="postgresql"),
)
event.listen(
    TMFDocument.__table__,
    "after_create",
    trigger_document_delete_pg_create.execute_if(dialect="postgresql"),
)


@event.listens_for(TMFDocument, "before_delete")
def prevent_document_delete(mapper, connection, target):
    raise RuntimeError(
        "IMMUTABILITY_VIOLATION: eTMF documents are immutable and cannot be deleted."
    )


class IntegrationOutbox(Base, IntegrationOutboxMixin):
    """Concrete integration outbox table for the eTMF service."""

    __tablename__ = "integration_outbox"
