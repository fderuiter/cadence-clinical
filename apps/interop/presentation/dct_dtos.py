"""Pydantic v2 DTOs for Decentralized Clinical Trials (DCT) REST API endpoints.

Strict typing and schema contracts for:
- WebRTC Telehealth Video Consultations (PRD-DCT-005)
- Tiered Pediatric Assent & LAR Consent (PRD-DCT-006)
- Direct-to-Patient (DTP) Logistics & Cold Chain (PRD-DCT-003)
- In-Home Healthcare Nurse Visits & Mobile eCRF Sync (PRD-DCT-004)
- ADR-2199 / ADR-2207, 21 CFR Part 11
"""

from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, Field

from apps.interop.domain.dct_models import (
    DTPShipmentStatus,
    ExcursionSeverity,
    InHomeVisitStatus,
    LARRelationship,
    ParticipantRole,
    PediatricAssentTier,
    ProcedureType,
    SignalingMessageType,
    TelehealthSessionStatus,
    TemperatureZone,
)

# =========================================================================
# Telehealth & Video Consultations DTOs
# =========================================================================


class TelehealthSessionCreateRequest(BaseModel):
    """Request to schedule a new WebRTC telehealth video visit."""

    study_id: str = Field(..., description="Study protocol identifier", min_length=1)
    subject_id: str = Field(..., description="Study subject identifier", min_length=1)
    title: str = Field(
        default="Telehealth Clinical Consultation",
        description="Descriptive title of consultation",
        min_length=1,
    )
    scheduled_start_utc: datetime = Field(
        ..., description="Scheduled visit start timestamp in UTC"
    )
    site_id: str | None = Field(
        default=None, description="Clinical trial site identifier"
    )
    scheduled_end_utc: datetime | None = Field(
        default=None, description="Optional scheduled end timestamp"
    )


class TelehealthSessionDTO(BaseModel):
    """Telehealth session data object."""

    id: str
    room_id: str
    study_id: str
    site_id: str | None
    subject_id: str
    title: str
    scheduled_start_utc: datetime
    scheduled_end_utc: datetime | None
    status: TelehealthSessionStatus
    co_view_state: dict[str, Any]
    signaling_messages: list[dict[str, Any]]
    participant_records: list[dict[str, Any]]
    recording_seal_metadata: dict[str, Any] | None
    created_at: datetime
    created_by: str


class TelehealthSessionResponse(BaseModel):
    """Response wrapper for telehealth session creation and queries."""

    success: bool
    session: TelehealthSessionDTO
    message: str = "Telehealth session processed successfully"


class TelehealthSessionListResponse(BaseModel):
    """Response wrapper for listing telehealth sessions."""

    success: bool
    sessions: list[TelehealthSessionDTO]
    total: int


class RoomTokenRequest(BaseModel):
    """Request to issue an HMAC-SHA256 signed room authorization token."""

    participant_id: str = Field(
        ..., description="Unique participant identifier", min_length=1
    )
    participant_role: ParticipantRole = Field(
        ..., description="Clinical role of participant"
    )
    display_name: str = Field(
        ..., description="Friendly participant display name", min_length=1
    )
    duration_minutes: int = Field(
        default=120, description="Token validity window in minutes", ge=15, le=480
    )


class RoomTokenResponse(BaseModel):
    """Response containing signed room token and claims."""

    success: bool
    room_id: str
    token: str
    expires_at_utc: datetime
    participant_role: ParticipantRole
    capabilities: dict[str, bool]


class WebRTCSignalingRequest(BaseModel):
    """WebRTC session signaling envelope (SDP / ICE candidates / annotations)."""

    sender_id: str = Field(
        ..., description="Participant sender identifier", min_length=1
    )
    sender_role: ParticipantRole = Field(..., description="Role of the sender")
    message_type: SignalingMessageType = Field(
        ..., description="Type of signaling message"
    )
    sdp: str | None = Field(
        default=None, description="Session Description Protocol (SDP) string"
    )
    candidate: str | None = Field(default=None, description="ICE candidate string")
    sdp_mid: str | None = Field(default=None, description="SDP media stream identifier")
    sdp_m_line_index: int | None = Field(default=None, description="SDP m-line index")
    payload: dict[str, Any] = Field(
        default_factory=dict, description="Arbitrary payload for chat/annotations"
    )


class CoViewStateRequest(BaseModel):
    """Synchronized eConsent co-viewing position and annotations update."""

    document_id: str = Field(
        ..., description="Informed consent document identifier", min_length=1
    )
    page_index: int = Field(default=0, description="Current active page index", ge=0)
    clause_id: str | None = Field(
        default=None, description="Active highlighted clause ID"
    )
    presenter_id: str = Field(..., description="Presenter user ID", min_length=1)
    presenter_role: ParticipantRole = Field(
        default=ParticipantRole.INVESTIGATOR, description="Role of presenter"
    )
    scroll_percent: float = Field(
        default=0.0,
        description="Vertical scroll percentage 0.0 to 100.0",
        ge=0.0,
        le=100.0,
    )
    active_highlights: list[dict[str, Any]] = Field(
        default_factory=list, description="Array of highlight coordinates"
    )
    lock_presenter_control: bool = Field(
        default=True, description="Whether presenter has exclusive navigation lock"
    )


