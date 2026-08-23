"""Domain entity models and enumerations for Site Payments.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-004, PRD-PAY-006
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


class BillingCycle(StrEnum):
    MONTHLY = "MONTHLY"
    QUARTERLY = "QUARTERLY"
    MANUAL = "MANUAL"


class InvoiceStatus(StrEnum):
    DRAFT = "DRAFT"
    APPROVED = "APPROVED"
    PAID = "PAID"
    RECONCILED = "RECONCILED"
    CANCELLED = "CANCELLED"


class ErpSyncStatus(StrEnum):
    PENDING = "PENDING"
    SYNCED = "SYNCED"
    FAILED = "FAILED"
    RECONCILED = "RECONCILED"


class ErpSystem(StrEnum):
    SAP = "SAP"
    NETSUITE = "NETSUITE"
    ORACLE_FINANCIALS = "ORACLE_FINANCIALS"
    GENERIC = "GENERIC"


class RemittanceDispatchStatus(StrEnum):
    PENDING = "PENDING"
    DISPATCHED = "DISPATCHED"
    DELIVERED = "DELIVERED"
    FAILED = "FAILED"


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
    INVOICE_COMPILED = "INVOICE_COMPILED"
    INVOICE_APPROVED = "INVOICE_APPROVED"
    INVOICE_PAID = "INVOICE_PAID"
    INVOICE_RECONCILED = "INVOICE_RECONCILED"
    REMITTANCE_ADVICE_GENERATED = "REMITTANCE_ADVICE_GENERATED"
    REMITTANCE_ADVICE_SIGNED = "REMITTANCE_ADVICE_SIGNED"
    REMITTANCE_ADVICE_DISPATCHED = "REMITTANCE_ADVICE_DISPATCHED"
    ERP_PAYMENT_SYNCED = "ERP_PAYMENT_SYNCED"
    ERP_WEBHOOK_PROCESSED = "ERP_WEBHOOK_PROCESSED"


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


class RemittancePayeeBreakdown(BaseModel):
    """Domain model representing aggregated payee totals in remittance advice."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    payee_id: str
    payee_name: str
    payee_role: str
    allocation_percentage: float
    total_gross: float
    total_withholding: float
    total_net: float
    currency: str = "USD"
    bank_account_reference: str | None = None
    tax_identifier: str | None = None


class InvoiceSummary(BaseModel):
    """Domain financial summary for a compiled self-billing invoice."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    total_base_amount: float
    total_overhead_amount: float
    total_patient_stipend: float
    total_travel_reimbursement: float
    total_gross_amount: float
    total_holdback_amount: float
    total_net_amount: float
    currency: str = "USD"
    payable_count: int
    payee_breakdowns: list[RemittancePayeeBreakdown] = Field(default_factory=list)


class ErpExportEnvelope(BaseModel):
    """Standard enterprise ERP export payload schema."""

    model_config = ConfigDict(from_attributes=True, extra="forbid")

    erp_system: str
    document_type: str = "SELF_BILLING_INVOICE"
    invoice_number: str
    study_id: str
    site_id: str
    billing_period: str
    currency: str
    total_amount: float
    net_amount: float
    holdback_amount: float
    payables_count: int
    line_items: list[dict[str, Any]]
    payee_distributions: list[dict[str, Any]]
    export_timestamp: str


# ==========================================
# Domain Entities
# ==========================================


class SplitPayeeConfigItem(BaseModel):
    """Configuration item for a split payee."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    payee_id: str
    payee_name: str
    payee_role: str
    allocation_percentage: float
    bank_account_reference: str | None = None
    tax_identifier: str | None = None


class ErpWebhookPayload(BaseModel):
    """Inbound enterprise ERP webhook payload from SAP / NetSuite."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    erp_system: str = "SAP"
    invoice_number: str | None = None
    invoice_id: str | None = None
    erp_reference_id: str
    status: str
    payment_date: str | None = None
    amount_paid: float | None = None
    currency: str = "USD"
    bank_reference: str | None = None
    notes: str | None = None


class BudgetContractModel(BaseModel):
    """Domain model for a site budget contract."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    study_id: str
    site_id: str
    contract_name: str
    tier_level: str = ContractTier.TIER_1_ACADEMIC.value
    currency: str = "USD"
    total_budget: float = 0.0
    overhead_percentage: float = 0.0
    withholding_percentage: float = 10.0
    effective_date: str | None = None
    status: str = ContractStatus.ACTIVE.value
    is_active: bool = True
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1
    fee_schedules: list[Any] = Field(default_factory=list)
    split_payees: list[Any] = Field(default_factory=list)


class FeeScheduleModel(BaseModel):
    """Domain model for a procedure fee schedule."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    contract_id: str
    visit_code: str
    activity_id: str | None = None
    form_id: str | None = None
    procedure_code: str | None = None
    procedure_name: str | None = None
    base_amount: float = 0.0
    overhead_percentage: float = 0.0
    withholding_percentage: float = 10.0
    patient_stipend: float = 0.0
    travel_reimbursement: float = 0.0
    requires_sdv: bool = False
    is_active: bool = True
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1


class SplitPayeeConfigModel(BaseModel):
    """Domain model for split payee configurations."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    contract_id: str
    payee_id: str
    payee_name: str
    payee_role: str = PayeeRole.HOSPITAL.value
    allocation_percentage: float
    bank_account_reference: str | None = None
    tax_identifier: str | None = None
    is_active: bool = True
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1


