from datetime import datetime
from typing import Any

from pydantic import BaseModel, Field

# eCOA and ePRO Shared Pydantic Schemas


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
    status: str  # "COMPLETED", "PENDING", "OVERDUE"
    due_at: datetime | None
    end_date: datetime
    submitted_at: datetime | None = None


class SubjectComplianceResponse(BaseModel):
    subject_id: str
    compliance_rate: float  # completed / total assignments * 100.0
    completed_count: int
    pending_count: int
    overdue_count: int
    assignments: list[AssignmentComplianceDetail]