class ArchiveRecordingRequest(BaseModel):
    """Request to archive video recording with SHA-256 digital seal (21 CFR Part 11)."""

    media_payload_or_hash: str = Field(
        ...,
        description="Raw recording media binary string or computed SHA-256 hash",
        min_length=1,
    )
    duration_seconds: int = Field(
        ..., description="Total recording duration in seconds", ge=1
    )
    resolution: str = Field(
        default="1080p", description="Video recording resolution (e.g. 720p, 1080p, 4K)"
    )
    recorded_by: str = Field(
        ..., description="User ID of recording operator", min_length=1
    )
    participant_manifest: list[dict[str, Any]] = Field(
        default_factory=list, description="List of participant identities"
    )


class ArchiveRecordingResponse(BaseModel):
    """Response containing recording digital seal."""

    success: bool
    recording_id: str
    media_sha256_hash: str
    digital_seal_signature: str
    sealed_at_utc: datetime
    part11_certification_statement: str


# =========================================================================
# Tiered Pediatric Assent DTOs
# =========================================================================


class ComprehensionAnswerDTO(BaseModel):
    """Comprehension question response item."""

    question_id: str = Field(..., min_length=1)
    selected_option: str = Field(..., min_length=1)
    correct_option: str = Field(..., min_length=1)


class PediatricAssentSubmitRequest(BaseModel):
    """Submission payload for tiered pediatric assent and LAR consent."""

    study_id: str = Field(..., min_length=1)
    subject_id: str = Field(..., min_length=1)
    minor_age: int = Field(
        ..., description="Age of minor at consent execution", ge=0, le=25
    )
    answers: list[ComprehensionAnswerDTO] = Field(
        default_factory=list, description="Quiz answers"
    )
    assent_signature: str | None = Field(
        default=None, description="Minor assent signature string or drawing token"
    )
    lar_name: str | None = Field(
        default=None, description="Name of Parent or Legally Authorized Representative"
    )
    lar_relationship: LARRelationship | None = Field(
        default=None, description="Legal relationship to minor"
    )
    lar_legal_basis: str | None = Field(
        default=None,
        description="Legal authority basis (e.g. Birth Certificate, Court Order)",
    )
    lar_signature: str | None = Field(
        default=None, description="Electronic signature of LAR"
    )
    investigator_name: str | None = Field(
        default=None, description="Investigator counter-signatory name"
    )
    investigator_signature: str | None = Field(
        default=None, description="Investigator electronic signature"
    )


class PediatricAssentDTO(BaseModel):
    """Persisted pediatric assent manifest."""

    assent_id: str
    study_id: str
    subject_id: str
    tier: PediatricAssentTier
    minor_age: int
    comprehension_score_percent: float
    comprehension_passed: bool
    assent_signature: str | None
    lar_name: str | None
    lar_relationship: str | None
    lar_legal_basis: str | None
    lar_signature: str | None
    investigator_name: str | None
    investigator_signature: str | None
    cryptographic_bundle_hash: str
    created_at: datetime


class PediatricAssentResponse(BaseModel):
    """Response wrapper for pediatric assent."""

    success: bool
    assent: PediatricAssentDTO
    message: str = "Pediatric assent processed successfully"


class PediatricAssentListResponse(BaseModel):
    """List response for pediatric assents."""

    success: bool
    assents: list[PediatricAssentDTO]
    total: int


class VerifyAssentResponse(BaseModel):
    """Response validating cryptographic bundle seal integrity."""

    assent_id: str
    is_valid: bool
    bundle_hash: str


# =========================================================================
# Direct-to-Patient (DTP) Logistics DTOs
# =========================================================================


class DTPShipmentCreateRequest(BaseModel):
    """Request to create a Direct-to-Patient courier dispatch order."""

    study_id: str = Field(..., min_length=1)
    site_id: str = Field(..., min_length=1)
    subject_pseudonym_id: str = Field(..., min_length=1)
    destination_address_pseudonym: str = Field(..., min_length=1)
    courier_name: str = Field(default="World Courier", min_length=1)
    temperature_zone: TemperatureZone = Field(default=TemperatureZone.REFRIGERATED)
    kit_serials: list[str] = Field(..., min_length=1)
    tamper_seal_id: str = Field(..., min_length=1)


class TemperatureReadingDTO(BaseModel):
    """Cold chain data logger reading point."""

    timestamp_utc: datetime
    temperature_celsius: float
    logger_id: str = "LOGGER-01"
    battery_percent: float = 100.0


class TemperatureIngestRequest(BaseModel):
    """Batch ingestion of temperature logger telemetry points."""

    readings: list[TemperatureReadingDTO] = Field(..., min_length=1)