class PayableAllocationModel(BaseModel):
    """Domain model for a split allocation against a payable."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    payable_id: str
    payee_id: str
    payee_name: str
    payee_role: str
    allocation_percentage: float
    gross_share: float
    withholding_share: float
    net_share: float
    currency: str = "USD"
    status: str = PayableStatus.TRIGGERED.value
    created_at: Any | None = None
    version_index: int = 1


class HoldbackReserveModel(BaseModel):
    """Domain model for a 10% holdback retention reserve."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    study_id: str
    site_id: str
    contract_id: str
    payable_id: str
    holdback_amount: float
    currency: str = "USD"
    status: str = HoldbackStatus.HELD.value
    release_authorized_by: str | None = None
    release_authorized_at: Any | None = None
    dm_signoff_by: str | None = None
    dm_signoff_at: Any | None = None
    quality_signoff_by: str | None = None
    quality_signoff_at: Any | None = None
    release_notes: str | None = None
    created_at: Any | None = None
    version_index: int = 1


class PayableModel(BaseModel):
    """Domain model for an earned site payable."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    study_id: str
    site_id: str
    contract_id: str
    subject_id: str
    visit_code: str
    form_id: str | None = None
    procedure_code: str | None = None
    procedure_name: str | None = None
    trigger_type: str = TriggerType.ECRF_SUBMISSION.value
    trigger_event_id: str
    trigger_source: str = "EDC"
    base_amount: float = 0.0
    overhead_amount: float = 0.0
    patient_stipend_amount: float = 0.0
    travel_reimbursement_amount: float = 0.0
    gross_amount: float = 0.0
    withholding_rate: float = 10.0
    withholding_amount: float = 0.0
    net_amount: float = 0.0
    currency: str = "USD"
    status: str = PayableStatus.TRIGGERED.value
    is_sdv_gated: bool = False
    sdv_verified_at: Any | None = None
    sdv_verified_by: str | None = None
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1
    allocations: list[Any] = Field(default_factory=list)
    holdback_reserve: Any | None = None


class InvoiceLineItemModel(BaseModel):
    """Domain model for an itemized invoice line item."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    invoice_id: str
    payable_id: str | None = None
    subject_id: str
    visit_code: str
    procedure_code: str | None = None
    procedure_name: str | None = None
    base_amount: float = 0.0
    overhead_amount: float = 0.0
    patient_stipend: float = 0.0
    travel_reimbursement: float = 0.0
    gross_amount: float = 0.0
    withholding_rate: float = 10.0
    withholding_amount: float = 0.0
    net_amount: float = 0.0
    currency: str = "USD"
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1
    payable: Any | None = None


class RemittanceAdviceModel(BaseModel):
    """Domain model for a cryptographically sealed PDF remittance advice statement."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    remittance_number: str
    invoice_id: str
    study_id: str
    site_id: str
    currency: str = "USD"
    gross_amount: float = 0.0
    overhead_amount: float = 0.0
    holdback_amount: float = 0.0
    net_amount: float = 0.0
    pdf_sha256_hash: str
    pdf_content: str | None = None
    is_sealed: bool = True
    is_signed: bool = False
    signed_by: str | None = None
    signed_at: Any | None = None
    signer_role: str | None = None
    signature_manifest: dict[str, Any] | None = None
    dispatched_to: str | None = None
    dispatched_at: Any | None = None
    dispatch_status: str = RemittanceDispatchStatus.PENDING.value
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1


class SelfBillingInvoiceModel(BaseModel):
    """Domain model for a compiled self-billing invoice."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    invoice_number: str
    study_id: str
    site_id: str
    contract_id: str
    billing_cycle: str = BillingCycle.MONTHLY.value
    period_start: str
    period_end: str
    currency: str = "USD"
    total_base_amount: float = 0.0
    total_overhead_amount: float = 0.0
    total_patient_stipend: float = 0.0
    total_travel_reimbursement: float = 0.0
    total_gross_amount: float = 0.0
    total_holdback_amount: float = 0.0
    total_net_amount: float = 0.0
    payable_count: int = 0
    status: str = InvoiceStatus.DRAFT.value
    approved_by: str | None = None
    approved_at: Any | None = None
    erp_system: str | None = None
    erp_reference_id: str | None = None
    erp_sync_status: str = ErpSyncStatus.PENDING.value
    erp_synced_at: Any | None = None
    reconciled_by: str | None = None
    reconciled_at: Any | None = None
    reconciliation_reference: str | None = None
    reconciliation_notes: str | None = None
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1
    line_items: list[Any] = Field(default_factory=list)
    remittance_advice: Any | None = None


class ErpSyncLogModel(BaseModel):
    """Domain model for an ERP synchronization transaction log."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    invoice_id: str
    erp_system: str
    sync_direction: str
    status: str
    payload: dict[str, Any] | None = None
    response_data: dict[str, Any] | None = None
    external_reference: str | None = None
    reconciled: bool = False
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1


class PaymentAuditLogModel(BaseModel):
    """Domain model for immutable financial audit log records."""

    model_config = ConfigDict(from_attributes=True, extra="allow")

    id: str
    entity_type: str
    entity_id: str
    action: str
    previous_state: dict[str, Any] | None = None
    new_state: dict[str, Any] | None = None
    details: str | None = None
    user_id: str = "system"
    user_roles: str = "Grants Manager"
    created_at: Any | None = None
    created_by: str = "system"
    reason_for_change: str | None = None
    version_index: int = 1
