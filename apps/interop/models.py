# @req:PRD-ECOA-001 - Instrument and subject-assignment persistence models
# This file defines the eCOA content and scheduling data models used to author questionnaires/diaries and assign them to subjects.
# Adheres to FDA 21 CFR Part 11 auditing requirements (created_at, created_by, reason_for_change, version_index).

import uuid
from datetime import datetime
from typing import Any, Dict, Optional

from sqlalchemy import JSON, Boolean, DateTime, ForeignKey, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship


class Base(DeclarativeBase):
    pass


class EPROSubmission(Base):
    """
    Represents an ePRO (electronic Patient-Reported Outcome) or eCOA (electronic
    Clinical Outcome Assessment) participant diary or survey submission.

    Includes client-side device timestamps, offline sync queue markers, and
    resolved/reconciled state answers.
    """

    __tablename__ = "epro_submissions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    diary_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    device_timestamp: Mapped[datetime] = mapped_column(
        DateTime, nullable=False, index=True
    )
    server_timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    answers: Mapped[Dict[str, Any]] = mapped_column(JSON, nullable=False)
    offline_sync_markers: Mapped[Dict[str, Any]] = mapped_column(JSON, nullable=False)
    sync_status: Mapped[str] = mapped_column(
        String(50), default="RESOLVED", nullable=False
    )  # RESOLVED, CONFLICT, IGNORED
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class InteropAuditLog(Base):
    """
    Represents an immutable, chronological audit trail log of all FHIR / eSource
    and ePRO mobile sync gateway actions.
    """

    __tablename__ = "interop_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_role: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False)
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    change_reason: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)


class Instrument(Base):
    """
    Represents an eCOA instrument/diary definition for surveys or questionnaires.
    Defines items (questions), response types/options, and scoring metadata.
    Includes 21 CFR Part 11 compliant audit and row-versioning fields.
    """

    __tablename__ = "instruments"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[Optional[str]] = mapped_column(String(1000), nullable=True)
    items: Mapped[Dict[str, Any]] = mapped_column(JSON, nullable=False)
    response_types: Mapped[Dict[str, Any]] = mapped_column(JSON, nullable=False)
    scoring_metadata: Mapped[Dict[str, Any]] = mapped_column(JSON, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    assignments: Mapped[list["SubjectAssignment"]] = relationship(
        "SubjectAssignment", back_populates="instrument", cascade="all, delete-orphan"
    )


class SubjectAssignment(Base):
    """
    Links a subject to an Instrument with a scheduled due/recurrence window.
    Complies with GxP/Part 11 audit requirements.
    """

    __tablename__ = "subject_assignments"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    instrument_id: Mapped[str] = mapped_column(
        String(36), ForeignKey("instruments.id"), nullable=False, index=True
    )

    # Schedulable due-window / recurrence data
    start_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    end_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    recurrence_pattern: Mapped[Optional[str]] = mapped_column(
        String(50), nullable=True
    )  # e.g. "DAILY", "WEEKLY"
    due_at: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    instrument: Mapped["Instrument"] = relationship(
        "Instrument", back_populates="assignments"
    )


class SubjectNotification(Base):
    """
    Represents an auditable, subject-scoped reminder or notification record.
    Tracks related assignment, due time, channel, delivery status, and read status.
    """

    __tablename__ = "subject_notifications"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    assignment_id: Mapped[Optional[str]] = mapped_column(
        String(36),
        ForeignKey("subject_assignments.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )
    due_at: Mapped[datetime] = mapped_column(DateTime, nullable=False, index=True)
    channel: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # "EMAIL", "SMS", "WEBHOOK", "IN_APP"
    delivery_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )  # "PENDING", "SENT", "FAILED"
    is_read: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    read_at: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    assignment: Mapped[Optional["SubjectAssignment"]] = relationship(
        "SubjectAssignment"
    )


class EPROSubmissionDefeated(Base):
    """
    Represents a preserved/durable copy of a defeated, shadow, or overwritten ePRO submission payload
    resulting from conflict resolution, online merge, or structural sync conflicts.
    """

    __tablename__ = "epro_defeated_submissions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    diary_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    device_timestamp: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    answers: Mapped[Dict[str, Any]] = mapped_column(JSON, nullable=False)
    winning_answers: Mapped[Optional[Dict[str, Any]]] = mapped_column(JSON, nullable=True)
    offline_sync_markers: Mapped[Dict[str, Any]] = mapped_column(JSON, nullable=False)
    status: Mapped[str] = mapped_column(
        String(100),
        default="Defeated by online-merge conflict resolution",
        nullable=False,
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class ClinicalQuery(Base):
    """
    Represents a clinical query state record for GxP data discrepancy tracking in the interop/sync gateway.
    """

    __tablename__ = "clinical_queries"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    visit_id: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)
    domain: Mapped[Optional[str]] = mapped_column(String(50), nullable=True)
    test_code: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    status: Mapped[str] = mapped_column(String(50), default="OPEN", nullable=False)
    explanation: Mapped[str] = mapped_column(String(1000), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
