from datetime import datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field


class ConflictStrategy(StrEnum):
    CLIENT_WINS = "CLIENT_WINS"
    SERVER_WINS = "SERVER_WINS"
    MERGE = "MERGE"


class FindingCreate(BaseModel):
    text: str = Field(..., description="The observation or action item text")
    severity: str = Field(..., description="Finding severity (MINOR, MAJOR, CRITICAL)")
    resolution_status: str | None = Field(
        "OPEN", description="Resolution status of finding"
    )


class OfflineSyncMarkers(BaseModel):
    client_id: str = Field(..., description="Unique client instance/device footprint")
    sequence_number: int = Field(
        ..., description="Monotonically increasing client sequence number"
    )
    conflict_strategy: ConflictStrategy = Field(
        ConflictStrategy.CLIENT_WINS,
        description="Explicit validated conflict resolution strategy",
    )
    timestamps: dict[str, Any] | None = Field(
        None, description="Per-field modification timestamps for fine-grained LWW merge"
    )
    signature: str | None = Field(
        None, description="HMAC-SHA256 signature for cryptographic validation"
    )


class MonitoringVisitOfflineSync(BaseModel):
    visit_id: str = Field(..., description="Target visit ID for offline update")
    study_id: str | None = Field(None, description="Optional study ID")
    site_id: str | None = Field(None, description="Optional site ID")
    actual_date: datetime = Field(
        ..., description="Actual date/time when the visit was conducted"
    )
    device_timestamp: datetime = Field(
        ..., description="Device timestamp when payload was captured"
    )
    findings: list[FindingCreate] = Field(
        default=[], description="List of recorded findings"
    )
    offline_sync_markers: OfflineSyncMarkers = Field(
        ..., description="Mandatory offline sync markers"
    )


class CTMSStudyCreate(BaseModel):
    study_id: str = Field(..., description="Unique study ID")
    name: str = Field(..., description="Descriptive study title/name")
    status: str | None = Field("ACTIVE", description="Initial study status")


class CTMSStudyResponse(BaseModel):
    id: str
    study_id: str
    name: str
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CTMSAuditLogResponse(BaseModel):
    id: str
    timestamp: str
    user_id: str | None
    user_role: str | None
    action: str
    details: str


class MonitoringVisitCreate(BaseModel):
    study_id: str = Field(..., description="Study ID associated with the visit")
    site_id: str = Field(..., description="Site ID where the monitoring visit occurs")
    cra_id: str = Field(..., description="CRA performing the monitoring visit")
    visit_type: str = Field(
        ..., description="Type of monitoring visit (e.g. SIV, IMV, COV)"
    )
    scheduled_date: datetime = Field(
        ..., description="Scheduled date/time of the visit"
    )


class MonitoringVisitComplete(BaseModel):
    actual_date: datetime = Field(
        ..., description="Actual date/time when the visit was conducted"
    )
    findings: list[FindingCreate] = Field(
        default=[], description="List of recorded findings"
    )


class MonitoringVisitResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    cra_id: str
    visit_type: str
    scheduled_date: str
    actual_date: str | None
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class MonitoringVisitFindingResponse(BaseModel):
    id: str
    visit_id: str
    text: str
    severity: str
    resolution_status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class GeneratedLetterResponse(BaseModel):
    id: str
    visit_id: str
    letter_type: str
    rendered_content: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class RecruitmentRecordCreate(BaseModel):
    site_id: str = Field(..., description="Site ID being tracked")
    study_id: str = Field(..., description="Study ID associated with the site")
    screened_count: int = Field(0, description="Total number of screened subjects")
    enrolled_count: int = Field(0, description="Total number of enrolled subjects")
    target_count: int = Field(0, description="Target enrollment count")
    as_of_date: datetime | None = Field(
        None, description="The date/time as of which metrics apply"
    )


