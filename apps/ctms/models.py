import uuid
from datetime import UTC, date, datetime
from typing import Any

from sqlalchemy import JSON, Boolean, DateTime, Float, ForeignKey, Integer, String, func
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column
from sqlmodel import Field, SQLModel


class Base(DeclarativeBase):
    pass


# Bind SQLModel metadata to Base metadata to support unified relational db management
SQLModel.metadata = Base.metadata


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
    actual_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    status: Mapped[str] = mapped_column(String(50), default="SCHEDULED", nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    signature_manifestation: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )
    signer: Mapped[str | None] = mapped_column(String(255), nullable=True)
    signing_timestamp: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    offline_sync_markers: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )
    sync_status: Mapped[str | None] = mapped_column(
        String(50), default="RESOLVED", nullable=True
    )


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

    offline_sync_markers: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )
    sync_status: Mapped[str | None] = mapped_column(
        String(50), default="RESOLVED", nullable=True
    )


class MonitoringVisitDefeated(Base):
    """
    Represents a preserved/durable copy of a defeated monitoring visit payload
    resulting from conflict resolution or structural sync conflicts.
    """

    __tablename__ = "ctms_defeated_monitoring_visits"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    visit_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    actual_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    findings: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    device_timestamp: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    offline_sync_markers: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False)
    status: Mapped[str] = mapped_column(
        String(100),
        default="Defeated by online-merge conflict resolution",
        nullable=False,
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(500), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class CTMSClinicalQuery(Base):
    """
    Represents a clinical query state record for GxP data discrepancy tracking
    resulting from sync conflicts or other site/visit level issues.
    """

    __tablename__ = "ctms_clinical_queries"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    visit_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    status: Mapped[str] = mapped_column(String(50), default="OPEN", nullable=False)
    explanation: Mapped[str] = mapped_column(String(1000), nullable=True)

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
    planned_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    actual_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
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
    effective_end_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

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
    triggered_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

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
    milestone_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("ctms_payment_milestones.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )
    amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    payment_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    due_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    paid_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class RegulatoryForm(Base):
    """
    Represents a clinical trial regulatory or generated form with 21 CFR Part 11 compliant signatures.
    """

    __tablename__ = "ctms_regulatory_forms"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    form_type: Mapped[str] = mapped_column(String(50), nullable=False)
    rendered_content: Mapped[str] = mapped_column(String(100000), nullable=False)
    approval_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Embedded signatures
    signature_manifestation: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )
    signer: Mapped[str | None] = mapped_column(String(255), nullable=True)
    signing_timestamp: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)


class SiteStaffMember(SQLModel, table=True):
    __tablename__ = "site_staff_members"

    id: str = Field(primary_key=True)
    site_id: str = Field(index=True)
    user_id: str = Field(index=True, unique=True)
    first_name: str
    last_name: str
    email: str
    primary_role: str
    license_number: str | None = None
    gcp_certified: bool = Field(default=False)

    # GxP Audit fields
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    created_by: str
    reason_for_change: str = "Initial Staff Registration"
    version_index: int = Field(default=1)
    is_active: bool = Field(default=True)
    is_deleted: bool = Field(default=False)


class DOADelegationRecord(SQLModel, table=True):
    __tablename__ = "doa_delegation_records"

    id: str = Field(primary_key=True)
    site_id: str = Field(index=True)
    staff_user_id: str = Field(index=True, foreign_key="site_staff_members.user_id")
    task_code: str = Field(index=True)
    start_date: date
    end_date: date | None = None
    status: str = Field(default="PENDING_PI_APPROVAL")
    pi_signature_hash: str | None = None
    pi_approved_at: datetime | None = None

    # GxP Audit fields
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    created_by: str
    reason_for_change: str
    version_index: int = Field(default=1)
    is_active: bool = Field(default=True)
    is_deleted: bool = Field(default=False)


GeneratedForm = RegulatoryForm


