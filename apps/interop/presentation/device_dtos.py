"""Pydantic v2 DTOs for Device SDTM (DD / DO) and Regulatory Incident Exports (EU MDR / IVDR).

Requirements: PRD-SYS-126, PRD-DEV-004, ISO 14155:2020, EU MDR 2017/745, EU IVDR 2017/746, ADR-2209.
"""

from typing import Any

from pydantic import BaseModel, Field


class SDTMDDGenerateRequest(BaseModel):
    """Request payload for compiling SDTM DD (Device Deficiencies) dataset."""

    study_id: str = Field(..., min_length=1)
    deficiencies: list[dict[str, Any]] = Field(default_factory=list)


class SDTMDOGenerateRequest(BaseModel):
    """Request payload for compiling SDTM DO (Device Tracking and Operations) dataset."""

    study_id: str = Field(..., min_length=1)
    devices: list[dict[str, Any]] = Field(default_factory=list)


class SDTMDDResponse(BaseModel):
    """Response payload containing compiled SDTM DD records."""

    study_id: str
    domain: str = "DD"
    record_count: int
    records: list[dict[str, Any]]


class SDTMDOResponse(BaseModel):
    """Response payload containing compiled SDTM DO records."""

    study_id: str
    domain: str = "DO"
    record_count: int
    records: list[dict[str, Any]]


class MDRIncidentReportRequest(BaseModel):
    """Request payload for compiling an EU MDR / IVDR Manufacturer Incident Report (MIR v7.2)."""

    study_id: str = Field(..., min_length=1)
    site_id: str = Field(..., min_length=1)
    deficiency_id: str = Field(..., min_length=1)
    brand_name: str = Field(..., min_length=1)
    udi_di: str = Field(..., min_length=3)
    lot_number: str = Field(..., min_length=1)
    serial_number: str | None = None
    incident_date: str
    awareness_date: str | None = None
    deficiency_type: str = "MALFUNCTION"
    deficiency_description: str = Field(..., min_length=5)
    safety_classification: str = "SADE"
    causality: str = "POSSIBLE"
    patient_involved: bool = False
    health_impact: str = "NONE"
    root_cause_analysis: str | None = None
    action_taken: str = "QUARANTINED_AT_SITE"
    capa_required: bool = False
    fsca_required: bool = False


class MDRIncidentReportResponse(BaseModel):
    """Response payload containing MIR v7.2 JSON and XML documents."""

    report_reference_number: str
    mir_version: str = "7.2"
    regulatory_framework: str = "EU_MDR_2017_745"
    reporting_date: str
    payload_json: dict[str, Any]
    payload_xml: str


class DeviceInvestigationExportPackageRequest(BaseModel):
    """Request payload for compiling full regulatory device investigation export package."""

    study_id: str = Field(..., min_length=1)
    devices: list[dict[str, Any]] = Field(default_factory=list)
    deficiencies: list[dict[str, Any]] = Field(default_factory=list)


class DeviceInvestigationExportPackageResponse(BaseModel):
    """Response payload containing SDTM DD, DO, and EU MDR/IVDR incident reports."""

    study_id: str
    generated_at: str
    sdtm_dd_count: int
    sdtm_do_count: int
    sdtm_dd_dataset: list[dict[str, Any]]
    sdtm_do_dataset: list[dict[str, Any]]
    incident_reports: list[dict[str, Any]]
    audit_manifest: dict[str, Any]
