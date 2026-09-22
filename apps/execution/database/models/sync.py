from datetime import datetime

from sqlalchemy import DateTime, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from .audit import Base


class SyncedBatchIdempotencyKey(Base):
    """Represents a unique client batch synchronization token for idempotency.

    Requirements: PRD-SYS-001
    """

    __tablename__ = "synced_batch_idempotency_keys"

    client_batch_id: Mapped[str] = mapped_column(String(255), primary_key=True)
    device_id: Mapped[str] = mapped_column(String(255), nullable=False)
    processed_count: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    processed_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())


class ProcessedOfflineBatch(Base):
    """Represents a processed offline batch record for idempotency tracking."""

    __tablename__ = "processed_offline_batches"

    client_batch_id: Mapped[str] = mapped_column(String(255), primary_key=True)
    device_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    synced_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())
