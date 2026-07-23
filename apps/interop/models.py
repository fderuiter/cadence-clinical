import uuid
from datetime import datetime
from typing import Any, Dict

from sqlalchemy import JSON, DateTime, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


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
