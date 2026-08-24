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


# ==========================================
# 11. Participant Financial Wallet DTOs
# ==========================================


class ParticipantWalletCreate(BaseModel):
    """Request payload to initialize or provision a participant financial wallet."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    subject_id: str = Field(..., description="Subject unique identifier / MRN")
    study_id: str = Field(..., description="Study Protocol ID")
    site_id: str = Field(..., description="Site ID")
    currency: str = Field(default="USD", description="Currency ISO code")
    card_type: str = Field(
        default="RELOADABLE_DEBIT", description="Card type (RELOADABLE_DEBIT, VIRTUAL)"
    )
    virtual_card_token: str | None = Field(
        default=None, description="Virtual card token"
    )
    virtual_card_mask: str | None = Field(
        default=None, description="Masked virtual card display number"
    )
    virtual_card_expiry: str | None = Field(
        default=None, description="Virtual card expiry MM/YY"
    )
    anti_coercion_cap: float | None = Field(
        default=None, ge=0.0, description="Ethics/IRB anti-coercion cap limit"
    )
    ach_routing_number: str | None = Field(
        default=None, description="Bank routing transit number"
    )
    ach_account_mask: str | None = Field(
        default=None, description="Masked bank account number"
    )
    ach_account_holder: str | None = Field(
        default=None, description="Account holder name"
    )


class ParticipantWalletPayoutMethodsUpdate(BaseModel):
    """Request payload to update payout preference credentials on a participant wallet."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    virtual_card_token: str | None = Field(default=None)
    virtual_card_mask: str | None = Field(default=None)
    virtual_card_expiry: str | None = Field(default=None)
    card_status: str | None = Field(default=None)
    ach_routing_number: str | None = Field(default=None)
    ach_account_mask: str | None = Field(default=None)
    ach_account_holder: str | None = Field(default=None)


class ParticipantWalletResponse(BaseModel):
    """Response payload for subject wallet details and balance summary."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    subject_id: str
    study_id: str
    site_id: str
    current_balance: float
    lifetime_stipends: float
    lifetime_reimbursements: float
    lifetime_disbursements: float
    currency: str
    status: str
    taxable_ytd: float = 0.0
    is_1099_threshold_exceeded: bool = False
    w9_status: str = "NOT_REQUIRED"
    w9_submitted_at: datetime | None = None
    w9_verified_at: datetime | None = None
    tax_id_last_four: str | None = None
    card_type: str = "RELOADABLE_DEBIT"
    card_status: str = "ACTIVE"
    virtual_card_token: str | None = None
    virtual_card_mask: str | None = None
    virtual_card_expiry: str | None = None
    anti_coercion_cap: float | None = None
    ach_routing_number: str | None = None
    ach_account_mask: str | None = None
    ach_account_holder: str | None = None
    created_at: datetime
    created_by: str
    updated_at: datetime
    version_index: int
    pending_claims_count: int = 0
    total_claims_count: int = 0
    disbursements_count: int = 0


class IssueReloadableCardRequest(BaseModel):
    """Request payload for automated reloadable debit card issuing."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    cardholder_name: str | None = Field(default=None, description="Name on card")
    card_type: str = Field(
        default="RELOADABLE_DEBIT", description="Card type (RELOADABLE_DEBIT, VIRTUAL)"
    )
    initial_load: float = Field(
        default=0.0, ge=0.0, description="Initial funds loaded onto the card"
    )


class IssueReloadableCardResponse(BaseModel):
    """Response payload for issued reloadable debit card."""

    model_config = ConfigDict(from_attributes=True)

    wallet_id: str
    subject_id: str
    card_token: str
    card_mask: str
    card_expiry: str
    card_type: str
    card_status: str
    cardholder_name: str
    current_balance: float
    issued_at: datetime


