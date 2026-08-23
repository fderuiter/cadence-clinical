"""Pydantic schemas for Safety service presentation layer."""

from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.safety.domain.as2_models import (
    AckType,
    AS2Authority,
)
from apps.safety.domain.periodic_report_models import (
    DSURLineListingItem,
    DSURSummaryTabulationRow,
    PeriodicReportCompilationRequest,
    PeriodicReportPayload,
    PeriodicReportResponseDTO,
    PeriodicReportSignRequest,
    PeriodicReportSignResponse,
    PeriodicReportStatus,
    PeriodicSafetyReportType,
)
from apps.safety.domain.sae_icsr import IndividualCaseSafetyReport
from apps.safety.domain.signal_models import (
    BenchmarkReferenceDataset,
    DisproportionalityContingency,
    DisproportionalityMetricResult,
    MGPSMultiItemResult,
    SafetySignalAlertDTO,
    SafetySignalAlertReviewRequest,
    SafetySignalAlertSeverity,
    SafetySignalAlertSignRequest,
    SafetySignalAlertSignResponse,
    SafetySignalAlertStatus,
    SafetySignalReviewDecision,
    SignalDetectionScanRequest,
    SignalDetectionScanResponse,
    SignalStrength,
    StratificationLevel,
)

__all__ = [
    "AS2Authority",
    "AS2OutboundTransmissionRequestDTO",
    "AS2TransmissionAuditLogDTO",
    "AS2TransmissionResponseDTO",
    "AckType",
    "BenchmarkReferenceDataset",
    "DSURLineListingItem",
    "DSURSummaryTabulationRow",
    "DisproportionalityContingency",
    "DisproportionalityMetricResult",
    "ICSRDataExportRequest",
    "IndividualCaseSafetyReport",
    "MDNProcessRequestDTO",
    "MDNProcessResponseDTO",
    "MGPSMultiItemResult",
    "PeriodicReportCompilationRequest",
    "PeriodicReportPayload",
    "PeriodicReportResponseDTO",
    "PeriodicReportSignRequest",
    "PeriodicReportSignResponse",
    "PeriodicReportStatus",
    "PeriodicSafetyReportType",
    "RegulatoryAckProcessRequestDTO",
    "RegulatoryAckProcessResponseDTO",
    "SAEDiscrepancyResponse",
    "SAEReconciliationJobRequest",
    "SAEReconciliationJobResponse",
    "SAEReconciliationRunRequest",
    "SAEReconciliationRunResponse",
    "SafetyAuditLogResponse",
    "SafetyCaseICSRCreate",
    "SafetyCaseICSRResponse",
    "SafetyExportJobCreate",
    "SafetyExportJobResponse",
    "SafetySignalAlertDTO",
    "SafetySignalAlertReviewRequest",
    "SafetySignalAlertSeverity",
    "SafetySignalAlertSignRequest",
    "SafetySignalAlertSignResponse",
    "SafetySignalAlertStatus",
    "SafetySignalReviewDecision",
    "SignalDetectionScanRequest",
    "SignalDetectionScanResponse",
    "SignalStrength",
    "StratificationLevel",
]


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


# =========================================================================
# AS2 B2B Transmission Gateway & MDN Presentation DTOs (PRD-PVG-002)
# =========================================================================


class AS2OutboundTransmissionRequestDTO(BaseModel):
    """Request schema for dispatching an outbound ICH E2B(R3) payload via AS2."""

    case_id: str | None = Field(default=None, description="Internal Safety Case ID")
    study_id: str | None = Field(default=None, description="Clinical Study Protocol ID")
    icsr_xml_payload: str = Field(
        ..., description="ICH E2B(R3) ICSR XML payload string"
    )
    target_authority: AS2Authority = Field(
        ...,
        description="Target Regulatory Health Authority (e.g. FDA_ESG, EMA_GATEWAY)",
    )
    sender_as2_id: str = Field(
        default="CADENCE-SPONSOR", description="Sender AS2 Identifier"
    )
    receiver_as2_id: str | None = Field(
        default=None, description="Receiver AS2 Identifier"
    )
    environment: str = Field(
        default="PRODUCTION", description="Environment (PRODUCTION / TEST)"
    )
    custom_headers: dict[str, str] = Field(
        default_factory=dict, description="Custom AS2 / HTTP headers"
    )


class AS2TransmissionAuditLogDTO(BaseModel):
    """Schema for individual immutable event log records in the transmission ledger."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    transmission_id: str
    action: str
    from_status: str | None = None
    to_status: str | None = None
    details: str
    created_at: str
    created_by: str
    reason_for_change: str | None = None
    version_index: int


class AS2TransmissionResponseDTO(BaseModel):
    """Response schema for AS2 B2B Transmission record details and handshake status."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    transmission_id: str
    case_id: str | None = None
    study_id: str | None = None
    worldwide_unique_case_id: str | None = None
    message_id: str
    as2_from: str
    as2_to: str
    target_authority: str
    environment: str
    status: str
    payload_digest_sha256: str
    mic: str
    encryption_algorithm: str
    signature_algorithm: str
    outbound_payload_size: int
    outbound_xml: str | None = None

    # ACK A
    mdn_message_id: str | None = None
    mdn_disposition: str | None = None
    mdn_mic: str | None = None
    mdn_status: str | None = None
    mdn_received_at: str | None = None
    ack_a_status: str | None = None
    ack_a_received_at: str | None = None

    # ACK B
    ack_b_status: str | None = None
    ack_b_received_at: str | None = None
    ack_b_errors: list[dict[str, Any]] = []

    # ACK C
    ack_c_status: str | None = None
    ack_c_received_at: str | None = None
    ack_c_details: dict[str, Any] = {}

    error_message: str | None = None
    retry_count: int = 0

    # 21 CFR Part 11
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int
    audit_events: list[AS2TransmissionAuditLogDTO] = []


class MDNProcessRequestDTO(BaseModel):
    """Request schema for processing an inbound asynchronous MDN receipt."""

    mdn_payload: str = Field(..., description="Raw MDN multipart MIME or text body")
    authority_cert_pem: str | None = Field(
        default=None,
        description="Optional sender certificate for signature verification",
    )


class MDNProcessResponseDTO(BaseModel):
    """Response schema after parsing and verifying an MDN receipt."""

    is_valid_mdn: bool
    original_message_id: str | None = None
    disposition_mode: str | None = None
    disposition_type: str
    disposition_raw: str | None = None
    received_content_mic: str | None = None
    mic_matches: bool | None = None
    is_signed: bool = False
    signature_valid: bool | None = None
    human_readable_text: str | None = None
    error_details: str | None = None


class RegulatoryAckProcessRequestDTO(BaseModel):
    """Request schema for processing an inbound ACK B or ACK C payload."""

    ack_payload: str = Field(..., description="ACK B or ACK C XML payload string")
    ack_type: AckType = Field(
        ...,
        description="Acknowledgement type: ACK_B (syntactic) or ACK_C (authority acceptance)",
    )
    transmission_id: str | None = Field(
        default=None, description="Optional transmission ID to associate directly"
    )


class RegulatoryAckProcessResponseDTO(BaseModel):
    """Response schema after processing a regulatory acknowledgement."""

    ack_type: str
    ack_status: str
    is_accepted: bool
    authority_receipt_id: str | None = None
    worldwide_unique_case_id: str | None = None
    message_id: str | None = None
    authority_status_code: str | None = None
    errors: list[str] = []
    warnings: list[str] = []
    received_at: str
