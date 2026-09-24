"""Relational DataLock SQLModel for persistent granular data lock and freeze governance.

Enforces 6-tier hierarchical lock inheritance across Study -> Site -> Subject -> Visit -> Form -> Field scopes.
Inherits from AuditedModel to maintain an immutable audit log, version index, and participation in Merkle-ledger seals.

Requirements: PRD-SYS-001, PRD-SYS-002, PRD-MDR-002, Trace-1, Trace-3, Trace-13, Trace-17
"""

import enum
import uuid
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import JSON, Boolean, DateTime, Index, String, func
from sqlalchemy.orm import Mapped, mapped_column, synonym

from .audit import AuditedModel


class ScopeTypeEnum(enum.StrEnum):
    """Hierarchical scope levels for clinical data governance."""

    STUDY = "STUDY"
    TRIAL = "TRIAL"
    SITE = "SITE"
    SUBJECT = "SUBJECT"
    VISIT = "VISIT"
    FORM = "FORM"
    ITEM_GROUP = "ITEM_GROUP"
    FIELD = "FIELD"


class LockTypeEnum(enum.StrEnum):
    """Lock state classifications."""

    UNLOCKED = "UNLOCKED"
    FROZEN = "FROZEN"
    LOCKED = "LOCKED"
    HARD_LOCK = "HARD_LOCK"
    SOFT_LOCK = "SOFT_LOCK"