class RecruitmentRecordResponse(BaseModel):
    id: str
    site_id: str
    study_id: str
    screened_count: int
    enrolled_count: int
    target_count: int
    as_of_date: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class SiteMilestoneCreate(BaseModel):
    site_id: str = Field(..., description="Site ID")
    study_id: str = Field(..., description="Study ID")
    milestone_type: str = Field(..., description="The type of milestone")
    planned_date: datetime | None = Field(None, description="Planned milestone date")
    actual_date: datetime | None = Field(None, description="Actual milestone date")
    status: str | None = Field("PLANNED", description="Status of the milestone")


class SiteMilestoneUpdate(BaseModel):
    planned_date: datetime | None = Field(None, description="Planned milestone date")
    actual_date: datetime | None = Field(None, description="Actual milestone date")
    status: str | None = Field(None, description="Status of the milestone")


class SiteMilestoneResponse(BaseModel):
    id: str
    site_id: str
    study_id: str
    milestone_type: str
    planned_date: str | None
    actual_date: str | None
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CRAAllocationCreate(BaseModel):
    cra_id: str = Field(..., description="CRA ID being allocated")
    site_id: str = Field(..., description="Site ID")
    study_id: str = Field(..., description="Study ID")
    status: str | None = Field("ACTIVE", description="Allocation status")
    effective_start_date: datetime | None = Field(
        None, description="Effective start date"
    )
    effective_end_date: datetime | None = Field(None, description="Effective end date")


class CRAAllocationUpdate(BaseModel):
    cra_id: str | None = Field(None, description="CRA ID being allocated")
    status: str | None = Field(None, description="Allocation status")
    effective_start_date: datetime | None = Field(
        None, description="Effective start date"
    )
    effective_end_date: datetime | None = Field(None, description="Effective end date")


class CRAAllocationResponse(BaseModel):
    id: str
    cra_id: str
    site_id: str
    study_id: str
    status: str
    effective_start_date: str
    effective_end_date: str | None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CRAWorkloadItem(BaseModel):
    cra_id: str
    active_allocations_count: int
    allocated_sites: list[str]
    allocated_studies: list[str]


class BudgetLineItemCreate(BaseModel):
    category: str = Field(
        ..., description="Category of budget item (VISIT_COST, EQUIPMENT, etc.)"
    )
    description: str = Field(..., description="Description of budget line item")
    amount: float = Field(..., description="Budget item cost")


class BudgetLineItemResponse(BaseModel):
    id: str
    grant_id: str
    category: str
    description: str
    amount: float
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class PaymentMilestoneCreate(BaseModel):
    milestone_name: str = Field(
        ..., description="Descriptive name of the payment milestone"
    )
    trigger_condition: str = Field(
        ..., description="Trigger condition (VISIT_COMPLETED, STUDY_APPROVED, MANUAL)"
    )
    amount: float = Field(
        ..., description="Payment amount associated with the milestone"
    )


class PaymentMilestoneResponse(BaseModel):
    id: str
    grant_id: str
    milestone_name: str
    trigger_condition: str
    amount: float
    is_triggered: bool
    triggered_at: str | None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class InvestigatorGrantCreate(BaseModel):
    study_id: str = Field(..., description="Clinical study ID")
    site_id: str = Field(..., description="Site ID")
    total_budget: float = Field(
        0.0, description="Overall budget allocated for the site"
    )
    currency: str | None = Field("USD", description="Currency code")


class InvestigatorGrantUpdate(BaseModel):
    total_budget: float | None = Field(None, description="Updated budget")
    currency: str | None = Field(None, description="Updated currency")
    status: str | None = Field(None, description="Updated status (DRAFT, APPROVED)")


class InvestigatorGrantResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    total_budget: float
    currency: str
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class InvestigatorPayableResponse(BaseModel):
    id: str
    grant_id: str
    milestone_id: str | None
    amount: float
    payment_status: str
    due_date: str | None
    paid_at: str | None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


# ==========================================
# Sub-domain DTOs
# ==========================================


