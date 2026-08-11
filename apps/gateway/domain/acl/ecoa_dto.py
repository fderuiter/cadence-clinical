"""Gateway ACL DTOs for eCOA/ePRO router.

Requirements: PRD-SYS-001
"""

from datetime import datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field


class InstrumentCreate(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    name: str = Field(..., description="The name of the questionnaire/diary")
    description: str | None = Field(None, description="Optional description")
    items: dict[str, Any] = Field(..., description="Items/questions")
    response_types: dict[str, Any] = Field(
        ..., description="Response types and options"
    )
    scoring_metadata: dict[str, Any] = Field(..., description="Scoring metadata")
    reason_for_change: str = Field(
        ..., description="21 CFR Part 11 compliant reason for change"
    )


class InstrumentResponse(BaseModel):
    id: str
    name: str
    description: str | None
    items: dict[str, Any]
    response_types: dict[str, Any]
    scoring_metadata: dict[str, Any]
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class SubjectAssignmentCreate(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    subject_id: str = Field(..., description="Unique subject identifier")
    instrument_id: str = Field(..., description="ID of the Instrument to assign")
    start_date: datetime = Field(..., description="Start of the due/recurrence window")
    end_date: datetime = Field(..., description="End of the due/recurrence window")
    recurrence_pattern: str | None = Field(None, description="E.g., DAILY, WEEKLY")
    due_at: datetime | None = Field(None, description="Optional specific due date/time")
    reason_for_change: str = Field(
        ..., description="21 CFR Part 11 compliant reason for change"
    )


class SubjectAssignmentResponse(BaseModel):
    id: str
    subject_id: str
    instrument_id: str
    start_date: datetime
    end_date: datetime
    recurrence_pattern: str | None
    due_at: datetime | None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class AssignmentComplianceDetail(BaseModel):
    assignment_id: str
    instrument_id: str
    instrument_name: str
    status: str
    due_at: datetime | None
    end_date: datetime
    submitted_at: datetime | None = None


class SubjectComplianceResponse(BaseModel):
    subject_id: str
    compliance_rate: float
    completed_count: int
    pending_count: int
    overdue_count: int
    assignments: list[AssignmentComplianceDetail]


class ConflictStrategyEnum(StrEnum):
    CLIENT_WINS = "CLIENT_WINS"
    SERVER_WINS = "SERVER_WINS"
    MERGE = "MERGE"


class EPROOfflineMarker(BaseModel):
    sequence_number: int
    client_id: str
    conflict_strategy: ConflictStrategyEnum = ConflictStrategyEnum.CLIENT_WINS
    signature: str | None = None
    timestamps: dict[str, datetime] | None = None


class EPROOfflineEntry(BaseModel):
    subject_id: str
    diary_id: str
    device_timestamp: datetime
    answers: dict[str, Any]
    offline_sync_markers: EPROOfflineMarker


class EPROBulkSyncRequest(BaseModel):
    submissions: list[EPROOfflineEntry]


class EPROSubmitResponse(BaseModel):
    status: str
    id: str | None = None
    subject_id: str | None = None
    diary_id: str | None = None
    answers: dict[str, Any] | None = None
    sync_status: str | None = None
    version_index: int | None = None
    query: dict[str, Any] | None = None
    signature_validation: dict[str, Any] | None = None
    reconciliation_result: dict[str, Any] | None = None
    audit_details: dict[str, Any] | None = None
    offline_sync_markers: EPROOfflineMarker | None = None


class EPROBulkSyncResponse(BaseModel):
    status: str
    processed_count: int
    created_count: int
    updated_count: int
    ignored_count: int
    conflict_count: int
    results: list[EPROSubmitResponse]


class SubjectNotificationResponse(BaseModel):
    id: str
    subject_id: str
    assignment_id: str | None = None
    due_at: datetime
    channel: str
    delivery_status: str
    is_read: bool
    read_at: datetime | None = None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class AcknowledgeNotificationRequest(BaseModel):
    reason_for_change: str
