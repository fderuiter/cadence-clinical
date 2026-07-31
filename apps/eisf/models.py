import uuid
from datetime import date, datetime, timezone
from typing import Any, Dict, Optional

from sqlalchemy import JSON, Date, DateTime, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, synonym
from sqlmodel import Field, SQLModel


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

    # Synonyms and aliases for site_id and binder classification organizer structure
    site_uuid = synonym("site_id")
    binder_section = synonym("binder_classification")
    artifact_type = synonym("binder_classification")


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

    # Synonyms to mirror TMFAuditLog exactly for Part 11 and user-scoping traceability
    user_id = synonym("actor_id")
    user_role = synonym("actor_role")


class EISFSectionTaxonomy(SQLModel, table=True):
    __tablename__ = "eisf_section_taxonomies"

    section_code: str = Field(primary_key=True)
    section_number: str = Field(index=True)
    title: str
    description: str
    is_mandatory: bool = Field(default=True)


class EISFDocumentRecord(SQLModel, table=True):
    __tablename__ = "eisf_document_records"

    id: str = Field(primary_key=True)
    site_id: str = Field(index=True)
    study_id: str = Field(index=True)
    section_code: str = Field(
        index=True, foreign_key="eisf_section_taxonomies.section_code"
    )
    filename: str
    file_path: str
    sha256_checksum: str
    version_major: int = Field(default=1)
    version_minor: int = Field(default=0)
    status: str = Field(default="DRAFT")
    expiration_date: Optional[date] = None

    # GxP Audit fields
    created_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
    created_by: str
    reason_for_change: str = "Initial Document Ingestion"
    version_index: int = Field(default=1)
    is_active: bool = Field(default=True)
    is_deleted: bool = Field(default=False)


STANDARD_EISF_SECTIONS = [
    {
        "section_code": "01.01",
        "section_number": "01.01",
        "title": "Investigator Curriculum Vitae (CV) & Medical Licenses",
        "description": "Curriculum Vitae and medical licenses for investigators.",
        "is_mandatory": True,
    },
    {
        "section_code": "02.01",
        "section_number": "02.01",
        "title": "IRB / IEC Approvals & Roster",
        "description": "Institutional Review Board or Independent Ethics Committee approvals and committee rosters.",
        "is_mandatory": True,
    },
    {
        "section_code": "03.01",
        "section_number": "03.01",
        "title": "Protocol Signature Pages & Amendments",
        "description": "Signed clinical study protocol signature pages and amendments.",
        "is_mandatory": True,
    },
    {
        "section_code": "04.01",
        "section_number": "04.01",
        "title": "Delegation of Authority (DOA) Log",
        "description": "Delegation of Authority Log documenting study team roles.",
        "is_mandatory": True,
    },
    {
        "section_code": "05.01",
        "section_number": "05.01",
        "title": "Local Laboratory Accreditations & Normal Ranges",
        "description": "Accreditation certificates, certifications, and normal reference ranges.",
        "is_mandatory": True,
    },
    {
        "section_code": "06.01",
        "section_number": "06.01",
        "title": "Investigational Product (IP) Shipping & Accountability Records",
        "description": "IP shipping, receipt, inventory, and accountability documentation.",
        "is_mandatory": True,
    },
    {
        "section_code": "07.01",
        "section_number": "07.01",
        "title": "Sample Handling & Biospecimen Logs",
        "description": "Logs tracking biospecimens, lab kit receipts, and shipping.",
        "is_mandatory": True,
    },
    {
        "section_code": "08.01",
        "section_number": "08.01",
        "title": "Monitoring Visit Reports & Correspondence",
        "description": "Site monitoring visit reports, confirmation letters, and relevant study correspondence.",
        "is_mandatory": True,
    },
]