# 1. Site Startup & Regulatory Greenlight
class CountryMilestoneCreate(BaseModel):
    study_id: str = Field(..., description="Clinical study ID")
    country_code: str = Field(..., description="ISO 2-letter or 3-letter country code")
    milestone_type: str = Field(
        ...,
        description="Milestone type (e.g. CTA_SUBMISSION, CTA_APPROVAL, ETHICS_APPROVAL)",
    )
    status: str = Field(
        "PLANNED", description="Status (PLANNED, IN_PROGRESS, APPROVED, REJECTED)"
    )
    planned_date: str | None = Field(None, description="Planned date ISO string")
    actual_date: str | None = Field(None, description="Actual date ISO string")
    approval_number: str | None = Field(
        None, description="Regulatory authority approval identifier"
    )
    authority_name: str | None = Field(
        None, description="Name of the competent authority / ethics body"
    )


class CountryMilestoneResponse(BaseModel):
    id: str
    study_id: str
    country_code: str
    milestone_type: str
    status: str
    planned_date: str | None
    actual_date: str | None
    approval_number: str | None
    authority_name: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class EssentialDocumentCreate(BaseModel):
    study_id: str = Field(..., description="Clinical study ID")
    site_id: str = Field(..., description="Site ID")
    document_type: str = Field(
        ...,
        description="Type of document (e.g. FDA_1572, SITE_CONTRACT, LOCAL_IRB_APPROVAL, GCP_CERTIFICATE)",
    )
    file_name: str = Field(..., description="Filename of the uploaded document")
    file_hash: str = Field(..., description="SHA-256 hash of the uploaded document")
    expiration_date: str | None = Field(None, description="Document expiration date")


class EssentialDocumentReview(BaseModel):
    status: str = Field(
        ..., description="Review disposition (APPROVED, REJECTED, EXPIRED)"
    )
    review_notes: str | None = Field(None, description="Reviewer feedback or rationale")


class EssentialDocumentResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    document_type: str
    file_name: str
    file_hash: str
    status: str
    expiration_date: str | None
    review_notes: str | None
    reviewed_by: str | None
    reviewed_at: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class CountryPrerequisiteRuleCreate(BaseModel):
    study_id: str
    country_code: str
    facet: str
    criterion_code: str
    criterion_name: str
    is_mandatory: bool = True
    description: str | None = None


class CountryPrerequisiteRuleResponse(BaseModel):
    id: str
    study_id: str
    country_code: str
    facet: str
    criterion_code: str
    criterion_name: str
    is_mandatory: bool
    description: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class SiteGreenlightGateResponse(BaseModel):
    id: str | None
    study_id: str
    site_id: str
    country_code: str = "US"
    overall_status: str
    facet_regulatory_irb: bool = False
    facet_cta_budget: bool = False
    facet_eisf_baseline: bool = False
    facet_doa_log: bool = False
    facet_staff_training: bool = False
    facet_ip_release: bool = False
    contract_approved: bool = False
    irb_approved: bool = False
    form_1572_approved: bool = False
    doa_signed_off: bool = False
    ip_ready: bool = False
    greenlight_token: str | None = None
    greenlight_certified_by: str | None = None
    greenlight_certified_at: str | None = None
    is_overridden: bool = False
    override_sponsor_user_id: str | None = None
    override_medical_user_id: str | None = None
    override_justification: str | None = None
    override_authorized_at: str | None = None
    evaluation_details: dict[str, Any] | None = None
    rejection_reason: str | None = None
    created_at: str | None = None
    created_by: str
    reason_for_change: str
    version_index: int


class SiteGreenlightOverrideRequest(BaseModel):
    study_id: str
    sponsor_user_id: str
    medical_user_id: str
    sponsor_signature: str
    medical_signature: str
    justification: str
    country_code: str = "US"


class GreenlightTokenVerifyRequest(BaseModel):
    token: str
    site_id: str | None = None
    study_id: str | None = None


class GreenlightTokenVerifyResponse(BaseModel):
    is_valid: bool
    payload: dict[str, Any] | None = None
    error: str | None = None