class W9SubmitRequest(BaseModel):
    """Request payload for IRS Form W-9 certification submission."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    tax_id_last_four: str = Field(
        ..., min_length=4, max_length=4, description="Last 4 digits of SSN/ITIN"
    )
    full_legal_name: str = Field(..., description="Full legal taxpayer name")
    certify_accurate: bool = Field(
        default=True, description="Taxpayer certification of accurate TIN"
    )
    address: str | None = Field(default=None, description="Taxpayer mailing address")


class W9SubmitResponse(BaseModel):
    """Response acknowledging W-9 certification submission."""

    model_config = ConfigDict(from_attributes=True)

    wallet_id: str
    subject_id: str
    w9_status: str
    tax_id_last_four: str
    submitted_at: datetime
    message: str


class W9VerifyRequest(BaseModel):
    """Request payload for coordinator verification of submitted W-9 form."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    notes: str | None = Field(
        default=None, description="Coordinator W-9 verification notes"
    )


class Tax1099ThresholdResponse(BaseModel):
    """Real-time IRS Form 1099 tax threshold analysis and W-9 validation state."""

    model_config = ConfigDict(from_attributes=True)

    wallet_id: str
    subject_id: str
    study_id: str
    taxable_ytd: float
    threshold_limit: float = 600.0
    threshold_percentage: float
    is_threshold_exceeded: bool
    w9_status: str
    w9_required: bool
    warning_level: str  # NORMAL, APPROACHING, EXCEEDED
    currency: str = "USD"


class BalanceInquiryResponse(BaseModel):
    """Real-time multi-channel balance inquiry response."""

    model_config = ConfigDict(from_attributes=True)

    wallet_id: str
    subject_id: str
    study_id: str
    site_id: str
    current_balance: float
    available_balance: float
    pending_claims_total: float
    lifetime_stipends: float
    lifetime_reimbursements: float
    lifetime_disbursements: float
    taxable_ytd: float
    currency: str
    virtual_card_mask: str | None
    card_status: str
    card_type: str
    ach_account_mask: str | None
    is_1099_threshold_exceeded: bool
    w9_status: str


# ==========================================
# 12. SoA Protocol Stipend Rules DTOs
# ==========================================


class ParticipantStipendRuleCreate(BaseModel):
    """Request payload to configure SoA protocol visit stipend compensation."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str = Field(..., description="Study Protocol ID")
    visit_code: str = Field(..., description="Schedule of Activities visit code")
    visit_name: str = Field(..., description="Descriptive visit name")
    procedure_code: str | None = Field(
        default=None, description="Optional procedure identifier"
    )
    amount: float = Field(..., ge=0.0, description="Stipend compensation amount")
    currency: str = Field(default="USD", description="Currency ISO code")
    requires_verification: bool = Field(
        default=True,
        description="Whether clinic visit completion must be verified in EDC",
    )
    taxable: bool = Field(default=False, description="Whether stipend is taxable")
    anti_coercion_cap: float | None = Field(
        default=None, ge=0.0, description="Ethics/IRB anti-coercion cap limit"
    )
    is_milestone: bool = Field(
        default=False, description="Whether visit is a major study milestone"
    )
    milestone_code: str | None = Field(
        default=None, description="Milestone code (e.g. MILESTONE_SCREENING)"
    )


class ParticipantStipendRuleResponse(BaseModel):
    """Response payload for configured visit stipend rule."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    visit_code: str
    visit_name: str
    procedure_code: str | None
    amount: float
    currency: str
    requires_verification: bool
    taxable: bool
    anti_coercion_cap: float | None = None
    is_milestone: bool = False
    milestone_code: str | None = None
    is_active: bool
    created_at: datetime
    created_by: str
    version_index: int


class VisitVerificationEventDto(BaseModel):
    """Inbound EDC visit verification trigger event to credit participant stipend."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str = Field(..., description="Study Protocol ID")
    site_id: str = Field(..., description="Site ID")
    subject_id: str = Field(..., description="Subject identifier")
    visit_code: str = Field(..., description="Completed visit code")
    event_id: str = Field(..., description="Unique EDC trigger event identifier")
    verified_by: str = Field(
        default="crc_coordinator", description="User identity confirming visit"
    )
    verification_timestamp: str | None = Field(
        default=None, description="ISO timestamp of visit completion"
    )


# ==========================================
# 13. Mobile Travel Expense & Receipt OCR DTOs
# ==========================================


class ReceiptOcrExtractRequest(BaseModel):
    """Request payload to run OCR expense extraction on a receipt."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    image_base64: str | None = Field(
        default=None, description="Base64 encoded receipt image"
    )
    raw_text: str | None = Field(default=None, description="Raw OCR text fallback")
    category_hint: str | None = Field(
        default=None, description="Optional category hint"
    )
    filename: str | None = Field(default=None, description="Uploaded file name")


