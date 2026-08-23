from typing import Any

from pydantic import BaseModel


class CTMSDelegationEntity(BaseModel):
    id: str | None = None
    site_id: str
    staff_user_id: str
    task_codes: list[str]
    start_date: str
    end_date: str | None = None
    is_active: bool = False
    signed_off: bool = False
    created_by: str
    reason_for_change: str
    version_index: int = 1


class CTMSAuditLogEntity(BaseModel):
    id: str | None = None
    user_id: str
    user_role: str
    action: str
    details: str
    timestamp: str  # ISO string


# ==========================================
# 1. Site Startup & Regulatory Greenlight
# ==========================================


class CountryRegulatoryMilestoneEntity(BaseModel):
    id: str | None = None
    study_id: str
    country_code: str
    milestone_type: str  # CTA_SUBMISSION, CTA_APPROVAL, ETHICS_APPROVAL, IMPORT_LICENSE
    planned_date: str | None = None
    actual_date: str | None = None
    status: str = "PLANNED"  # PLANNED, IN_PROGRESS, APPROVED, REJECTED
    approval_number: str | None = None
    authority_name: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class EssentialDocumentEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    document_type: str  # FDA_1572, PROTOCOL_SIGNATURE, FINANCIAL_DISCLOSURE, SITE_CONTRACT, LOCAL_IRB_APPROVAL, LAB_CERTIFICATION, CV_INVESTIGATOR, GCP_CERTIFICATE
    file_name: str
    file_hash: str
    expiration_date: str | None = None
    status: str = "DRAFT"  # DRAFT, SUBMITTED, APPROVED, EXPIRED, REJECTED
    review_notes: str | None = None
    reviewed_by: str | None = None
    reviewed_at: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class SiteGreenlightGateEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    overall_status: str = "PENDING"  # PENDING, APPROVED, REJECTED
    contract_approved: bool = False
    irb_approved: bool = False
    form_1572_approved: bool = False
    doa_signed_off: bool = False
    ip_ready: bool = False
    greenlight_certified_by: str | None = None
    greenlight_certified_at: str | None = None
    rejection_reason: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


# ==========================================
# 2. Protocol Deviations & Action Items
# ==========================================


class ProtocolDeviationEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    subject_id: str | None = None
    visit_id: str | None = None
    deviation_category: str  # ELIGIBILITY, INFORMED_CONSENT, DOSING_IP, STUDY_PROCEDURES, SAFETY_REPORTING, LAB_HANDLING, VISIT_WINDOW, OTHER
    severity: str  # MINOR, MAJOR, CRITICAL
    title: str
    description: str
    date_occurred: str
    date_identified: str
    status: str = "IDENTIFIED"  # IDENTIFIED, UNDER_REVIEW, CAPA_ESCALATED, RESOLVED
    root_cause_5whys: list[str] = []
    root_cause_summary: str | None = None
    corrective_action_plan: str | None = None
    preventive_action_plan: str | None = None
    quality_capa_id: str | None = None
    reported_by: str
    resolved_by: str | None = None
    resolved_at: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class DeviationActionItemEntity(BaseModel):
    id: str | None = None
    deviation_id: str
    site_id: str
    description: str
    assignee_user_id: str
    assignee_role: str
    due_date: str
    status: str = "OPEN"  # OPEN, IN_PROGRESS, COMPLETED, CANCELLED
    resolution_notes: str | None = None
    completed_by: str | None = None
    completed_at: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


# ==========================================
# 3. RBQM & Centralized Monitoring
# ==========================================


class RBQMKRIMetricEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    metric_type: str  # QUERY_VELOCITY, SAE_REPORTING_LAG_DAYS, PROTOCOL_DEVIATION_RATE, FORM_ENTRY_LAG_DAYS, SDV_BACKLOG_RATE
    metric_value: float
    threshold_low: float
    threshold_high: float
    breach_status: str = "NORMAL"  # NORMAL, WARNING, BREACHED
    calculation_date: str
    notes: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class SiteRiskScoreEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    composite_score: float  # 0.0 - 100.0
    risk_level: str = "LOW"  # LOW, MEDIUM, HIGH
    assessment_date: str
    recommended_monitoring_type: str = (
        "ROUTINE_ON_SITE"  # REMOTE, ROUTINE_ON_SITE, TARGETED_FOR_CAUSE
    )
    monitoring_interval_days: int = 30
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


# ==========================================
# ==========================================
# 4. Procedure Financials, Budget Contracts & Invoices
# ==========================================