# 2. Protocol Deviations & Action Items
class ProtocolDeviationCreate(BaseModel):
    study_id: str = Field(..., description="Study ID")
    site_id: str = Field(..., description="Site ID")
    subject_id: str | None = Field(None, description="Subject ID if applicable")
    visit_id: str | None = Field(None, description="Visit ID if applicable")
    deviation_category: str = Field(
        ...,
        description="Category (ELIGIBILITY, INFORMED_CONSENT, DOSING_IP, SAFETY_REPORTING, VISIT_WINDOW, OTHER)",
    )
    severity: str = Field(..., description="Severity (MINOR, MAJOR, CRITICAL)")
    title: str = Field(..., description="Brief title of deviation")
    description: str = Field(
        ..., description="Detailed description of protocol deviation"
    )
    date_occurred: str = Field(..., description="Date deviation occurred (YYYY-MM-DD)")


class ProtocolDeviationRCA(BaseModel):
    root_cause_5whys: list[str] = Field(
        default=[], description="5-Why questions and answers"
    )
    root_cause_summary: str = Field(..., description="Summary of root cause analysis")
    corrective_action_plan: str = Field(..., description="Corrective action plan (CAP)")
    preventive_action_plan: str = Field(..., description="Preventive action plan (PAP)")


class ProtocolDeviationResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    subject_id: str | None
    visit_id: str | None
    deviation_category: str
    severity: str
    title: str
    description: str
    date_occurred: str
    date_identified: str
    status: str
    root_cause_5whys: list[str]
    root_cause_summary: str | None
    corrective_action_plan: str | None
    preventive_action_plan: str | None
    quality_capa_id: str | None
    reported_by: str
    resolved_by: str | None
    resolved_at: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class DeviationActionItemCreate(BaseModel):
    deviation_id: str = Field(..., description="Deviation ID")
    site_id: str = Field(..., description="Site ID")
    description: str = Field(..., description="Description of the action item task")
    assignee_user_id: str = Field(..., description="User ID of assignee")
    assignee_role: str = Field(..., description="Role of assignee")
    due_date: str = Field(..., description="Due date (YYYY-MM-DD)")


class DeviationActionItemComplete(BaseModel):
    resolution_notes: str = Field(..., description="Resolution summary")


class DeviationActionItemResponse(BaseModel):
    id: str
    deviation_id: str
    site_id: str
    description: str
    assignee_user_id: str
    assignee_role: str
    due_date: str
    status: str
    resolution_notes: str | None
    completed_by: str | None
    completed_at: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


# 3. RBQM & Centralized Monitoring
class RBQMKRIMetricCreate(BaseModel):
    study_id: str = Field(..., description="Study ID")
    site_id: str = Field(..., description="Site ID")
    metric_type: str = Field(
        ...,
        description="KRI metric type (QUERY_VELOCITY, SAE_REPORTING_LAG_DAYS, PROTOCOL_DEVIATION_RATE, FORM_ENTRY_LAG_DAYS, SDV_BACKLOG_RATE)",
    )
    metric_value: float = Field(..., description="Calculated metric value")
    threshold_low: float = Field(..., description="Lower normal threshold")
    threshold_high: float = Field(..., description="Upper normal threshold")
    notes: str | None = Field(
        None, description="Analytical context or observation notes"
    )


class RBQMKRIMetricResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    metric_type: str
    metric_value: float
    threshold_low: float
    threshold_high: float
    breach_status: str
    calculation_date: str
    notes: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class SiteRiskScoreResponse(BaseModel):
    id: str | None
    study_id: str
    site_id: str
    composite_score: float
    risk_level: str
    assessment_date: str
    recommended_monitoring_type: str
    monitoring_interval_days: int
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