class CTMSDelegation(Base):
    """
    Represents site staff task delegation of authority with Part 11 compliance.

    Requirements: PRD-SYS-001
    """

    __tablename__ = "ctms_delegations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    staff_user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    task_codes: Mapped[list[str]] = mapped_column(JSON, nullable=False)
    start_date: Mapped[str] = mapped_column(String(50), nullable=False)
    end_date: Mapped[str | None] = mapped_column(String(50), nullable=True)
    is_active: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    signed_off: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class CountryRegulatoryMilestone(Base):
    """Tracks country-level regulatory submissions and approvals."""

    __tablename__ = "ctms_country_regulatory_milestones"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    country_code: Mapped[str] = mapped_column(String(10), nullable=False, index=True)
    milestone_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    planned_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    actual_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    status: Mapped[str] = mapped_column(String(50), default="PLANNED", nullable=False)
    approval_number: Mapped[str | None] = mapped_column(String(255), nullable=True)
    authority_name: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class EssentialDocument(Base):
    """Tracks site-level essential documents required for regulatory compliance."""

    __tablename__ = "ctms_essential_documents"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    document_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    file_name: Mapped[str] = mapped_column(String(255), nullable=False)
    file_hash: Mapped[str] = mapped_column(String(255), nullable=False)
    expiration_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    status: Mapped[str] = mapped_column(String(50), default="DRAFT", nullable=False)
    review_notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    reviewed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reviewed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class SiteGreenlightGate(Base):
    """Manages site greenlight gatekeeper validation for study startup."""

    __tablename__ = "ctms_site_greenlight_gates"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True, index=True
    )
    overall_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    contract_approved: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    irb_approved: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    form_1572_approved: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    doa_signed_off: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    ip_ready: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    greenlight_certified_by: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    greenlight_certified_at: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    rejection_reason: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class ProtocolDeviation(Base):
    """Tracks protocol deviations, root cause analyses, and CAPA escalations."""

    __tablename__ = "ctms_protocol_deviations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    visit_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    deviation_category: Mapped[str] = mapped_column(String(100), nullable=False)
    severity: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # MINOR, MAJOR, CRITICAL
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(String(2000), nullable=False)
    date_occurred: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    date_identified: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="IDENTIFIED", nullable=False
    )
    root_cause_5whys: Mapped[list[str]] = mapped_column(
        JSON, default=list, nullable=False
    )
    root_cause_summary: Mapped[str | None] = mapped_column(String(2000), nullable=True)
    corrective_action_plan: Mapped[str | None] = mapped_column(
        String(2000), nullable=True
    )
    preventive_action_plan: Mapped[str | None] = mapped_column(
        String(2000), nullable=True
    )
    quality_capa_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reported_by: Mapped[str] = mapped_column(String(255), nullable=False)
    resolved_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    resolved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class DeviationActionItem(Base):
    """Tracks action items and remediation tasks assigned from protocol deviations."""

    __tablename__ = "ctms_deviation_action_items"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    deviation_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("ctms_protocol_deviations.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    description: Mapped[str] = mapped_column(String(1000), nullable=False)
    assignee_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    assignee_role: Mapped[str] = mapped_column(String(100), nullable=False)
    due_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="OPEN", nullable=False)
    resolution_notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    completed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    completed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class RBQMKRIMetric(Base):
    """Tracks Key Risk Indicator (KRI) calculations and QTL breaches under ICH E6(R2)/(R3)."""

    __tablename__ = "ctms_rbqm_kri_metrics"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    metric_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    metric_value: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    threshold_low: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    threshold_high: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    breach_status: Mapped[str] = mapped_column(
        String(50), default="NORMAL", nullable=False
    )
    calculation_date: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class SiteRiskScore(Base):
    """Tracks composite site risk scoring and adaptive monitoring recommendations."""

    __tablename__ = "ctms_site_risk_scores"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    composite_score: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    risk_level: Mapped[str] = mapped_column(String(50), default="LOW", nullable=False)
    assessment_date: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    recommended_monitoring_type: Mapped[str] = mapped_column(
        String(100), default="ROUTINE_ON_SITE", nullable=False
    )
    monitoring_interval_days: Mapped[int] = mapped_column(
        Integer, default=30, nullable=False
    )

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class ProcedurePaymentGrid(Base):
    """Tracks visit and procedure payment matrices for investigator grants."""

    __tablename__ = "ctms_procedure_payment_grids"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    grant_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("ctms_investigator_grants.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    visit_name: Mapped[str] = mapped_column(String(100), nullable=False)
    procedure_code: Mapped[str] = mapped_column(String(100), nullable=False)
    procedure_name: Mapped[str] = mapped_column(String(255), nullable=False)
    base_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    overhead_percentage: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    withholding_percentage: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class FinancialInvoice(Base):
    """Tracks batch invoices and payment disbursements for investigator sites."""

    __tablename__ = "ctms_financial_invoices"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    grant_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("ctms_investigator_grants.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    invoice_number: Mapped[str] = mapped_column(
        String(100), nullable=False, unique=True, index=True
    )
    invoice_type: Mapped[str] = mapped_column(String(100), nullable=False)
    gross_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    withholding_amount: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    net_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="DRAFT", nullable=False)
    payable_ids: Mapped[list[str]] = mapped_column(JSON, default=list, nullable=False)
    approved_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    approved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    disbursed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class IPKitRecord(Base):
    """Tracks Investigational Product (IP) kit inventories, lots, and subject dispensations."""

    __tablename__ = "ctms_ip_kit_records"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kit_number: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    lot_number: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    kit_type: Mapped[str] = mapped_column(
        String(50), default="ACTIVE_DRUG", nullable=False
    )
    shipment_tracking_number: Mapped[str] = mapped_column(String(100), nullable=False)
    expiration_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="RECEIVED_AVAILABLE", nullable=False
    )
    received_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    dispensed_subject_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    dispensed_visit_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    dispensed_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    returned_units_count: Mapped[int | None] = mapped_column(Integer, nullable=True)
    expected_units_count: Mapped[int | None] = mapped_column(Integer, nullable=True)
    compliance_percentage: Mapped[float | None] = mapped_column(Float, nullable=True)
    notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class IPTemperatureExcursion(Base):
    """Tracks temperature excursions, quarantine holds, and QA stability assessments."""

    __tablename__ = "ctms_ip_temperature_excursions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kit_ids: Mapped[list[str]] = mapped_column(JSON, default=list, nullable=False)
    excursion_type: Mapped[str] = mapped_column(
        String(50), default="STORAGE", nullable=False
    )
    min_temp_celsius: Mapped[float] = mapped_column(Float, nullable=False)
    max_temp_celsius: Mapped[float] = mapped_column(Float, nullable=False)
    duration_hours: Mapped[float] = mapped_column(Float, nullable=False)
    occurred_at: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    disposition_status: Mapped[str] = mapped_column(
        String(50), default="QUARANTINED", nullable=False
    )
    qa_reviewed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    qa_reviewed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    qa_rationale: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class IPDestructionCertificate(Base):
    """Tracks witnessed on-site or depot destruction of investigational products."""

    __tablename__ = "ctms_ip_destruction_certificates"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    certificate_number: Mapped[str] = mapped_column(
        String(100), nullable=False, unique=True, index=True
    )
    kit_ids: Mapped[list[str]] = mapped_column(JSON, default=list, nullable=False)
    destruction_method: Mapped[str] = mapped_column(String(100), nullable=False)
    destruction_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    witness_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    witness_role: Mapped[str] = mapped_column(String(100), nullable=False)
    pi_signature_hash: Mapped[str] = mapped_column(String(255), nullable=False)
    pi_signed_at: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    reason_for_destruction: Mapped[str] = mapped_column(String(1000), nullable=False)

    # Standard Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class ETMFSyncRecord(Base):
    """Tracks automated synchronization of CTMS artifacts with eTMF DIA Reference Model."""

    __tablename__ = "ctms_etmf_sync_records"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    artifact_type: Mapped[str] = mapped_column(String(100), nullable=False)
    source_record_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    etmf_document_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    dia_zone: Mapped[str] = mapped_column(String(50), nullable=False)
    dia_section: Mapped[str] = mapped_column(String(50), nullable=False)
    dia_artifact: Mapped[str] = mapped_column(String(100), nullable=False)
    sync_status: Mapped[str] = mapped_column(
        String(50), default="SYNCED", nullable=False
    )
    error_message: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    synced_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )

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