class DataLock(AuditedModel):
    """Represents a persistent granular data lock record in PostgreSQL/SQLite.

    Locks or freezes clinical data across 6 hierarchical tiers:
    Study -> Site -> Subject -> Visit -> Form -> Field.
    """

    __tablename__ = "data_locks"
    __table_args__ = (
        Index("idx_data_locks_scope_active", "scope_type", "scope_id", "is_active"),
        Index(
            "idx_data_locks_hierarchy",
            "study_id",
            "site_id",
            "subject_id",
            "form_id",
        ),
    )

    # Hierarchical scope coordinates
    study_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    visit_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    form_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    item_group_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    field_name: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # Lock definition & state
    scope_type: Mapped[str] = mapped_column(
        String(50),
        nullable=False,
        default="FORM",
        index=True,
        comment="STUDY, SITE, SUBJECT, VISIT, FORM, ITEM_GROUP, FIELD",
    )
    scope = synonym("scope_type")

    scope_id: Mapped[str] = mapped_column(
        String(255),
        nullable=False,
        index=True,
        comment="Identifier of target entity at given scope level",
    )

    lock_type: Mapped[str] = mapped_column(
        String(50),
        nullable=False,
        default="HARD_LOCK",
        comment="FROZEN, LOCKED, HARD_LOCK, SOFT_LOCK",
    )

    is_active: Mapped[bool] = mapped_column(
        Boolean,
        default=True,
        nullable=False,
        index=True,
    )

    # GxP Creation Audit Trail
    created_at: Mapped[datetime] = mapped_column(
        DateTime,
        default=func.now(),
        nullable=False,
    )
    created_by: Mapped[str] = mapped_column(
        String(255),
        nullable=False,
    )
    reason_for_change: Mapped[str] = mapped_column(
        String(1000),
        nullable=False,
    )

    # GxP Unlock Override Audit Trail (>= 50 chars justification enforced)
    unlocked_at: Mapped[datetime | None] = mapped_column(
        DateTime,
        nullable=True,
    )
    unlocked_by: Mapped[str | None] = mapped_column(
        String(255),
        nullable=True,
    )
    unlock_justification: Mapped[str | None] = mapped_column(
        String(1000),
        nullable=True,
    )

    # 21 CFR Part 11 Step-up Dual Signature Token (X-Sig-Token)
    signature_token: Mapped[str | None] = mapped_column(
        String(1000),
        nullable=True,
    )
    additional_metadata: Mapped[dict | None] = mapped_column(
        JSON,
        nullable=True,
        default=dict,
    )

    def __init__(self, **kwargs: Any) -> None:
        """Initialize DataLock with support for legacy and flexible parameter aliases."""
        if "id" not in kwargs:
            lock_id = kwargs.pop("lock_id", None)
            kwargs["id"] = lock_id or f"dl_{uuid.uuid4().hex[:12]}"
        if "is_active" not in kwargs:
            kwargs["is_active"] = True
        if "created_at" not in kwargs:
            kwargs["created_at"] = datetime.now(UTC)

        # Alias mappings
        if "scope" in kwargs and "scope_type" not in kwargs:
            val = kwargs.pop("scope")
            kwargs["scope_type"] = str(val.value if hasattr(val, "value") else val)
        if "action" in kwargs and "lock_type" not in kwargs:
            action = kwargs.pop("action")
            kwargs["lock_type"] = (
                "FROZEN"
                if str(action).upper() == "FREEZE"
                else (
                    "HARD_LOCK"
                    if str(action).upper() == "HARD_LOCK"
                    else str(action).upper()
                )
            )
        if "status" in kwargs and "lock_type" not in kwargs:
            status = kwargs.pop("status")
            kwargs["lock_type"] = str(
                status.value if hasattr(status, "value") else status
            )
        if "locked_by" in kwargs and "created_by" not in kwargs:
            kwargs["created_by"] = kwargs.pop("locked_by")
        if "locked_at" in kwargs and "created_at" not in kwargs:
            locked_at_val = kwargs.pop("locked_at")
            if isinstance(locked_at_val, str):
                try:
                    locked_at_val = datetime.fromisoformat(
                        locked_at_val.replace("Z", "+00:00")
                    )
                except Exception:
                    locked_at_val = datetime.now(UTC)
            kwargs["created_at"] = locked_at_val

        # Infer scope_id if missing but specific ID provided
        if "scope_id" not in kwargs or not kwargs["scope_id"]:
            scope_type = kwargs.get("scope_type", "FORM").upper()
            if scope_type in ("FORM",) and kwargs.get("form_id"):
                kwargs["scope_id"] = kwargs["form_id"]
            elif scope_type in ("SUBJECT",) and kwargs.get("subject_id"):
                kwargs["scope_id"] = kwargs["subject_id"]
            elif scope_type in ("SITE",) and kwargs.get("site_id"):
                kwargs["scope_id"] = kwargs["site_id"]
            elif scope_type in ("VISIT",) and kwargs.get("visit_id"):
                kwargs["scope_id"] = kwargs["visit_id"]
            elif scope_type in ("STUDY", "TRIAL") and kwargs.get("study_id"):
                kwargs["scope_id"] = kwargs["study_id"]
            elif scope_type in ("FIELD",) and (
                kwargs.get("field_name") or kwargs.get("item_group_id")
            ):
                kwargs["scope_id"] = (
                    kwargs.get("field_name")
                    or kwargs.get("item_group_id")
                    or "FIELD_SCOPE"
                )
            else:
                kwargs["scope_id"] = kwargs.get("form_id") or "UNKNOWN_SCOPE"

        super().__init__(**kwargs)

    @property
    def lock_id(self) -> str:
        """Alias for id."""
        return self.id

    @lock_id.setter
    def lock_id(self, value: str) -> None:
        self.id = value

    @property
    def locked_by(self) -> str:
        """Alias for created_by."""
        return self.created_by

    @locked_by.setter
    def locked_by(self, value: str) -> None:
        self.created_by = value

    @property
    def locked_at(self) -> datetime:
        """Alias for created_at."""
        return self.created_at

    @locked_at.setter
    def locked_at(self, value: datetime | str) -> None:
        if isinstance(value, str):
            try:
                self.created_at = datetime.fromisoformat(value.replace("Z", "+00:00"))
            except ValueError:
                self.created_at = datetime.now(UTC)
        else:
            self.created_at = value

    @property
    def status(self) -> str:
        """Returns active lock status or UNLOCKED if inactive."""
        if not self.is_active:
            return "UNLOCKED"
        return self.lock_type

    @status.setter
    def status(self, value: str) -> None:
        self.lock_type = value
