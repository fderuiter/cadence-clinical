import uuid
from datetime import datetime
from typing import Any, Dict, Optional

from sqlalchemy import JSON, DateTime, Integer, String, func
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class CTMSAuditLog(Base):
    """
    Represents an immutable, chronological record of all actions performed
    on the CTMS platform, in compliance with 21 CFR Part 11.
    """

    __tablename__ = "ctms_audit_logs"

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


class CTMSStudy(Base):
    """
    Example CTMS domain model representing a clinical trial metadata boundary
    with mandatory Part 11 audit fields.
    """

    __tablename__ = "ctms_studies"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="ACTIVE", nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


async def write_audit_log(
    session: AsyncSession,
    user_id: str,
    user_role: str,
    action: str,
    details: str,
) -> None:
    """
    Utility helper to write to the append-only CTMSAuditLog.
    """
    log_entry = CTMSAuditLog(
        user_id=user_id,
        user_role=user_role,
        action=action,
        details=details,
    )
    session.add(log_entry)
    await session.flush()
