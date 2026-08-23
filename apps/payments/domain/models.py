"""Domain entity models and enumerations for Site Payments.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class ContractTier(StrEnum):
    TIER_1_ACADEMIC = "TIER_1_ACADEMIC"
    TIER_2_COMMUNITY = "TIER_2_COMMUNITY"
    TIER_3_GLOBAL = "TIER_3_GLOBAL"


class ContractStatus(StrEnum):
    ACTIVE = "ACTIVE"
    SUPERSEDED = "SUPERSEDED"
    DRAFT = "DRAFT"
    TERMINATED = "TERMINATED"


class PayableStatus(StrEnum):
    PENDING_SDV = "PENDING_SDV"
    TRIGGERED = "TRIGGERED"
    PENDING_APPROVAL = "PENDING_APPROVAL"
    APPROVED = "APPROVED"
    INVOICED = "INVOICED"
    PAID = "PAID"
    CANCELLED = "CANCELLED"


class PayeeRole(StrEnum):
    HOSPITAL = "HOSPITAL"
    PI_PRACTICE = "PI_PRACTICE"
    CENTRAL_PHARMACY = "CENTRAL_PHARMACY"
    LAB = "LAB"
    COORDINATOR = "COORDINATOR"
    INSTITUTION = "INSTITUTION"
    OTHER = "OTHER"


class TriggerType(StrEnum):
    ECRF_SUBMISSION = "ECRF_SUBMISSION"
    CRA_SDV_VERIFIED = "CRA_SDV_VERIFIED"
    MILESTONE_COMPLETED = "MILESTONE_COMPLETED"
    MANUAL_ADJUSTMENT = "MANUAL_ADJUSTMENT"


class HoldbackStatus(StrEnum):
    HELD = "HELD"
    PENDING_RELEASE = "PENDING_RELEASE"
    RELEASED = "RELEASED"
    FORFEITED = "FORFEITED"


class AuditAction(StrEnum):
    CONTRACT_CREATED = "CONTRACT_CREATED"
    CONTRACT_UPDATED = "CONTRACT_UPDATED"
    FEE_SCHEDULE_ADDED = "FEE_SCHEDULE_ADDED"
    SPLIT_PAYEE_CONFIGURED = "SPLIT_PAYEE_CONFIGURED"
    PAYABLE_TRIGGERED_ECRF = "PAYABLE_TRIGGERED_ECRF"
    PAYABLE_GATED_PENDING_SDV = "PAYABLE_GATED_PENDING_SDV"
    SDV_VERIFIED_UNLOCKED = "SDV_VERIFIED_UNLOCKED"
    PAYABLE_APPROVED = "PAYABLE_APPROVED"
    SPLIT_ALLOCATED = "SPLIT_ALLOCATED"
    HOLDBACK_RESERVED = "HOLDBACK_RESERVED"
    HOLDBACK_RELEASED = "HOLDBACK_RELEASED"


class SplitAllocationCalculation(BaseModel):
    """Domain model representing a single calculated split payee share."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    payee_id: str
    payee_name: str
    payee_role: str
    allocation_percentage: float
    gross_share: float
    withholding_share: float
    net_share: float
    currency: str = "USD"


class VisitCalculationBreakdown(BaseModel):
    """Domain calculation breakdown for a procedure/visit payable."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    visit_code: str
    procedure_code: str
    procedure_name: str
    base_amount: float
    overhead_percentage: float
    overhead_amount: float
    patient_stipend: float = 0.0
    travel_reimbursement: float = 0.0
    gross_amount: float
    withholding_rate: float
    withholding_amount: float
    net_amount: float
    currency: str = "USD"
    requires_sdv: bool = False
    is_sdv_gated: bool = False
    allocations: list[SplitAllocationCalculation] = Field(default_factory=list)
    contract_id: str
    version_index: int = 1
    metadata: dict[str, Any] = Field(default_factory=dict)
