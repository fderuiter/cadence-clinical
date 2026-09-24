from datetime import datetime

from sqlalchemy import JSON, DateTime, String, UniqueConstraint, func
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .audit import AuditedModel


class ComplianceChangeRequest(AuditedModel):
    """Represents a GxP-regulated compliance change request.

    Maintains a multi-approver workflow for system settings and policy updates.
    """

    __tablename__ = "compliance_change_requests"

    setting_key: Mapped[str] = mapped_column(String(255), nullable=False)
    old_value: Mapped[str | None] = mapped_column(String(255), nullable=True)
    new_value: Mapped[str | None] = mapped_column(String(255), nullable=True)
    requested_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason: Mapped[str] = mapped_column(String(1000), nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING_APPROVAL", nullable=False
    )
    impact_assessment: Mapped[dict | None] = mapped_column(JSON, nullable=True)

    signatures: Mapped[list[ChangeApprovalSignature]] = relationship(
        "ChangeApprovalSignature",
        primaryjoin="ComplianceChangeRequest.id == foreign(ChangeApprovalSignature.change_request_id)",
        back_populates="change_request",
        cascade="all, delete-orphan",
        lazy="selectin",
    )


class ChangeApprovalSignature(AuditedModel):
    """Represents a cryptographic/electronic approval signature for a change request."""

    __tablename__ = "change_approval_signatures"
    __table_args__ = (
        UniqueConstraint("signature_token", name="uq_change_approval_signature_token"),
    )

    change_request_id: Mapped[str] = mapped_column(String(36), nullable=False)
    approver_id: Mapped[str] = mapped_column(String(255), nullable=False)
    signature_token: Mapped[str] = mapped_column(String(255), nullable=False)
    role: Mapped[str] = mapped_column(String(255), nullable=False)
    signed_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )

    change_request: Mapped[ComplianceChangeRequest] = relationship(
        "ComplianceChangeRequest",
        primaryjoin="foreign(ChangeApprovalSignature.change_request_id) == ComplianceChangeRequest.id",
        back_populates="signatures",
        uselist=False,
    )


class SiteComplianceCache(AuditedModel):
    """Local relational cache of site-level and study-level milestone compliance statuses.

    Tracks whether necessary EDL documents have been approved in eTMF.
    """

    __tablename__ = "site_compliance_caches"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    milestone: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    is_complete: Mapped[bool] = mapped_column(default=False, nullable=False)
    missing_documents: Mapped[str | None] = mapped_column(String(1000), nullable=True)
