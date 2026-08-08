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
