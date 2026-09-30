"""Pydantic v2 Models and Schemas for Financial & Grant Fee Matrix Engine.

Defines schemas for site grant contracts, multi-tiered overhead schedules,
split-payee allocations, event-driven procedure fee matrices, holdback retention,
and cryptographic payable ledger line items (21 CFR Part 11 compliant).
"""

from __future__ import annotations

from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field, field_validator


class TriggerType(StrEnum):
    """Event triggers initiating payable line items."""

    ENROLLMENT = "ENROLLMENT"
    VISIT_COMPLETED = "VISIT_COMPLETED"
    FORM_APPROVED = "FORM_APPROVED"
    QUERY_RESOLVED = "QUERY_RESOLVED"
    AE_REPORTED = "AE_REPORTED"
    UNSCHEDULED_PROCEDURE = "UNSCHEDULED_PROCEDURE"
    PASSTHROUGH_EXPENSE = "PASSTHROUGH_EXPENSE"
    MANUAL_ADJUSTMENT = "MANUAL_ADJUSTMENT"
    MILESTONE_ACHIEVED = "MILESTONE_ACHIEVED"


class PayableStatus(StrEnum):
    """Lifecycle status of payable ledger line items."""

    PENDING = "PENDING"
    APPROVED = "APPROVED"
    INVOICED = "INVOICED"
    PAID = "PAID"
    HELD = "HELD"
    CANCELLED = "CANCELLED"


class OverheadTier(BaseModel):
    """Multi-tiered cumulative or bracketed overhead rate definition."""

    tier_id: str
    min_threshold: float = Field(default=0.0, ge=0.0)
    max_threshold: float | None = Field(default=None, ge=0.0)
    overhead_rate: float = Field(..., ge=0.0, le=1.0)


class PayeeSplit(BaseModel):
    """Split-payee distribution allocation rule."""

    payee_id: str
    payee_name: str
    percentage: float = Field(..., gt=0.0, le=1.0)
    is_primary: bool = False
    account_reference: str | None = None


class HoldbackReserveRule(BaseModel):
    """Holdback retention reserve policy."""

    holdback_rate: float = Field(default=0.10, ge=0.0, le=0.50)
    cap_amount: float | None = Field(default=None, ge=0.0)
    release_trigger: str = "STUDY_CLOSEOUT"


class FeeMatrixItem(BaseModel):
    """Per-procedure, per-visit, or milestone fee definition."""

    item_code: str
    description: str
    trigger_type: TriggerType
    event_identifier: str | None = None
    base_amount: float = Field(..., ge=0.0)
    overhead_applicable: bool = True
    withholding_applicable: bool = True
    holdback_applicable: bool = True
    max_allowable_events: int | None = Field(default=None, ge=1)


class GrantContract(BaseModel):
    """Site Investigator Grant Agreement configuration."""

    contract_id: str
    study_id: str
    site_id: str
    currency: str = "USD"
    effective_date: datetime
    expiration_date: datetime | None = None
    overhead_tiers: list[OverheadTier] = Field(default_factory=list)
    default_overhead_rate: float = Field(default=0.0, ge=0.0, le=1.0)
    default_withholding_rate: float = Field(default=0.0, ge=0.0, le=1.0)
    holdback_rule: HoldbackReserveRule = Field(default_factory=HoldbackReserveRule)
    payee_splits: list[PayeeSplit] = Field(default_factory=list)
    fee_matrix: list[FeeMatrixItem] = Field(default_factory=list)
    payment_terms_days: int = Field(default=30, ge=0)
    is_active: bool = True

    @field_validator("payee_splits")
    @classmethod
    def validate_splits_sum(cls, splits: list[PayeeSplit]) -> list[PayeeSplit]:
        """Validate that non-empty payee splits sum to 1.0 (100%)."""
        if not splits:
            return splits
        total = sum(s.percentage for s in splits)
        if not (0.999 <= total <= 1.001):
            raise ValueError(f"Payee splits must sum to 1.0 (100%), got {total:.4f}")
        return splits


class ClinicalEvent(BaseModel):
    """Inbound clinical execution milestone event."""

    event_id: str
    study_id: str
    site_id: str
    trigger_type: TriggerType
    occurred_at: datetime
    subject_id: str | None = None
    visit_id: str | None = None
    form_id: str | None = None
    procedure_code: str | None = None
    custom_amount: float | None = None
    metadata: dict[str, Any] = Field(default_factory=dict)


class PayableBreakdown(BaseModel):
    """Detailed mathematical breakdown of line item deductions and additions."""

    base_amount: float
    overhead_amount: float
    gross_amount: float
    withholding_amount: float
    holdback_amount: float
    net_payable: float
    effective_overhead_rate: float
    withholding_rate: float
    holdback_rate: float


class PayableLineItem(BaseModel):
    """Computed payable transaction line item with 21 CFR Part 11 audit hash."""

    line_item_id: str
    event_id: str
    contract_id: str
    item_code: str
    item_description: str
    payee_id: str
    payee_name: str
    split_percentage: float
    currency: str
    breakdown: PayableBreakdown
    status: PayableStatus = PayableStatus.PENDING
    occurred_at: datetime
    calculated_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    audit_hash: str


class HoldbackLedgerAccount(BaseModel):
    """Holdback retention escrow tracking account."""

    contract_id: str
    site_id: str
    currency: str
    accumulated_holdback: float = 0.0
    released_holdback: float = 0.0
    current_balance: float = 0.0


class LedgerSummary(BaseModel):
    """Consolidated payable ledger statement with cryptographic digest."""

    summary_id: str
    study_id: str
    site_id: str
    contract_id: str
    currency: str
    period_start: datetime
    period_end: datetime
    total_events_processed: int
    total_line_items: int
    total_base_amount: float
    total_overhead_amount: float
    total_gross_payable: float
    total_withholding_deducted: float
    total_holdback_reserved: float
    total_net_payable: float
    line_items: list[PayableLineItem]
    holdback_account: HoldbackLedgerAccount
    payee_subtotals: dict[str, float]
    cryptographic_digest: str