class ReceiptOcrExtractResponse(BaseModel):
    """Extracted receipt OCR structured metadata."""

    model_config = ConfigDict(from_attributes=True)

    merchant_name: str
    expense_date: str
    gross_amount: float
    tax_deduction: float
    net_amount: float
    category: str
    confidence_score: float
    detected_keywords: list[str]
    raw_text: str


class ExpenseClaimSubmitRequest(BaseModel):
    """Request payload for participant submitting a travel/meal expense reimbursement claim."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    wallet_id: str | None = Field(
        default=None,
        description="Participant wallet ID (optional if subject_id supplied)",
    )
    subject_id: str = Field(..., description="Subject unique identifier")
    study_id: str = Field(..., description="Study ID")
    site_id: str = Field(..., description="Site ID")
    expense_date: str = Field(..., description="Expense date (YYYY-MM-DD)")
    category: str = Field(
        default="MILEAGE",
        description="Expense category (MILEAGE, PARKING, MEAL, LODGING, TRANSIT, OTHER)",
    )
    merchant_name: str = Field(..., description="Merchant or vendor name")
    gross_amount: float = Field(..., gt=0.0, description="Gross receipt expense amount")
    tax_deduction: float = Field(
        default=0.0, ge=0.0, description="Calculated tax deduction / withholding"
    )
    net_amount: float | None = Field(
        default=None, description="Net reimbursement amount (calculated if omitted)"
    )
    currency: str = Field(default="USD", description="Currency ISO code")
    is_meal_allowance: bool = Field(
        default=False, description="Whether claim is a per diem meal allowance"
    )
    per_diem_cap: float | None = Field(
        default=None, ge=0.0, description="Optional per diem meal cap limit"
    )
    receipt_image_name: str | None = Field(
        default=None, description="Original receipt filename"
    )
    receipt_image_base64: str | None = Field(
        default=None, description="Base64 encoded receipt image or preview"
    )
    receipt_ocr_data: dict[str, Any] | None = Field(
        default=None, description="Raw/extracted OCR metadata"
    )


class ExpenseClaimReviewRequest(BaseModel):
    """Request payload for coordinator 1-click review/approval of expense claim."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    status: str = Field(..., description="Review outcome (APPROVED or REJECTED)")
    review_notes: str | None = Field(
        default=None, description="Coordinator review comments"
    )
    adjusted_amount: float | None = Field(
        default=None, description="Optional adjusted gross amount"
    )
    adjusted_category: str | None = Field(
        default=None, description="Optional corrected category"
    )
    adjusted_merchant: str | None = Field(
        default=None, description="Optional corrected merchant name"
    )


class ExpenseClaimResponse(BaseModel):
    """Response payload for submitted expense claim."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    wallet_id: str
    subject_id: str
    study_id: str
    site_id: str
    expense_date: str
    category: str
    merchant_name: str
    gross_amount: float
    tax_deduction: float
    net_amount: float
    currency: str
    is_meal_allowance: bool = False
    per_diem_cap: float | None = None
    receipt_image_name: str | None
    receipt_ocr_data: dict[str, Any] | None
    status: str
    reviewed_by: str | None
    reviewed_at: datetime | None
    review_notes: str | None
    created_at: datetime
    created_by: str
    version_index: int


# ==========================================
# 14. Digital Disbursement & Transaction DTOs
# ==========================================


class DisbursementRequest(BaseModel):
    """Request payload to dispatch digital payout from wallet to Virtual Card or ACH."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    disbursement_type: str = Field(
        default="VIRTUAL_CARD", description="Payout method (VIRTUAL_CARD or DIRECT_ACH)"
    )
    amount: float | None = Field(
        default=None,
        gt=0.0,
        description="Amount to disburse (defaults to full available balance)",
    )
    destination_mask: str | None = Field(
        default=None, description="Custom masked card / bank account display descriptor"
    )