class SiteBudgetTemplateEntity(BaseModel):
    id: str | None = None
    template_name: str
    study_id: str | None = None
    tier_level: str = (
        "STANDARD"  # TIER_1_ACADEMIC, TIER_2_COMMUNITY, TIER_3_REGIONAL, STANDARD
    )
    currency: str = "USD"
    overhead_percentage: float = 0.0
    withholding_percentage: float = 0.0
    fixed_startup_fee: float = 0.0
    fixed_closeout_fee: float = 0.0
    irb_fee: float = 0.0
    patient_stipend_amount: float = 0.0
    travel_reimbursement_rate: float = 0.0
    fixed_fees: list[dict[str, Any]] = []
    procedure_items: list[dict[str, Any]] = []
    is_active: bool = True
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class InvestigatorGrantEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    contract_title: str | None = None
    template_id: str | None = None
    tier_level: str = "STANDARD"
    institution_name: str | None = None
    total_budget: float = 0.0
    currency: str = "USD"
    sponsor_currency: str = "USD"
    exchange_rate: float = 1.0
    overhead_percentage: float = 0.0
    withholding_percentage: float = 0.0
    fixed_startup_fee: float = 0.0
    fixed_closeout_fee: float = 0.0
    irb_fee: float = 0.0
    patient_stipend_per_visit: float = 0.0
    travel_reimbursement_per_visit: float = 0.0
    status: str = "DRAFT"
    is_active: bool = True
    executed_at: str | None = None
    executed_by: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class GrantContractAmendmentEntity(BaseModel):
    id: str | None = None
    grant_id: str
    amendment_number: int
    effective_date: str
    previous_budget: float = 0.0
    revised_budget: float = 0.0
    previous_version_index: int = 1
    new_version_index: int = 2
    summary_of_changes: str
    status: str = "EXECUTED"
    approved_by: str | None = None
    approved_at: str | None = None
    signature_manifestation: dict[str, Any] | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class CurrencyExchangeRateEntity(BaseModel):
    id: str | None = None
    base_currency: str = "USD"
    target_currency: str
    rate: float
    effective_date: str
    expiry_date: str | None = None
    is_active: bool = True
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class BudgetAdjustmentEntity(BaseModel):
    id: str | None = None
    grant_id: str
    site_id: str
    study_id: str
    adjustment_type: str  # PASSTHROUGH_REIMBURSEMENT, PROTOCOL_DEVIATION_PENALTY, OUT_OF_WINDOW_PENALTY, SITE_BONUS, DISCRETIONARY_CREDIT
    description: str
    amount: float
    currency: str = "USD"
    status: str = "PENDING"
    approved_by: str | None = None
    approved_at: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class ProcedurePaymentGridEntity(BaseModel):
    id: str | None = None
    grant_id: str
    visit_name: str
    activity_id: str | None = None
    form_id: str | None = None
    procedure_code: str
    procedure_name: str
    base_amount: float
    overhead_percentage: float = 0.0
    withholding_percentage: float = 0.0
    patient_stipend: float = 0.0
    travel_reimbursement: float = 0.0
    is_active: bool = True
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class FinancialInvoiceEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    grant_id: str
    invoice_number: str
    invoice_type: str  # VISIT_PROCEDURE_BATCH, MILESTONE_PAYMENT, PASSTHROUGH_EXPENSE
    gross_amount: float
    withholding_amount: float
    net_amount: float
    currency: str = "USD"
    status: str = "DRAFT"  # DRAFT, SUBMITTED, APPROVED, DISBURSED, CANCELLED
    payable_ids: list[str] = []
    approved_by: str | None = None
    approved_at: str | None = None
    disbursed_at: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


# ==========================================
# 5. IP Accountability & Temp Excursions
# ==========================================


class IPKitRecordEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    kit_number: str
    lot_number: str
    kit_type: str = "ACTIVE_DRUG"  # ACTIVE_DRUG, PLACEBO, COMPARATOR
    shipment_tracking_number: str
    expiration_date: str
    status: str = "RECEIVED_AVAILABLE"  # IN_TRANSIT, RECEIVED_AVAILABLE, QUARANTINED, DISPENSED, RETURNED_TO_SITE, DESTROYED, RETURNED_TO_DEPOT
    received_date: str | None = None
    dispensed_subject_id: str | None = None
    dispensed_visit_id: str | None = None
    dispensed_date: str | None = None
    returned_units_count: int | None = None
    expected_units_count: int | None = None
    compliance_percentage: float | None = None
    notes: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class IPTemperatureExcursionEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    kit_ids: list[str] = []
    excursion_type: str = "STORAGE"  # TRANSIT, STORAGE
    min_temp_celsius: float
    max_temp_celsius: float
    duration_hours: float
    occurred_at: str
    disposition_status: str = (
        "QUARANTINED"  # QUARANTINED, QA_APPROVED_USE, QA_REJECTED_DESTROY
    )
    qa_reviewed_by: str | None = None
    qa_reviewed_at: str | None = None
    qa_rationale: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


class IPDestructionCertificateEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    certificate_number: str
    kit_ids: list[str] = []
    destruction_method: str = "ON_SITE_INCINERATION"  # ON_SITE_INCINERATION, RETURN_TO_DEPOT_DESTRUCTION, BIOHAZARD_DISPOSAL
    destruction_date: str
    witness_user_id: str
    witness_role: str
    pi_signature_hash: str
    pi_signed_at: str
    reason_for_destruction: str
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1


# ==========================================
# 6. eTMF Synchronization Record
# ==========================================


class ETMFSyncRecordEntity(BaseModel):
    id: str | None = None
    study_id: str
    site_id: str
    artifact_type: str  # MVR_REPORT, DOA_LOG, GREENLIGHT_PACKAGE, DEVIATION_REPORT, IP_DESTRUCTION_CERT
    source_record_id: str
    etmf_document_id: str
    dia_zone: str
    dia_section: str
    dia_artifact: str
    sync_status: str = "SYNCED"  # PENDING, SYNCED, FAILED
    error_message: str | None = None
    synced_at: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int = 1
