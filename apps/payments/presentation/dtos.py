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


# ==========================================
# 7. Self-Billing Invoicing & Remittance Advice
# ==========================================


class BillingCycleCompilationRequest(BaseModel):
    """Request payload for compiling monthly/quarterly billing cycle into draft self-billing invoice."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str = Field(..., description="Target Study Protocol UUID or Identifier")
    site_id: str = Field(..., description="Target Trial Site UUID or Identifier")
    billing_cycle: str = Field(
        default="MONTHLY", description="Billing cycle type (MONTHLY, QUARTERLY, MANUAL)"
    )
    period_start: str = Field(..., description="Billing cycle start date (YYYY-MM-DD)")
    period_end: str = Field(..., description="Billing cycle end date (YYYY-MM-DD)")
    auto_approve: bool = Field(
        default=False, description="Automatically approve invoice upon compilation"
    )


class InvoiceLineItemResponse(BaseModel):
    """Response model for an itemized procedure line item on an invoice."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    invoice_id: str
    payable_id: str | None
    subject_id: str
    visit_code: str
    procedure_code: str | None
    procedure_name: str | None
    base_amount: float
    overhead_amount: float
    patient_stipend: float
    travel_reimbursement: float
    gross_amount: float
    withholding_rate: float
    withholding_amount: float
    net_amount: float
    currency: str
    created_at: datetime
    version_index: int


class RemittanceAdviceResponse(BaseModel):
    """Response payload for cryptographically sealed PDF remittance advice statement."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    remittance_number: str
    invoice_id: str
    study_id: str
    site_id: str
    currency: str
    gross_amount: float
    overhead_amount: float
    holdback_amount: float
    net_amount: float
    pdf_sha256_hash: str
    is_sealed: bool
    is_signed: bool
    signed_by: str | None
    signed_at: datetime | None
    signer_role: str | None
    signature_manifest: dict[str, Any] | None
    dispatched_to: str | None
    dispatched_at: datetime | None
    dispatch_status: str
    created_at: datetime
    version_index: int


class SelfBillingInvoiceResponse(BaseModel):
    """Response payload for a compiled self-billing invoice statement."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    invoice_number: str
    study_id: str
    site_id: str
    contract_id: str
    billing_cycle: str
    period_start: str
    period_end: str
    currency: str
    total_base_amount: float
    total_overhead_amount: float
    total_patient_stipend: float
    total_travel_reimbursement: float
    total_gross_amount: float
    total_holdback_amount: float
    total_net_amount: float
    payable_count: int
    status: str
    approved_by: str | None
    approved_at: datetime | None
    erp_system: str | None
    erp_reference_id: str | None
    erp_sync_status: str
    erp_synced_at: datetime | None
    reconciled_by: str | None
    reconciled_at: datetime | None
    reconciliation_reference: str | None
    reconciliation_notes: str | None
    created_at: datetime
    created_by: str
    reason_for_change: str | None
    version_index: int
    line_items: list[InvoiceLineItemResponse] = Field(default_factory=list)
    remittance_advice: RemittanceAdviceResponse | None = None


class RemittanceAdviceSignRequest(BaseModel):
    """Request payload for applying 21 CFR Part 11 digital signature to a remittance advice document."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    signer_name: str = Field(..., description="Authorized Grants Lead Name")
    signer_role: str = Field(
        default="Grants Lead", description="Signer corporate or clinical title"
    )
    signature_reason: str = Field(
        default="Authorized Self-Billing Remittance Disbursement Statement",
        description="21 CFR Part 11 Electronic Signature manifestation statement",
    )
    cert_pem: str | None = Field(
        default=None, description="Optional custom X.509 Certificate PEM"
    )
    private_key_pem: str | None = Field(
        default=None, description="Optional custom RSA Private Key PEM"
    )


class RemittanceAdviceDispatchRequest(BaseModel):
    """Request payload for dispatching signed remittance advice statement via automated email."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    recipient_email: str = Field(
        ..., description="Site financial contact destination email address"
    )


class RemittanceSignatureVerificationResponse(BaseModel):
    """Response payload for cryptographic verification of a remittance advice statement."""

    model_config = ConfigDict(from_attributes=True)

    is_valid: bool
    status: str
    failure_reason: str | None = None
    pdf_sha256_hash: str
    signed_by: str | None = None
    signed_at: datetime | None = None
    signer_role: str | None = None


# ==========================================
# 8. Enterprise ERP Synchronization (SAP/NetSuite)
# ==========================================


class ErpExportResponse(BaseModel):
    """Standard enterprise ERP export payload schema."""

    model_config = ConfigDict(from_attributes=True)

    erp_system: str
    invoice_number: str
    document_type: str = "SELF_BILLING_INVOICE"
    payload: dict[str, Any]
    export_timestamp: str


class ErpWebhookPayload(BaseModel):
    """Inbound enterprise ERP webhook payload from SAP / NetSuite."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    erp_system: str = Field(
        default="SAP",
        description="Source ERP system (SAP, NETSUITE, ORACLE_FINANCIALS)",
    )
    invoice_number: str | None = Field(
        default=None, description="Cadence invoice number"
    )
    invoice_id: str | None = Field(default=None, description="Cadence invoice UUID")
    erp_reference_id: str = Field(
        ..., description="ERP Document Number / Transaction ID"
    )
    status: str = Field(
        ..., description="ERP execution status (PENDING, APPROVED, PAID, RECONCILED)"
    )
    payment_date: str | None = Field(
        default=None, description="Bank settlement date (YYYY-MM-DD)"
    )
    amount_paid: float | None = Field(
        default=None, ge=0.0, description="Amount disbursed by ERP"
    )
    currency: str = Field(default="USD", description="Currency of payment")
    bank_reference: str | None = Field(
        default=None, description="Bank wire reference or clearing check number"
    )
    notes: str | None = Field(
        default=None, description="ERP reconciliation processing notes"
    )


class ErpWebhookResponse(BaseModel):
    """Response acknowledging ERP webhook processing and status transition."""

    model_config = ConfigDict(from_attributes=True)

    status: str
    invoice_id: str
    invoice_number: str
    erp_system: str
    erp_reference_id: str
    previous_status: str
    new_status: str
    reconciled: bool
    message: str


class PaymentReconciliationRequest(BaseModel):
    """Formal request payload for reconciling bank disbursement against invoice."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    payment_reference: str = Field(
        ..., description="Bank Wire / Check / ERP Settlement Document Number"
    )
    erp_system: str = Field(
        default="SAP", description="ERP system identifier (SAP, NETSUITE, etc.)"
    )
    payment_date: str | None = Field(
        default=None, description="Payment execution date (YYYY-MM-DD)"
    )
    amount_paid: float = Field(..., ge=0.0, description="Actual disbursed net amount")
    notes: str | None = Field(
        default=None, description="Financial reconciliation notes"
    )


class PaymentReconciliationResponse(BaseModel):
    """Summary response for completed payment reconciliation."""

    model_config = ConfigDict(from_attributes=True)

    invoice_id: str
    invoice_number: str
    status: str
    erp_sync_status: str
    reconciled_by: str
    reconciled_at: datetime
    reconciliation_reference: str
    total_net_amount: float
    amount_paid: float
    reconciled_payables_count: int


class ErpSyncLogResponse(BaseModel):
    """Response payload for ERP synchronization transaction log."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    invoice_id: str
    erp_system: str
    sync_direction: str
    status: str
    payload: dict[str, Any] | None
    response_data: dict[str, Any] | None
    external_reference: str | None
    reconciled: bool
    created_at: datetime
    created_by: str
    version_index: int
