"""SQLAlchemy ORM models for the Fileshare microservice.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from __future__ import annotations

import uuid
from datetime import datetime

from sqlalchemy import Boolean, DateTime, ForeignKey, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship

from apps.fileshare.domain.models import (
    FileRecord,
    GuestLink,
    PermissionLevel,
    ShareGrant,
    ShareScope,
)


class Base(DeclarativeBase):
    """Declarative Base for fileshare microservice tables."""

    pass


class FileRecordModel(Base):
    """Database table representing stored file records and their object storage keys."""

    __tablename__ = "file_records"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    filename: Mapped[str] = mapped_column(String(500), nullable=False)
    mime_type: Mapped[str] = mapped_column(String(255), nullable=False)
    size_bytes: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    object_key: Mapped[str] = mapped_column(
        String(1000), nullable=False, unique=True, index=True
    )
    checksum_sha256: Mapped[str | None] = mapped_column(
        String(64), nullable=True, index=True
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    uploaded_by: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    uploaded_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    is_on_hold: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_deleted: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # 21 CFR Part 11 GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    # Relationships
    share_grants: Mapped[list[ShareGrantModel]] = relationship(
        back_populates="file_record", cascade="all, delete-orphan"
    )
    guest_links: Mapped[list[GuestLinkModel]] = relationship(
        back_populates="file_record", cascade="all, delete-orphan"
    )

    def to_domain(self) -> FileRecord:
        """Converts ORM model to pure domain model."""
        return FileRecord(
            id=self.id,
            study_id=self.study_id,
            site_id=self.site_id,
            filename=self.filename,
            mime_type=self.mime_type,
            size_bytes=self.size_bytes,
            object_key=self.object_key,
            checksum_sha256=self.checksum_sha256,
            version_index=self.version_index,
            uploaded_by=self.uploaded_by,
            uploaded_at=self.uploaded_at,
            is_on_hold=self.is_on_hold,
            is_deleted=self.is_deleted,
            created_at=self.created_at,
            created_by=self.created_by,
            reason_for_change=self.reason_for_change,
        )


class ShareGrantModel(Base):
    """Database table representing internal share grants and permissions."""

    __tablename__ = "share_grants"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    file_record_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("file_records.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    granted_to_user_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    granted_by_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    scope: Mapped[str] = mapped_column(String(50), default="individual", nullable=False)
    permission_level: Mapped[str] = mapped_column(
        String(50), default="view", nullable=False
    )
    expires_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    revoked_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    is_deleted: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # 21 CFR Part 11 GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    file_record: Mapped[FileRecordModel] = relationship(back_populates="share_grants")

    def to_domain(self) -> ShareGrant:
        """Converts ORM model to pure domain model."""
        return ShareGrant(
            id=self.id,
            file_record_id=self.file_record_id,
            granted_to_user_id=self.granted_to_user_id,
            granted_by_user_id=self.granted_by_user_id,
            scope=ShareScope(self.scope),
            permission_level=PermissionLevel(self.permission_level),
            expires_at=self.expires_at,
            revoked_at=self.revoked_at,
            is_deleted=self.is_deleted,
            created_at=self.created_at,
            created_by=self.created_by,
            reason_for_change=self.reason_for_change,
        )


class GuestLinkModel(Base):
    """Database table representing time-bounded guest links."""

    __tablename__ = "guest_links"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    file_record_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("file_records.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    token_hmac: Mapped[str] = mapped_column(
        String(128), nullable=False, unique=True, index=True
    )
    expires_at: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    last_accessed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    access_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    revoked_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    # 21 CFR Part 11 GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    file_record: Mapped[FileRecordModel] = relationship(back_populates="guest_links")

    def to_domain(self) -> GuestLink:
        """Converts ORM model to pure domain model."""
        return GuestLink(
            id=self.id,
            file_record_id=self.file_record_id,
            token_hmac=self.token_hmac,
            expires_at=self.expires_at,
            created_by=self.created_by,
            last_accessed_at=self.last_accessed_at,
            access_count=self.access_count,
            revoked_at=self.revoked_at,
            created_at=self.created_at,
            reason_for_change=self.reason_for_change,
        )