class DisbursementResponse(BaseModel):
    """Response payload for digital disbursement dispatch."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    wallet_id: str
    subject_id: str
    study_id: str
    disbursement_type: str
    amount: float
    currency: str
    status: str
    reference_number: str
    destination_mask: str
    dispatched_at: datetime
    settled_at: datetime | None
    error_message: str | None
    created_at: datetime
    created_by: str
    version_index: int


class ParticipantTransactionResponse(BaseModel):
    """Response payload for ledger transaction record in participant wallet."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    wallet_id: str
    subject_id: str
    study_id: str
    transaction_type: str
    amount: float
    balance_after: float
    reference_id: str
    description: str
    created_at: datetime
    created_by: str
    version_index: int


# ==========================================
# 15. Plain Language Lay Summary Portal DTOs
# ==========================================


class LaySummaryCreate(BaseModel):
    """Request payload to draft/create an EU CTR 536/2014 Plain Language Lay Summary."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    study_id: str = Field(..., description="Study Protocol ID")
    protocol_id: str | None = Field(default=None, description="Protocol Code")
    title: str = Field(..., description="Lay Summary title")
    trial_phase: str = Field(default="Phase III", description="Clinical trial phase")
    therapeutic_area: str = Field(..., description="Therapeutic indication")
    sponsor_name: str = Field(..., description="Sponsor organization")
    summary_text: str = Field(..., description="Plain-language narrative overview")
    plain_language_sections: dict[str, Any] | None = Field(
        default=None,
        description="Structured patient sections (objective, treatments, results, side_effects)",
    )
    irb_approval_number: str = Field(
        ..., description="IRB/Ethics Committee approval number"
    )
    irb_approval_date: str = Field(..., description="IRB approval date (YYYY-MM-DD)")
    language: str = Field(default="en", description="Language code (en, es, de, fr)")
    status: str = Field(
        default="PUBLISHED",
        description="Publication status (DRAFT, IRB_APPROVED, PUBLISHED)",
    )


class LaySummaryPublishRequest(BaseModel):
    """Request payload to approve and publish a plain language lay summary."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    irb_approval_number: str | None = Field(
        default=None, description="Confirmed IRB approval identifier"
    )
    irb_approval_date: str | None = Field(
        default=None, description="Confirmed IRB approval date"
    )


class LaySummaryResponse(BaseModel):
    """Response payload for EU CTR Plain Language Lay Summary."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    protocol_id: str | None
    title: str
    trial_phase: str
    therapeutic_area: str
    sponsor_name: str
    summary_text: str
    plain_language_sections: dict[str, Any] | None
    irb_approval_number: str
    irb_approval_date: str
    language: str
    status: str
    published_at: datetime | None
    created_at: datetime
    created_by: str
    version_index: int
    receipts_count: int = 0


class LaySummaryReceiptConfirmRequest(BaseModel):
    """Request payload for participant acknowledging receipt and comprehension of lay summary."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    subject_id: str = Field(..., description="Participant subject ID")
    study_id: str = Field(..., description="Study ID")
    comprehension_score: int = Field(
        default=5,
        ge=1,
        le=5,
        description="Comprehension rating (1=Very Difficult to 5=Very Clear)",
    )
    comprehension_feedback: str | None = Field(
        default=None, description="Participant feedback or comments on the summary"
    )
    confirmation_statement: str = Field(
        default="I confirm I have received and reviewed the plain language summary of trial results.",
        description="Formal acknowledgment statement",
    )


class LaySummaryReceiptResponse(BaseModel):
    """Cryptographically sealed confirmation receipt certificate for Lay Summary."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    summary_id: str
    subject_id: str
    study_id: str
    comprehension_score: int
    comprehension_feedback: str | None
    confirmation_statement: str
    receipt_hash: str
    signer_identity: str
    confirmed_at: datetime
    status: str = "CONFIRMED"
