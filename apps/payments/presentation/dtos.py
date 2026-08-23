"""Pydantic DTOs for Site Payments request and response contracts.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

# ==========================================
# 1. Budget Contracts
# ==========================================


class BudgetContractCreate(BaseModel):
    """Request payload for creating or versioning an investigator site budget contract."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str = Field(..., description="Target Study Protocol UUID or Identifier")
    site_id: str = Field(..., description="Target Trial Site UUID or Identifier")
    contract_name: str = Field(..., description="Descriptive contract schedule title")
    tier_level: str = Field(
        default="TIER_1_ACADEMIC",
        description="Contract tier level (TIER_1_ACADEMIC, TIER_2_COMMUNITY, TIER_3_GLOBAL)",
    )
    currency: str = Field(default="USD", description="ISO-4217 Currency code")
    total_budget: float = Field(
        default=0.0, ge=0.0, description="Total contracted grant budget cap"
    )
    overhead_percentage: float = Field(
        default=0.0,
        ge=0.0,
        le=100.0,
        description="Default institutional overhead percentage",
    )
    withholding_percentage: float = Field(
        default=10.0,
        ge=0.0,
        le=100.0,
        description="Default holdback retention reserve percentage",
    )
    effective_date: str | None = Field(
        default=None, description="Contract effective execution date"
    )


class BudgetContractResponse(BaseModel):
    """Response payload for budget contract details."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str
    contract_name: str
    tier_level: str
    currency: str
    total_budget: float
    overhead_percentage: float
    withholding_percentage: float
    effective_date: str | None
    status: str
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str | None
    version_index: int
    fee_schedules_count: int = 0
    split_payees_count: int = 0


# ==========================================
# 2. Fee Schedules
# ==========================================


class FeeScheduleCreate(BaseModel):
    """Request payload for adding procedure fee schedule items to a contract."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    visit_code: str = Field(
        ..., description="Visit name or identifier (e.g. SCREENING, CYCLE_1_DAY_1)"
    )
    activity_id: str | None = Field(
        default=None, description="CDISC USDM Activity UUID"
    )
    form_id: str | None = Field(default=None, description="Linked eCRF Form UUID or ID")
    procedure_code: str = Field(..., description="Procedure code (e.g. PROC-ECG-01)")
    procedure_name: str = Field(..., description="Human readable procedure title")
    base_amount: float = Field(..., ge=0.0, description="Base reimbursement fee")
    overhead_percentage: float | None = Field(
        default=None,
        ge=0.0,
        le=100.0,
        description="Override institutional overhead % (inherits from contract if omitted)",
    )
    withholding_percentage: float | None = Field(
        default=None,
        ge=0.0,
        le=100.0,
        description="Override withholding holdback % (inherits from contract if omitted)",
    )
    patient_stipend: float = Field(
        default=0.0, ge=0.0, description="Patient reimbursement stipend"
    )
    travel_reimbursement: float = Field(
        default=0.0, ge=0.0, description="Travel reimbursement allowance"
    )
    requires_sdv: bool = Field(
        default=False, description="True if CRA SDV verification gates payment"
    )


class FeeScheduleResponse(BaseModel):
    """Response payload for fee schedule line item."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    contract_id: str
    visit_code: str
    activity_id: str | None
    form_id: str | None
    procedure_code: str
    procedure_name: str
    base_amount: float
    overhead_percentage: float
    withholding_percentage: float
    patient_stipend: float
    travel_reimbursement: float
    requires_sdv: bool
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str | None
    version_index: int


# ==========================================
# 3. Split Payees
# ==========================================


class SplitPayeeConfigItem(BaseModel):
    """Configuration for a single recipient payee share."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    payee_id: str = Field(
        ..., description="Payee unique identifier (e.g. PAYEE-HOSP-01)"
    )
    payee_name: str = Field(
        ..., description="Payee legal name (e.g. Hospital Research Foundation)"
    )
    payee_role: str = Field(
        ..., description="Role/type: HOSPITAL, PI_PRACTICE, CENTRAL_PHARMACY, LAB, etc."
    )
    allocation_percentage: float = Field(
        ..., gt=0.0, le=100.0, description="Percentage of payable allocated to payee"
    )
    bank_account_reference: str | None = Field(
        default=None, description="Bank routing/account token"
    )
    tax_identifier: str | None = Field(default=None, description="Tax ID / EIN")


class SplitPayeeConfigBulkUpdate(BaseModel):
    """Bulk update payload for contract split payee allocations (must sum to 100%)."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    payees: list[SplitPayeeConfigItem] = Field(
        ..., min_length=1, description="List of recipient payees (must sum to 100%)"
    )


class SplitPayeeConfigResponse(BaseModel):
    """Response payload for split payee configuration."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    contract_id: str
    payee_id: str
    payee_name: str
    payee_role: str
    allocation_percentage: float
    bank_account_reference: str | None
    tax_identifier: str | None
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str | None
    version_index: int


