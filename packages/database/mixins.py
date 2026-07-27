from datetime import datetime

from sqlalchemy import DateTime, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column


class AuditMixin:
    """
    SQLAlchemy 2.0 mixin providing standard GxP audit columns
    for clinical database models.
    """

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(
        String(1000), default="system_operation", nullable=False
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


SharedAuditMixin = AuditMixin
