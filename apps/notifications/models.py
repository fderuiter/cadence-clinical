import uuid
from datetime import datetime
from enum import Enum
from typing import Optional

from sqlalchemy import Boolean, DateTime, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class NotificationCategory(str, Enum):
    ALERTS = "ALERTS"
    SYSTEM = "SYSTEM"
    ACTION_ITEMS = "ACTION_ITEMS"


class NotificationPriority(str, Enum):
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class NotificationStatus(str, Enum):
    OPEN = "OPEN"
    ACKNOWLEDGED = "ACKNOWLEDGED"
    RESOLVED = "RESOLVED"


class Notification(Base):
    """
    Represents a notification target, priority, category, message content,
    delivery state, and associated 21 CFR Part 11 compliant audit fields.
    """

    __tablename__ = "notification_records"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    recipient_user_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    recipient_role: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    category: Mapped[NotificationCategory] = mapped_column(String(50), nullable=False)
    priority: Mapped[NotificationPriority] = mapped_column(String(50), nullable=False)
    channels: Mapped[str] = mapped_column(String(255), default="IN_APP")
    message_content: Mapped[str] = mapped_column(String, nullable=False)
    related_entity_id: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)
    related_entity_type: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True
    )
    status: Mapped[NotificationStatus] = mapped_column(
        String(50), default=NotificationStatus.OPEN, nullable=False
    )
    delivery_state: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    retries: Mapped[int] = mapped_column(Integer, default=0, nullable=False)

    # 21 CFR Part 11 audit fields for tracking mutability/creation
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class NotificationAuditLog(Base):
    """
    Represents an immutable, chronological append-only audit ledger of actions performed on Notification records.
    """

    __tablename__ = "notification_audit_logs"

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


class NotificationDelivery(Base):
    """
    Represents the delivery status of a specific channel for a given notification.
    """

    __tablename__ = "notification_deliveries"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    notification_id: Mapped[str] = mapped_column(String(36), nullable=False, index=True)
    channel: Mapped[str] = mapped_column(String(50), nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="PENDING", nullable=False)
    attempts: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    last_error: Mapped[Optional[str]] = mapped_column(String(1000), nullable=True)
    next_retry_at: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    completed_at: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    retry_eligible: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