# ==========================================
# 4. Real-Time Ingestion Event Contracts
# ==========================================


class EcrfSubmissionEventDto(BaseModel):
    """Clinical event emitted when an eCRF form is completed/submitted in EDC."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str
    site_id: str
    subject_id: str
    visit_code: str
    form_id: str
    event_id: str = Field(
        ..., description="Unique event UUID for deterministic idempotency"
    )
    timestamp: datetime | None = None
    actor_id: str = "edc_system"


class CraSdvVerificationEventDto(BaseModel):
    """Clinical event emitted when a CRA verifies/signs off Source Data Verification (SDV)."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str
    site_id: str
    subject_id: str
    visit_code: str
    form_id: str | None = None
    verified_by: str = Field(..., description="CRA User ID performing the verification")
    event_id: str = Field(
        ..., description="Unique event UUID for deterministic idempotency"
    )
    timestamp: datetime | None = None


class MilestoneCompletionEventDto(BaseModel):
    """Clinical event emitted when a study/site milestone is verified."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str
    site_id: str
    milestone_code: str
    milestone_name: str
    amount: float = Field(..., ge=0.0)
    verified_by: str
    event_id: str = Field(
        ..., description="Unique event UUID for deterministic idempotency"
    )
    timestamp: datetime | None = None


class BatchEventIngestDto(BaseModel):
    """Batch ingestion container for clinical financial events."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    ecrf_submissions: list[EcrfSubmissionEventDto] = Field(default_factory=list)
    sdv_verifications: list[CraSdvVerificationEventDto] = Field(default_factory=list)
    milestones: list[MilestoneCompletionEventDto] = Field(default_factory=list)


# ==========================================
# 5. Payables, Allocations & Holdbacks
# ==========================================


class PayableAllocationResponse(BaseModel):
    """Response payload for an allocated split payee share."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    payable_id: str
    payee_id: str
    payee_name: str
    payee_role: str
    allocation_percentage: float
    gross_share: float
    withholding_share: float
    net_share: float
    currency: str
    status: str
    created_at: datetime
    version_index: int


class HoldbackReserveResponse(BaseModel):
    """Response payload for 10% holdback retention reserve details."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str
    contract_id: str
    payable_id: str
    holdback_amount: float
    currency: str
    status: str
    release_authorized_by: str | None
    release_authorized_at: datetime | None
    dm_signoff_by: str | None
    dm_signoff_at: datetime | None
    quality_signoff_by: str | None
    quality_signoff_at: datetime | None
    release_notes: str | None
    created_at: datetime
    version_index: int


class PayableResponse(BaseModel):
    """Response payload for a site payable line item with full split allocation breakdown."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str
    contract_id: str
    subject_id: str
    visit_code: str
    form_id: str | None
    procedure_code: str | None
    procedure_name: str | None
    trigger_type: str
    trigger_event_id: str
    trigger_source: str
    base_amount: float
    overhead_amount: float
    patient_stipend_amount: float
    travel_reimbursement_amount: float
    gross_amount: float
    withholding_rate: float
    withholding_amount: float
    net_amount: float
    currency: str
    status: str
    is_sdv_gated: bool
    sdv_verified_at: datetime | None
    sdv_verified_by: str | None
    created_at: datetime
    created_by: str
    reason_for_change: str | None
    version_index: int
    allocations: list[PayableAllocationResponse] = Field(default_factory=list)
    holdback_reserve: HoldbackReserveResponse | None = None


class HoldbackReleaseRequest(BaseModel):
    """Formal request payload to release withholding holdback reserves with dual DM and Quality sign-off."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str
    site_id: str
    release_reason: str = Field(
        ...,
        description="Clinical justification (e.g. Study closeout & query resolution)",
    )
    dm_signoff_by: str = Field(
        ..., description="Data Management Lead electronic signature ID"
    )
    quality_signoff_by: str = Field(
        ..., description="Quality Oversight electronic signature ID"
    )
    holdback_ids: list[str] | None = Field(
        default=None,
        description="Optional specific holdback IDs to release (releases all site reserves if omitted)",
    )


class HoldbackReleaseResponse(BaseModel):
    """Summary response for released holdback retention reserves."""

    model_config = ConfigDict(from_attributes=True)

    study_id: str
    site_id: str
    total_released_amount: float
    currency: str
    released_count: int
    dm_signoff_by: str
    quality_signoff_by: str
    status: str
    released_reserves: list[HoldbackReserveResponse] = Field(default_factory=list)


# ==========================================
# 6. Audit Trail
# ==========================================


class PaymentAuditLogResponse(BaseModel):
    """Response model for immutable 21 CFR Part 11 payment audit records."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    entity_type: str
    entity_id: str
    action: str
    previous_state: dict[str, Any] | None
    new_state: dict[str, Any] | None
    details: str | None
    user_id: str
    user_roles: str
    created_at: datetime
    created_by: str
    reason_for_change: str | None
    version_index: int