# 4. Procedure Financials, Budget Templates & Invoices
class SiteBudgetTemplateCreate(BaseModel):
    template_name: str = Field(..., description="Template unique title")
    study_id: str | None = Field(None, description="Optional study ID scope")
    tier_level: str = Field(
        "STANDARD",
        description="Tier classification (TIER_1_ACADEMIC, TIER_2_COMMUNITY, TIER_3_REGIONAL)",
    )
    currency: str = Field("USD", description="Currency (USD, EUR, GBP, etc.)")
    overhead_percentage: float = Field(
        0.0, description="Default institutional overhead %"
    )
    withholding_percentage: float = Field(0.0, description="Default withholding %")
    fixed_startup_fee: float = Field(0.0, description="Fixed site startup fee")
    fixed_closeout_fee: float = Field(0.0, description="Fixed site closeout fee")
    irb_fee: float = Field(0.0, description="Initial IRB/Ethics review fee")
    patient_stipend_amount: float = Field(0.0, description="Per-visit patient stipend")
    travel_reimbursement_rate: float = Field(
        0.0, description="Per-visit travel reimbursement"
    )
    fixed_fees: list[dict[str, Any]] = Field(
        default=[], description="List of fixed milestone/setup fees"
    )
    procedure_items: list[dict[str, Any]] = Field(
        default=[], description="List of procedure schedule items"
    )


class SiteBudgetTemplateResponse(BaseModel):
    id: str
    template_name: str
    study_id: str | None
    tier_level: str
    currency: str
    overhead_percentage: float
    withholding_percentage: float
    fixed_startup_fee: float
    fixed_closeout_fee: float
    irb_fee: float
    patient_stipend_amount: float
    travel_reimbursement_rate: float
    fixed_fees: list[dict[str, Any]]
    procedure_items: list[dict[str, Any]]
    is_active: bool
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class ApplyTemplateRequest(BaseModel):
    template_id: str = Field(..., description="Site budget template ID to apply")


class ContractAmendmentCreate(BaseModel):
    summary_of_changes: str = Field(
        ..., description="Detailed summary of contract changes"
    )
    revised_budget: float = Field(..., description="New amended total grant budget")
    updated_procedures: list[dict[str, Any]] | None = Field(
        None, description="Optional updated fee schedule items"
    )
    signature_manifestation: dict[str, Any] | None = Field(
        None, description="Electronic signature payload"
    )


class ContractAmendmentResponse(BaseModel):
    id: str
    grant_id: str
    amendment_number: int
    effective_date: str
    previous_budget: float
    revised_budget: float
    previous_version_index: int
    new_version_index: int
    summary_of_changes: str
    status: str
    approved_by: str | None
    approved_at: str | None
    signature_manifestation: dict[str, Any] | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class CurrencyExchangeRateCreate(BaseModel):
    base_currency: str = Field("USD", description="Base ISO currency (e.g. USD)")
    target_currency: str = Field(..., description="Target ISO currency (e.g. EUR)")
    rate: float = Field(..., description="Exchange multiplier from base to target")
    expiry_date: str | None = Field(None, description="Optional expiry ISO date")


class CurrencyExchangeRateResponse(BaseModel):
    id: str
    base_currency: str
    target_currency: str
    rate: float
    effective_date: str
    expiry_date: str | None
    is_active: bool
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class CurrencyConvertRequest(BaseModel):
    amount: float = Field(..., description="Monetary amount to convert")
    from_currency: str = Field("USD", description="Source ISO currency")
    to_currency: str = Field(..., description="Target ISO currency")


class CurrencyConvertResponse(BaseModel):
    original_amount: float
    from_currency: str
    to_currency: str
    exchange_rate: float
    converted_amount: float


class BudgetAdjustmentCreate(BaseModel):
    grant_id: str = Field(..., description="Investigator grant ID")
    site_id: str = Field(..., description="Site ID")
    study_id: str = Field(..., description="Study ID")
    adjustment_type: str = Field(
        ...,
        description="Type (PASSTHROUGH_REIMBURSEMENT, PROTOCOL_DEVIATION_PENALTY, OUT_OF_WINDOW_PENALTY, SITE_BONUS, DISCRETIONARY_CREDIT)",
    )
    description: str = Field(..., description="Clinical/operational justification")
    amount: float = Field(
        ..., description="Adjustment value (positive credit or negative deduction)"
    )
    currency: str = Field("USD", description="Currency code")