class CustodyHandoffRequest(BaseModel):
    """Chain of Custody handoff recording request."""

    stage: str = Field(
        ..., description="e.g. DEPOT_PICKUP, TRANSIT_HUB, FINAL_MILE", min_length=1
    )
    released_by: str = Field(..., min_length=1)
    received_by: str = Field(..., min_length=1)
    tamper_seal_intact: bool = Field(default=True)
    current_temperature_celsius: float = Field(
        ..., description="Visual thermometer reading in °C"
    )
    notes: str = ""


class DeliveryConfirmRequest(BaseModel):
    """Delivery confirmation with recipient electronic signature."""

    recipient_name: str = Field(..., min_length=1)
    signature_manifestation: str = Field(..., min_length=1)
    tamper_seal_verified: bool = Field(default=True)
    logger_greenlight: bool = Field(default=True)


class DTPShipmentDTO(BaseModel):
    """Direct-to-Patient shipment data object."""

    shipment_id: str
    tracking_number: str
    courier_name: str
    study_id: str
    site_id: str
    subject_pseudonym_id: str
    destination_address_pseudonym: str
    temperature_zone: TemperatureZone
    kit_serials: list[str]
    tamper_seal_id: str
    status: DTPShipmentStatus
    current_temp_celsius: float
    min_temp_celsius: float
    max_temp_celsius: float
    mkt_celsius: float
    excursion_severity: ExcursionSeverity
    custody_handoffs: list[dict[str, Any]]
    temperature_readings: list[dict[str, Any]]
    delivery_signature: dict[str, Any] | None
    created_at: datetime


class DTPShipmentResponse(BaseModel):
    """Response wrapper for DTP shipment operations."""

    success: bool
    shipment: DTPShipmentDTO
    message: str = "DTP shipment processed successfully"


class DTPShipmentListResponse(BaseModel):
    """List response for DTP shipments."""

    success: bool
    shipments: list[DTPShipmentDTO]
    total: int


# =========================================================================
# In-Home Healthcare Nurse Visits DTOs
# =========================================================================


class ProtocolProcedureDTO(BaseModel):
    """Protocol procedure task record."""

    procedure_type: ProcedureType
    description: str
    completed: bool = False
    timestamp_utc: datetime | None = None
    sample_barcodes: list[str] = Field(default_factory=list)
    performed_by: str = ""
    notes: str = ""


class InHomeVisitCreateRequest(BaseModel):
    """Request to schedule an in-home visiting clinician appointment."""

    study_id: str = Field(..., min_length=1)
    site_id: str = Field(..., min_length=1)
    subject_id: str = Field(..., min_length=1)
    visit_number: str = Field(..., min_length=1)
    visit_name: str = Field(..., min_length=1)
    nurse_id: str = Field(..., min_length=1)
    nurse_name: str = Field(..., min_length=1)
    nurse_license_number: str = Field(..., min_length=1)
    scheduled_start_utc: datetime = Field(...)
    procedures: list[ProtocolProcedureDTO] = Field(default_factory=list)


class InHomeVisitStatusUpdateRequest(BaseModel):
    """Update in-home visit progress and completed procedures."""

    status: InHomeVisitStatus
    procedures: list[ProtocolProcedureDTO] | None = None


class MobileECRFSyncRequest(BaseModel):
    """Offline-cached mobile eCRF data entry submission."""

    client_sync_id: str = Field(..., min_length=1)
    offline_collected_at_utc: datetime = Field(...)
    data: dict[str, Any] = Field(
        ..., description="Key-value mapping of eCRF item answers"
    )


class InHomeVisitDTO(BaseModel):
    """In-home nursing visit data object."""

    visit_id: str
    study_id: str
    site_id: str
    subject_id: str
    visit_number: str
    visit_name: str
    nurse_id: str
    nurse_name: str
    nurse_license_number: str
    scheduled_start_utc: datetime
    status: InHomeVisitStatus
    procedures: list[dict[str, Any]]
    mobile_ecrf_data: dict[str, Any]
    offline_sync_id: str | None
    sync_status: str
    created_at: datetime


class InHomeVisitResponse(BaseModel):
    """Response wrapper for in-home visit."""

    success: bool
    visit: InHomeVisitDTO
    message: str = "In-home visit processed successfully"


class InHomeVisitListResponse(BaseModel):
    """List response for in-home visits."""

    success: bool
    visits: list[InHomeVisitDTO]
    total: int


# =========================================================================
# Overview Dashboard Metrics DTO
# =========================================================================


class DCTMetricsResponse(BaseModel):
    """Comprehensive DCT dashboard overview metrics."""

    success: bool
    study_id: str
    total_telehealth_sessions: int
    active_video_rooms: int
    total_dtp_shipments: int
    active_cold_chain_shipments: int
    temperature_excursion_alerts: int
    scheduled_nurse_visits: int
    completed_home_visits: int
    pending_offline_syncs: int
    pediatric_assents_completed: int
