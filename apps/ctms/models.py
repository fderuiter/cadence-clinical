import uuid
from datetime import datetime
from typing import Optional

from sqlalchemy import Boolean, DateTime, Float, ForeignKey, Integer, String, func
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


class MonitoringVisit(Base):
    """
    Represents a clinical trial site monitoring visit report (MVR) lifecycle.
    """

    __tablename__ = "ctms_monitoring_visits"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    cra_id: Mapped[str] = mapped_column(String(255), nullable=False)
    visit_type: Mapped[str] = mapped_column(String(50), nullable=False)
    scheduled_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    actual_date: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    status: Mapped[str] = mapped_column(String(50), default="SCHEDULED", nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class MonitoringVisitFinding(Base):
    """
    Represents an individual monitoring visit finding or action item.
    """

    __tablename__ = "ctms_monitoring_visit_findings"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    visit_id: Mapped[str] = mapped_column(String(36), nullable=False, index=True)
    text: Mapped[str] = mapped_column(String(1000), nullable=False)
    severity: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # MINOR, MAJOR, CRITICAL
    resolution_status: Mapped[str] = mapped_column(
        String(50), default="OPEN", nullable=False
    )  # OPEN, RESOLVED

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class GeneratedLetter(Base):
    """
    Represents a persisted confirmation or follow-up letter generated for a monitoring visit.
    Ensures that letters can be retrieved without re-rendering previously issued content.
    """

    __tablename__ = "ctms_generated_letters"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    visit_id: Mapped[str] = mapped_column(String(36), nullable=False, index=True)
    letter_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # CONFIRMATION, FOLLOW_UP
    rendered_content: Mapped[str] = mapped_column(String(100000), nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class RecruitmentRecord(Base):
    """
    Tracks site recruitment metrics for clinical studies with standard Part 11 audit fields.
    """

    __tablename__ = "ctms_recruitment_records"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    screened_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    enrolled_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    target_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    as_of_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class SiteMilestone(Base):
    """
    Represents site milestones with planning details and status tracking under Part 11 compliance.
    """

    __tablename__ = "ctms_site_milestones"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    milestone_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    planned_date: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    actual_date: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    status: Mapped[str] = mapped_column(String(50), default="PLANNED", nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class CRAAllocation(Base):
    """
    Represents allocation of a CRA to a site and study with active/inactive statuses.
    """

    __tablename__ = "ctms_cra_allocations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    cra_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    status: Mapped[str] = mapped_column(String(50), default="ACTIVE", nullable=False)
    effective_start_date: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    effective_end_date: Mapped[Optional[datetime]] = mapped_column(
        DateTime, nullable=True
    )

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class InvestigatorGrant(Base):
    """
    Represents an Investigator Grant for a site and study, tracking the total budget
    and approval status under Part 11.
    """

    __tablename__ = "ctms_investigator_grants"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    total_budget: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="DRAFT", nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class BudgetLineItem(Base):
    """
    Represents a specific line item in the budget of an Investigator Grant.
    """

    __tablename__ = "ctms_budget_line_items"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    grant_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("ctms_investigator_grants.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    category: Mapped[str] = mapped_column(String(100), nullable=False)
    description: Mapped[str] = mapped_column(String(255), nullable=False)
    amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class PaymentMilestone(Base):
    """
    Represents a predefined payment milestone triggered automatically or manually.
    """

    __tablename__ = "ctms_payment_milestones"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    grant_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("ctms_investigator_grants.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    milestone_name: Mapped[str] = mapped_column(String(255), nullable=False)
    trigger_condition: Mapped[str] = mapped_column(String(100), nullable=False)
    amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    is_triggered: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    triggered_at: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class InvestigatorPayable(Base):
    """
    Tracks payables generated dynamically from milestones or custom triggers.
    """

    __tablename__ = "ctms_investigator_payables"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    grant_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("ctms_investigator_grants.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    milestone_id: Mapped[Optional[str]] = mapped_column(
        String(36),
        ForeignKey("ctms_payment_milestones.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )
    amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    payment_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    due_date: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    paid_at: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)

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
