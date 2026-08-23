"""SQLAlchemy ORM models for Site Payments microservice.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import JSON, Boolean, Float, ForeignKey, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship

from packages.database.datetime_helpers import UTCDateTime


class Base(DeclarativeBase):
    """Declarative Base for Site Payments microservice tables."""

    pass


class BudgetContractModel(Base):
    """Database table representing active and versioned investigator grant contracts."""

    __tablename__ = "payments_budget_contracts"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    contract_name: Mapped[str] = mapped_column(String(255), nullable=False)
    tier_level: Mapped[str] = mapped_column(
        String(50), default="TIER_1_ACADEMIC", nullable=False
    )
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)
    total_budget: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    overhead_percentage: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    withholding_percentage: Mapped[float] = mapped_column(
        Float, default=10.0, nullable=False
    )
    effective_date: Mapped[str | None] = mapped_column(String(50), nullable=True)
    status: Mapped[str] = mapped_column(
        String(50), default="ACTIVE", nullable=False, index=True
    )
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    fee_schedules: Mapped[list[FeeScheduleModel]] = relationship(
        "FeeScheduleModel",
        back_populates="contract",
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    split_payees: Mapped[list[SplitPayeeConfigModel]] = relationship(
        "SplitPayeeConfigModel",
        back_populates="contract",
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    payables: Mapped[list[PayableModel]] = relationship(
        "PayableModel",
        back_populates="contract",
        cascade="all, delete-orphan",
        lazy="selectin",
    )


class FeeScheduleModel(Base):
    """Database table linking specific visit activities and eCRF forms to agreed reimbursement rates."""

    __tablename__ = "payments_fee_schedules"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    contract_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_budget_contracts.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    visit_code: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    activity_id: Mapped[str | None] = mapped_column(String(100), nullable=True)
    form_id: Mapped[str | None] = mapped_column(String(100), nullable=True, index=True)
    procedure_code: Mapped[str] = mapped_column(String(100), nullable=False)
    procedure_name: Mapped[str] = mapped_column(String(255), nullable=False)
    base_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    overhead_percentage: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    withholding_percentage: Mapped[float] = mapped_column(
        Float, default=10.0, nullable=False
    )
    patient_stipend: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    travel_reimbursement: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    requires_sdv: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    contract: Mapped[BudgetContractModel] = relationship(
        "BudgetContractModel",
        back_populates="fee_schedules",
        lazy="selectin",
    )


class SplitPayeeConfigModel(Base):
    """Database table configuring multi-party split payee disbursement shares for a site contract."""

    __tablename__ = "payments_split_payees"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    contract_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_budget_contracts.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    payee_id: Mapped[str] = mapped_column(String(100), nullable=False)
    payee_name: Mapped[str] = mapped_column(String(255), nullable=False)
    payee_role: Mapped[str] = mapped_column(String(50), nullable=False)
    allocation_percentage: Mapped[float] = mapped_column(
        Float, nullable=False
    )  # e.g., 70.0, 20.0, 10.0
    bank_account_reference: Mapped[str | None] = mapped_column(
        String(100), nullable=True
    )
    tax_identifier: Mapped[str | None] = mapped_column(String(100), nullable=True)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    contract: Mapped[BudgetContractModel] = relationship(
        "BudgetContractModel",
        back_populates="split_payees",
        lazy="selectin",
    )


class PayableModel(Base):
    """Database table recording earned, gated, and approved site payable line items."""

    __tablename__ = "payments_payables"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    contract_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_budget_contracts.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    visit_code: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    form_id: Mapped[str | None] = mapped_column(String(100), nullable=True, index=True)
    procedure_code: Mapped[str | None] = mapped_column(String(100), nullable=True)
    procedure_name: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # Trigger provenance
    trigger_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    trigger_event_id: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True, index=True
    )
    trigger_source: Mapped[str] = mapped_column(
        String(100), default="EDC_SERVICE", nullable=False
    )

    # Financial breakdown
    base_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    overhead_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    patient_stipend_amount: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    travel_reimbursement_amount: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    gross_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    withholding_rate: Mapped[float] = mapped_column(Float, default=10.0, nullable=False)
    withholding_amount: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    net_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)

    # Lifecycle status & CRA SDV Gating
    status: Mapped[str] = mapped_column(
        String(50), default="TRIGGERED", nullable=False, index=True
    )
    is_sdv_gated: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False, index=True
    )
    sdv_verified_at: Mapped[datetime | None] = mapped_column(UTCDateTime, nullable=True)
    sdv_verified_by: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    contract: Mapped[BudgetContractModel] = relationship(
        "BudgetContractModel",
        back_populates="payables",
        lazy="selectin",
    )
    allocations: Mapped[list[PayableAllocationModel]] = relationship(
        "PayableAllocationModel",
        back_populates="payable",
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    holdback_reserve: Mapped[HoldbackReserveModel | None] = relationship(
        "HoldbackReserveModel",
        back_populates="payable",
        uselist=False,
        cascade="all, delete-orphan",
        lazy="selectin",
    )


class PayableAllocationModel(Base):
    """Database table recording split payee disbursement shares for a specific payable."""

    __tablename__ = "payments_payable_allocations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    payable_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_payables.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    payee_id: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    payee_name: Mapped[str] = mapped_column(String(255), nullable=False)
    payee_role: Mapped[str] = mapped_column(String(50), nullable=False)
    allocation_percentage: Mapped[float] = mapped_column(Float, nullable=False)
    gross_share: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    withholding_share: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    net_share: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="ALLOCATED", nullable=False)

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationship
    payable: Mapped[PayableModel] = relationship(
        "PayableModel",
        back_populates="allocations",
        lazy="selectin",
    )


class HoldbackReserveModel(Base):
    """Database table managing 10% holdback retention reserves and formal release governance."""

    __tablename__ = "payments_holdback_reserves"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    contract_id: Mapped[str] = mapped_column(String(36), nullable=False, index=True)
    payable_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_payables.id", ondelete="CASCADE"),
        nullable=False,
        unique=True,
        index=True,
    )
    holdback_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="HELD", nullable=False, index=True
    )

    # Formal DM & Quality Release Sign-offs
    release_authorized_by: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    release_authorized_at: Mapped[datetime | None] = mapped_column(
        UTCDateTime, nullable=True
    )
    dm_signoff_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    dm_signoff_at: Mapped[datetime | None] = mapped_column(UTCDateTime, nullable=True)
    quality_signoff_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    quality_signoff_at: Mapped[datetime | None] = mapped_column(
        UTCDateTime, nullable=True
    )
    release_notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationship
    payable: Mapped[PayableModel] = relationship(
        "PayableModel",
        back_populates="holdback_reserve",
        lazy="selectin",
    )


class PaymentAuditLogModel(Base):
    """Immutable, append-only 21 CFR Part 11 financial transaction audit ledger."""

    __tablename__ = "payments_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    entity_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    entity_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    action: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    previous_state: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    new_state: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    details: Mapped[str | None] = mapped_column(String(2000), nullable=True)
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_roles: Mapped[str] = mapped_column(String(255), nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False, index=True
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class SelfBillingInvoiceModel(Base):
    """Database table representing self-billing invoices aggregated over a billing cycle."""

    __tablename__ = "payments_self_billing_invoices"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    invoice_number: Mapped[str] = mapped_column(
        String(100), unique=True, nullable=False, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    contract_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_budget_contracts.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    billing_cycle: Mapped[str] = mapped_column(
        String(50), default="MONTHLY", nullable=False, index=True
    )
    period_start: Mapped[str] = mapped_column(String(50), nullable=False)
    period_end: Mapped[str] = mapped_column(String(50), nullable=False)
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)

    # Aggregated Financials
    total_base_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    total_overhead_amount: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    total_patient_stipend: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    total_travel_reimbursement: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    total_gross_amount: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    total_holdback_amount: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    total_net_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    payable_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)

    # Lifecycle & Approval
    status: Mapped[str] = mapped_column(
        String(50), default="DRAFT", nullable=False, index=True
    )
    approved_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    approved_at: Mapped[datetime | None] = mapped_column(UTCDateTime, nullable=True)

    # Enterprise ERP Synchronization
    erp_system: Mapped[str | None] = mapped_column(
        String(50), default="SAP", nullable=True
    )
    erp_reference_id: Mapped[str | None] = mapped_column(
        String(100), nullable=True, index=True
    )
    erp_sync_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False, index=True
    )
    erp_synced_at: Mapped[datetime | None] = mapped_column(UTCDateTime, nullable=True)

    # Reconciliation
    reconciled_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reconciled_at: Mapped[datetime | None] = mapped_column(UTCDateTime, nullable=True)
    reconciliation_reference: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    reconciliation_notes: Mapped[str | None] = mapped_column(
        String(1000), nullable=True
    )

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    contract: Mapped[BudgetContractModel] = relationship(
        "BudgetContractModel",
        lazy="selectin",
    )
    line_items: Mapped[list[InvoiceLineItemModel]] = relationship(
        "InvoiceLineItemModel",
        back_populates="invoice",
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    remittance_advice: Mapped[RemittanceAdviceModel | None] = relationship(
        "RemittanceAdviceModel",
        back_populates="invoice",
        uselist=False,
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    erp_logs: Mapped[list[ErpSyncLogModel]] = relationship(
        "ErpSyncLogModel",
        back_populates="invoice",
        cascade="all, delete-orphan",
        lazy="selectin",
    )


class InvoiceLineItemModel(Base):
    """Database table recording itemized procedure line items on a self-billing invoice."""

    __tablename__ = "payments_invoice_line_items"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    invoice_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_self_billing_invoices.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    payable_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("payments_payables.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    visit_code: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    procedure_code: Mapped[str | None] = mapped_column(String(100), nullable=True)
    procedure_name: Mapped[str | None] = mapped_column(String(255), nullable=True)

    base_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    overhead_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    patient_stipend: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    travel_reimbursement: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    gross_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    withholding_rate: Mapped[float] = mapped_column(Float, default=10.0, nullable=False)
    withholding_amount: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    net_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    invoice: Mapped[SelfBillingInvoiceModel] = relationship(
        "SelfBillingInvoiceModel",
        back_populates="line_items",
        lazy="selectin",
    )
    payable: Mapped[PayableModel | None] = relationship(
        "PayableModel",
        lazy="selectin",
    )


class RemittanceAdviceModel(Base):
    """Database table managing cryptographically signed PDF remittance advice statements."""

    __tablename__ = "payments_remittance_advices"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    remittance_number: Mapped[str] = mapped_column(
        String(100), unique=True, nullable=False, index=True
    )
    invoice_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_self_billing_invoices.id", ondelete="CASCADE"),
        unique=True,
        nullable=False,
        index=True,
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    currency: Mapped[str] = mapped_column(String(10), default="USD", nullable=False)

    gross_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    overhead_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    holdback_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    net_amount: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)

    # Cryptographic Sealing & SHA-256 Digest
    pdf_sha256_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    pdf_content: Mapped[str | None] = mapped_column(String, nullable=True)
    is_sealed: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # 21 CFR Part 11 Electronic Signature
    is_signed: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    signed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    signed_at: Mapped[datetime | None] = mapped_column(UTCDateTime, nullable=True)
    signer_role: Mapped[str | None] = mapped_column(String(100), nullable=True)
    signature_manifest: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )

    # Automated Email Dispatch
    dispatched_to: Mapped[str | None] = mapped_column(String(255), nullable=True)
    dispatched_at: Mapped[datetime | None] = mapped_column(UTCDateTime, nullable=True)
    dispatch_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False, index=True
    )

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationship
    invoice: Mapped[SelfBillingInvoiceModel] = relationship(
        "SelfBillingInvoiceModel",
        back_populates="remittance_advice",
        lazy="selectin",
    )


class ErpSyncLogModel(Base):
    """Database table recording bidirectional ERP synchronization events and webhooks."""

    __tablename__ = "payments_erp_sync_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    invoice_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("payments_self_billing_invoices.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    erp_system: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    sync_direction: Mapped[str] = mapped_column(
        String(50), default="OUTBOUND_EXPORT", nullable=False
    )
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False, index=True
    )
    payload: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    response_data: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    external_reference: Mapped[str | None] = mapped_column(
        String(100), nullable=True, index=True
    )
    reconciled: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationship
    invoice: Mapped[SelfBillingInvoiceModel] = relationship(
        "SelfBillingInvoiceModel",
        back_populates="erp_logs",
        lazy="selectin",
    )