class BudgetAdjustmentResponse(BaseModel):
    id: str
    grant_id: str
    site_id: str
    study_id: str
    adjustment_type: str
    description: str
    amount: float
    currency: str
    status: str
    approved_by: str | None
    approved_at: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class ProcedurePaymentGridCreate(BaseModel):
    grant_id: str = Field(..., description="Grant ID")
    visit_name: str = Field(
        ..., description="Visit name (e.g. SCREENING, WEEK_4, END_OF_TREATMENT)"
    )
    procedure_code: str = Field(..., description="CPT/Procedure code")
    procedure_name: str = Field(..., description="Descriptive procedure name")
    base_amount: float = Field(..., description="Base fee amount")
    overhead_percentage: float = Field(0.0, description="Institutional overhead %")
    withholding_percentage: float = Field(
        0.0, description="Retention/Withholding % (e.g. 10%)"
    )
    activity_id: str | None = Field(
        None, description="Optional CDISC USDM protocol activity ID"
    )
    form_id: str | None = Field(None, description="Optional eCRF form ID linkage")
    patient_stipend: float = Field(0.0, description="Per-procedure patient stipend")
    travel_reimbursement: float = Field(
        0.0, description="Per-procedure travel reimbursement"
    )


class ProcedurePaymentGridResponse(BaseModel):
    id: str
    grant_id: str
    visit_name: str
    procedure_code: str
    procedure_name: str
    base_amount: float
    overhead_percentage: float
    withholding_percentage: float
    activity_id: str | None = None
    form_id: str | None = None
    patient_stipend: float = 0.0
    travel_reimbursement: float = 0.0
    is_active: bool
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class FinancialInvoiceCreate(BaseModel):
    study_id: str = Field(..., description="Study ID")
    site_id: str = Field(..., description="Site ID")
    grant_id: str = Field(..., description="Grant ID")
    invoice_type: str = Field("VISIT_PROCEDURE_BATCH", description="Type of invoice")
    gross_amount: float = Field(..., description="Gross payable amount")
    withholding_amount: float = Field(0.0, description="Withheld amount")
    currency: str = Field("USD", description="Currency code")
    payable_ids: list[str] = Field(
        default=[], description="List of included payable IDs"
    )


class FinancialInvoiceResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    grant_id: str
    invoice_number: str
    invoice_type: str
    gross_amount: float
    withholding_amount: float
    net_amount: float
    currency: str
    status: str
    payable_ids: list[str]
    approved_by: str | None
    approved_at: str | None
    disbursed_at: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class VisitPayableCalculationResponse(BaseModel):
    gross_amount: float
    withholding_amount: float
    net_amount: float
    visit_name: str | None = None
    procedures_count: int = 0
    base_amount: float = 0.0
    overhead_amount: float = 0.0
    patient_stipend_total: float = 0.0
    travel_reimbursement_total: float = 0.0
    currency: str = "USD"
    target_currency: str | None = None
    exchange_rate: float = 1.0
    converted_net_amount: float | None = None
    version_index: int | None = None


# 5. IP Accountability & Temperature Excursions
class IPShipmentReceiveRequest(BaseModel):
    study_id: str = Field(..., description="Study ID")
    site_id: str = Field(..., description="Site ID")
    kit_numbers: list[str] = Field(..., description="List of kit serial identifiers")
    lot_number: str = Field(..., description="Manufacturing lot number")
    kit_type: str = Field(
        "ACTIVE_DRUG", description="Kit type (ACTIVE_DRUG, PLACEBO, COMPARATOR)"
    )
    shipment_tracking_number: str = Field(..., description="Courier tracking number")
    expiration_date: str = Field(..., description="Expiration date (YYYY-MM-DD)")


class IPKitRecordResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    kit_number: str
    lot_number: str
    kit_type: str
    shipment_tracking_number: str
    expiration_date: str
    status: str
    received_date: str | None
    dispensed_subject_id: str | None
    dispensed_visit_id: str | None
    dispensed_date: str | None
    returned_units_count: int | None
    expected_units_count: int | None
    compliance_percentage: float | None
    notes: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class IPTemperatureExcursionCreate(BaseModel):
    study_id: str = Field(..., description="Study ID")
    site_id: str = Field(..., description="Site ID")
    kit_ids: list[str] = Field(..., description="List of affected IP kit record IDs")
    excursion_type: str = Field(
        "STORAGE", description="Excursion type (TRANSIT, STORAGE)"
    )
    min_temp_celsius: float = Field(..., description="Minimum recorded temperature")
    max_temp_celsius: float = Field(..., description="Maximum recorded temperature")
    duration_hours: float = Field(
        ..., description="Duration in hours outside standard range"
    )
    occurred_at: str = Field(..., description="Timestamp when excursion was detected")


class IPTemperatureExcursionDisposition(BaseModel):
    disposition_status: str = Field(
        ..., description="QA disposition (QA_APPROVED_USE, QA_REJECTED_DESTROY)"
    )
    qa_rationale: str = Field(
        ..., description="Justification and stability data reference"
    )


class IPTemperatureExcursionResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    kit_ids: list[str]
    excursion_type: str
    min_temp_celsius: float
    max_temp_celsius: float
    duration_hours: float
    occurred_at: str
    disposition_status: str
    qa_reviewed_by: str | None
    qa_reviewed_at: str | None
    qa_rationale: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


class IPKitDispenseRequest(BaseModel):
    subject_id: str = Field(..., description="Subject ID")
    visit_id: str = Field(..., description="Visit ID")


class IPKitReconcileRequest(BaseModel):
    returned_units_count: int = Field(
        ..., description="Number of returned units/tablets"
    )
    expected_units_count: int = Field(
        ..., description="Expected returned units/tablets"
    )
    notes: str | None = Field(None, description="Discrepancy notes")


class IPDestructionCertificateCreate(BaseModel):
    study_id: str = Field(..., description="Study ID")
    site_id: str = Field(..., description="Site ID")
    kit_ids: list[str] = Field(..., description="List of kit IDs destroyed")
    destruction_method: str = Field(
        "ON_SITE_INCINERATION", description="Destruction method"
    )
    witness_user_id: str = Field(..., description="User ID of the witness")
    witness_role: str = Field(..., description="Role of the witness")
    pi_signature_hash: str = Field(
        ..., description="Cryptographic signature hash of the PI"
    )
    reason_for_destruction: str = Field(
        ..., description="Clinical rationale for destruction"
    )


class IPDestructionCertificateResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    certificate_number: str
    kit_ids: list[str]
    destruction_method: str
    destruction_date: str
    witness_user_id: str
    witness_role: str
    pi_signature_hash: str
    pi_signed_at: str
    reason_for_destruction: str
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int


# 6. eTMF Sync DTOs
class ETMFSyncRequest(BaseModel):
    study_id: str = Field(..., description="Study ID")
    site_id: str = Field(..., description="Site ID")
    artifact_type: str = Field(
        ...,
        description="Artifact type (MVR_REPORT, DOA_LOG, GREENLIGHT_PACKAGE, DEVIATION_REPORT, IP_DESTRUCTION_CERT)",
    )
    source_record_id: str = Field(..., description="Source record ID in CTMS")
    title: str = Field(..., description="Document Title")
    content_text: str = Field(
        ..., description="Full text or markdown content of artifact"
    )
    dia_zone: str = Field("05", description="DIA TMF Reference Model Zone")
    dia_section: str = Field("05.02", description="DIA TMF Section")
    dia_artifact: str = Field("Trip Report", description="DIA TMF Artifact Name")


class ETMFSyncRecordResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    artifact_type: str
    source_record_id: str
    etmf_document_id: str
    dia_zone: str
    dia_section: str
    dia_artifact: str
    sync_status: str
    error_message: str | None
    synced_at: str | None
    created_at: str | None
    created_by: str
    reason_for_change: str
    version_index: int
