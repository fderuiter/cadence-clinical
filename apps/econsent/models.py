import uuid
from datetime import datetime
from typing import Optional

from sqlalchemy import JSON, Boolean, DateTime, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class ConsentDocument(Base):
    """
    Represents a site-scoped clinical Trial eConsent Document.
    Complies with FDA 21 CFR Part 11 auditing and tracking constraints.
    """

    __tablename__ = "consent_documents"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    document_name: Mapped[str] = mapped_column(String(255), nullable=False)
    content: Mapped[str] = mapped_column(String, nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class ConsentClause(Base):
    """
    Represents a versioned Informed Consent Form (ICF) clause scoped by study_id.
    Ensures that historical versions are preserved and never mutated.
    """

    __tablename__ = "consent_clauses"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    clause_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    text: Mapped[str] = mapped_column(String, nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class ConsentTemplate(Base):
    """
    Represents a versioned eConsent template/workflow scoped by study_id.
    Ensures that historical versions are preserved and never mutated.
    """

    __tablename__ = "consent_templates"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    template_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    template_name: Mapped[str] = mapped_column(String(255), nullable=False)
    protocol_version: Mapped[str] = mapped_column(String(255), nullable=False)
    is_published: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    requires_reconsent: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Ordered clause blocks and workflow steps stored as JSON
    clauses: Mapped[list[str]] = mapped_column(JSON, default=list, nullable=False)
    workflow_steps: Mapped[list[dict]] = mapped_column(
        JSON, default=list, nullable=False
    )

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class ConsentAuditLog(Base):
    """
    Represents an append-only, 21 CFR Part 11 compliant audit trail for eConsent operations.
    Captures actor metadata, action type, document references, change justifications, and timestamps.
    """

    __tablename__ = "consent_audit_logs"

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
