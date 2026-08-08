"""Pydantic schemas for Safety service presentation layer."""

from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.safety.domain.sae_icsr import IndividualCaseSafetyReport


class ICSRDataExportRequest(BaseModel):
    job_name: str = Field(..., description="The descriptive name of the export job")
    icsr: IndividualCaseSafetyReport = Field(
        ..., description="The E2B ICSR report data"
    )


class SafetyCaseICSRCreate(BaseModel):
    worldwide_unique_case_id: str = Field(
        ..., description="Worldwide unique identifier for this safety case"
    )
    patient_id: str = Field(..., description="Unique subject/patient identifier")
    case_data: dict[str, Any] = Field(
        ..., description="The structured ICSR case JSON payload"
    )


class SafetyCaseICSRResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    worldwide_unique_case_id: str
    patient_id: str
    case_data: dict[str, Any]
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class SafetyExportJobCreate(BaseModel):
    job_name: str = Field(..., description="The descriptive name of the export job")


class SafetyExportJobResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    job_name: str
    status: str
    output: str | None = None
    error: str | None = None
    error_message: str | None = None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class SafetyAuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    created_at: str
    created_by: str
    reason_for_change: str | None = None
    version_index: int
    action: str
    details: str
    record_id: str | None = None


class SAEReconciliationRunRequest(BaseModel):
    study_id: str = Field(..., description="The study identifier for reconciliation")


class SAEDiscrepancyResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    run_id: str
    source: str
    case_event_key: str
    field_name: str
    expected_value: str | None = None
    actual_value: str | None = None
    meddra_version: str | None = None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class SAEReconciliationRunResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    run_date: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int
    discrepancies: list[SAEDiscrepancyResponse] = []


class SAEReconciliationJobRequest(BaseModel):
    study_id: str = Field(..., description="The study identifier for reconciliation")


class SAEReconciliationJobResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    status: str
    error_message: str | None = None
    run_id: str | None = None
    result_summary: dict[str, Any] | None = None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int
