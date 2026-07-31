import uuid
from datetime import datetime, timezone
from typing import Optional

from sqlalchemy import (
    JSON,
    Boolean,
    DateTime,
    Float,
    Integer,
    String,
    TypeDecorator,
    func,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class AwareDateTime(TypeDecorator):
    """
    SQLAlchemy type that ensures all datetimes are timezone-aware and stored/retrieved in UTC.
    """

    impl = DateTime(timezone=True)
    cache_ok = True

    def process_bind_param(self, value, dialect):
        if value is not None:
            if isinstance(value, str):
                from dateutil.parser import parse

                value = parse(value)
            if isinstance(value, datetime):
                if value.tzinfo is None:
                    return value.replace(tzinfo=timezone.utc)
                return value.astimezone(timezone.utc)
        return value

    def process_result_value(self, value, dialect):
        if value is not None:
            if value.tzinfo is None:
                return value.replace(tzinfo=timezone.utc)
            return value.astimezone(timezone.utc)
        return value


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
        AwareDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class EtmfArchivalDelivery(Base):
    """
    Represents an append-only, idempotent eTMF archival delivery record for signed ICFs.
    Complies with FDA 21 CFR Part 11 auditing and tracking constraints.
    """

    __tablename__ = "etmf_archival_deliveries"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    status: Mapped[str] = mapped_column(String(50), default="PENDING", nullable=False)
    attempts: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    last_error: Mapped[Optional[str]] = mapped_column(String, nullable=True)
    next_retry_at: Mapped[Optional[datetime]] = mapped_column(
        AwareDateTime, nullable=True
    )
    completed_at: Mapped[Optional[datetime]] = mapped_column(
        AwareDateTime, nullable=True
    )
    retry_eligible: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    correlation_id: Mapped[str] = mapped_column(
        String(255), unique=True, index=True, nullable=False
    )
    template_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, nullable=False)
    subject_pseudonym: Mapped[str] = mapped_column(
        String(255), index=True, nullable=False
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)
    artifact_content: Mapped[str] = mapped_column(String, nullable=False)
    etmf_document_id: Mapped[Optional[str]] = mapped_column(String(36), nullable=True)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        AwareDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class SubjectConsent(Base):
    """
    Represents an append-only, immutable record of a subject's cryptographically signed consent.
    Complies with FDA 21 CFR Part 11 auditing and tracking constraints.
    """

    __tablename__ = "subject_consents"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    subject_pseudonym: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    template_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    version_index: Mapped[int] = mapped_column(Integer, nullable=False)
    protocol_version: Mapped[str] = mapped_column(String(255), nullable=False)
    source_content_identity: Mapped[str] = mapped_column(String, nullable=False)
    server_timestamp: Mapped[datetime] = mapped_column(
        AwareDateTime, default=func.now(), nullable=False
    )
    device_timestamp: Mapped[Optional[datetime]] = mapped_column(
        AwareDateTime, nullable=True
    )
    signature_manifest: Mapped[dict] = mapped_column(JSON, default=dict, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        AwareDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class ComprehensionCheck(Base):
    """
    Represents a set of comprehension questions, answers, and thresholds bound to a specific template version.
    Ensures that historical check configurations are preserved.
    """

    __tablename__ = "comprehension_checks"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    template_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    version_index: Mapped[int] = mapped_column(Integer, nullable=False)

    questions: Mapped[list[dict]] = mapped_column(JSON, default=list, nullable=False)
    expected_answers: Mapped[dict] = mapped_column(JSON, default=dict, nullable=False)
    threshold_policy: Mapped[dict] = mapped_column(JSON, default=dict, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        AwareDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class ComprehensionResult(Base):
    """
    Represents an append-only, immutable record of a subject's comprehension evaluation.
    Complies with FDA 21 CFR Part 11 auditing and tracking constraints.
    """

    __tablename__ = "comprehension_results"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    template_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    version_index: Mapped[int] = mapped_column(Integer, nullable=False)
    subject_pseudonym: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )

    # Snapshots/Definitions of check used during the check
    questions: Mapped[list[dict]] = mapped_column(JSON, default=list, nullable=False)
    expected_answers: Mapped[dict] = mapped_column(JSON, default=dict, nullable=False)
    threshold_policy: Mapped[dict] = mapped_column(JSON, default=dict, nullable=False)

    # Subject submission & evaluation
    submitted_answers: Mapped[dict] = mapped_column(JSON, default=dict, nullable=False)
    passed: Mapped[bool] = mapped_column(Boolean, nullable=False)
    score: Mapped[float] = mapped_column(Float, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        AwareDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class ConsentSignature(Base):
    """
    Represents a subject's electronic signature on a specific version of an eConsent template.
    Complies with FDA 21 CFR Part 11 auditing and tracking constraints.
    """

    __tablename__ = "consent_signatures"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    template_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    version_index: Mapped[int] = mapped_column(Integer, nullable=False)
    subject_pseudonym: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    signature_data: Mapped[Optional[str]] = mapped_column(String, nullable=True)
    signed_at: Mapped[datetime] = mapped_column(
        AwareDateTime, default=func.now(), nullable=False
    )

    # 21 CFR Part 11 Compliance Auditing Metadata
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
    category: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        AwareDateTime, default=func.now(), nullable=False
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
        AwareDateTime, default=func.now(), nullable=False
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
        AwareDateTime, default=func.now(), nullable=False, index=True
    )
    actor_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    actor_role: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False)
    document_id: Mapped[Optional[str]] = mapped_column(
        String(36), nullable=True, index=True
    )
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class ConsentTranslation(Base):
    """
    Represents an audited, human-reviewed, per-language consent translation
    tied to a specific source clause or template version.
    Complies with FDA 21 CFR Part 11 auditing and tracking constraints.
    """

    __tablename__ = "consent_translations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    translation_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)

    # Source metadata
    source_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    source_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # "clause" or "template"
    source_version_index: Mapped[int] = mapped_column(Integer, nullable=False)

    # Translation details
    language_code: Mapped[str] = mapped_column(String(10), nullable=False, index=True)
    translated_title: Mapped[str] = mapped_column(String(255), nullable=False)
    translated_text: Mapped[str] = mapped_column(String, nullable=False)

    # Review & approval workflow: DRAFT -> IN_REVIEW -> APPROVED
    status: Mapped[str] = mapped_column(String(50), default="DRAFT", nullable=False)

    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        AwareDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
