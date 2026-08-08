"""Pydantic transport schemas for Safety Gateway dispatch and SAE reconciliation REST API.

Requirements: PRD-SYS-001
"""

from typing import Any

from pydantic import BaseModel, Field


class SafetyDispatchRequest(BaseModel):
    """Request payload to dispatch ICH E2B(R3) safety report to external PV gateway.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target subject ID")
    safety_report_id: str = Field(..., description="Unique E2B(R3) Safety Report ID")
    destination_gateway: str = Field(
        "ARGUS", description="Destination safety gateway: ARGUS, ARISG, EUDRAVIGILANCE"
    )
    expedited: bool = Field(True, description="True for expedited 7/15-day reporting")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class SafetyDispatchResponse(BaseModel):
    """Response payload following E2B safety report gateway dispatch.

    Requirements: PRD-SYS-001
    """

    dispatch_id: str = Field(..., description="Unique dispatch transaction ID")
    safety_report_id: str = Field(..., description="Target Safety Report ID")
    status: str = Field(
        ..., description="Dispatch status: DISPATCHED, DELIVERED, ACKNOWLEDGED"
    )
    dispatched_at: str = Field(..., description="UTC ISO dispatch timestamp")
    ack_status: str = Field(
        ..., description="AS2 / SFTP gateway acknowledgment message"
    )


class SAEReconcileRequest(BaseModel):
    """Request payload to reconcile EDC AE data against Safety ICSR cases.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target study ID")
    edc_ae_events: list[dict[str, Any]] = Field(
        ..., description="List of EDC AE form data dicts"
    )
    safety_cases_xml: list[str] | None = Field(
        None, description="Optional raw E2B XML reports to parse"
    )
