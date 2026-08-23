"""SQLAlchemy ORM models for Study Protocol Training Curricula and Qualification Gating.

Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
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

from apps.knowledge.infrastructure.models import Base


class TrainingCurriculum(Base):
    """Represents a training curriculum for a study protocol version or SOP revision.

    Requirements: PRD-TRN-001
    """

    __tablename__ = "training_curricula"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    protocol_version: Mapped[str] = mapped_column(
        String(50), nullable=False, index=True
    )
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(Text, nullable=False)
    target_roles: Mapped[list[str]] = mapped_column(JSON, nullable=False, default=list)
    is_mandatory: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    passing_score: Mapped[float] = mapped_column(Float, default=0.80, nullable=False)
    min_reading_time_seconds: Mapped[int] = mapped_column(
        Integer, default=60, nullable=False
    )
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    metadata_json: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)

    # GxP audit fields (21 CFR Part 11)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    modules: Mapped[list[TrainingModule]] = relationship(
        "TrainingModule",
        back_populates="curriculum",
        cascade="all, delete-orphan",
        order_by="TrainingModule.order_index",
    )
    assignments: Mapped[list[TrainingAssignment]] = relationship(
        "TrainingAssignment",
        back_populates="curriculum",
        cascade="all, delete-orphan",
    )


class TrainingModule(Base):
    """Represents a discrete learning module or delta-procedure unit inside a curriculum.

    Requirements: PRD-TRN-001, PRD-TRN-003
    """

    __tablename__ = "training_modules"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    curriculum_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("training_curricula.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    module_type: Mapped[str] = mapped_column(
        String(50), default="PROCEDURE_DELTA", nullable=False
    )
    content_text: Mapped[str] = mapped_column(Text, nullable=False)
    video_url: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    document_reference: Mapped[str | None] = mapped_column(String(500), nullable=True)
    order_index: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    quiz_questions: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, nullable=False, default=list
    )
    min_reading_time_seconds: Mapped[int] = mapped_column(
        Integer, default=30, nullable=False
    )

    # GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    curriculum: Mapped[TrainingCurriculum] = relationship(
        "TrainingCurriculum", back_populates="modules"
    )


class TrainingAssignment(Base):
    """Represents a training assignment to a specific user for a study and site.

    Requirements: PRD-TRN-001, PRD-TRN-002
    """

    __tablename__ = "training_assignments"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    curriculum_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("training_curricula.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_name: Mapped[str] = mapped_column(String(255), nullable=False)
    user_role: Mapped[str] = mapped_column(String(100), nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False, index=True
    )
    time_spent_seconds: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    quiz_score: Mapped[float | None] = mapped_column(Float, nullable=True)
    attempts_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    completed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    certificate_id: Mapped[str | None] = mapped_column(String(36), nullable=True)

    # GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    curriculum: Mapped[TrainingCurriculum] = relationship(
        "TrainingCurriculum", back_populates="assignments"
    )


class TrainingCompletionCertificate(Base):
    """Represents a verified completion certificate with 21 CFR Part 11 electronic signature and SHA-256 seal.

    Requirements: PRD-TRN-003, PRD-TRN-004
    """

    __tablename__ = "training_completion_certificates"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    assignment_id: Mapped[str] = mapped_column(String(36), nullable=False, index=True)
    curriculum_id: Mapped[str] = mapped_column(String(36), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_name: Mapped[str] = mapped_column(String(255), nullable=False)
    user_role: Mapped[str] = mapped_column(String(100), nullable=False)
    protocol_version: Mapped[str] = mapped_column(String(50), nullable=False)
    score_achieved: Mapped[float] = mapped_column(Float, nullable=False)
    time_spent_seconds: Mapped[int] = mapped_column(Integer, nullable=False)
    signature_manifestation: Mapped[dict[str, Any]] = mapped_column(
        JSON, nullable=False
    )
    sha256_seal: Mapped[str] = mapped_column(String(64), nullable=False)
    pdf_base64: Mapped[str | None] = mapped_column(Text, nullable=True)
    etmf_doc_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    eisf_doc_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    routed_to_etmf: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    routed_to_eisf: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class TrainingAuditLog(Base):
    """Immutable audit trail for all training operations to satisfy 21 CFR Part 11.

    Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
    """

    __tablename__ = "training_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_role: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False)
    details: Mapped[str] = mapped_column(String(2000), nullable=False)
    study_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
