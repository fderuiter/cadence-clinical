"""Domain models and data contracts for DCT Telehealth, DTP Logistics, and Pediatric Assent.

Pure domain representation adhering to:
- PRD-DCT-003: Direct-to-Patient (DTP) Investigational Product Dispatch Tracking
- PRD-DCT-004: Decentralized In-Home Healthcare Nurse Visit Coordination
- PRD-DCT-005: GxP-Compliant Telehealth Video Consultation & Encrypted Recording
- PRD-DCT-006: Tiered Pediatric Assent & Legally Authorized Representative (LAR) Consent
- ADR-2199 / ADR-2207, 21 CFR Part 11, ICH GCP E6(R2)/(R3)
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from enum import StrEnum
from typing import Any


class TelehealthSessionStatus(StrEnum):
    """Lifecycle states for WebRTC telehealth video visits."""

    SCHEDULED = "SCHEDULED"
    WAITING_ROOM = "WAITING_ROOM"
    IN_PROGRESS = "IN_PROGRESS"
    COMPLETED = "COMPLETED"
    CANCELLED = "CANCELLED"
    RECORDING_PROCESSING = "RECORDING_PROCESSING"


class ParticipantRole(StrEnum):
    """Permitted clinical roles for telehealth room participants."""

    INVESTIGATOR = "INVESTIGATOR"
    SUBJECT = "SUBJECT"
    LAR = "LAR"
    NURSE_COORDINATOR = "NURSE_COORDINATOR"
    TRANSLATOR = "TRANSLATOR"
    WITNESS = "WITNESS"


class SignalingMessageType(StrEnum):
    """WebRTC session signaling message categories."""

    OFFER = "OFFER"
    ANSWER = "ANSWER"
    ICE_CANDIDATE = "ICE_CANDIDATE"
    JOIN_ROOM = "JOIN_ROOM"
    LEAVE_ROOM = "LEAVE_ROOM"
    CO_VIEW_STATE = "CO_VIEW_STATE"
    ANNOTATION = "ANNOTATION"
    CHAT = "CHAT"


class PediatricAssentTier(StrEnum):
    """Multi-tiered age-appropriate informed consent / assent categories."""

    CHILD_7_11 = "CHILD_7_11"
    ADOLESCENT_12_17 = "ADOLESCENT_12_17"
    LAR_PARENT_GUARDIAN = "LAR_PARENT_GUARDIAN"
    ADULT_18_PLUS = "ADULT_18_PLUS"


class LARRelationship(StrEnum):
    """Legal relationship of Legally Authorized Representative to minor/subject."""

    PARENT = "PARENT"
    LEGAL_GUARDIAN = "LEGAL_GUARDIAN"
    POWER_OF_ATTORNEY = "POWER_OF_ATTORNEY"
    COURT_APPOINTED = "COURT_APPOINTED"
    NEXT_OF_KIN = "NEXT_OF_KIN"


class DTPShipmentStatus(StrEnum):
    """Lifecycle state machine for Direct-to-Patient courier IP shipments."""

    ORDER_CREATED = "ORDER_CREATED"
    DEPOT_PICKED = "DEPOT_PICKED"
    PACKED = "PACKED"
    COURIER_DISPATCHED = "COURIER_DISPATCHED"
    IN_TRANSIT = "IN_TRANSIT"
    OUT_FOR_DELIVERY = "OUT_FOR_DELIVERY"
    DELIVERED = "DELIVERED"
    TEMP_EXCURSION_HOLD = "TEMP_EXCURSION_HOLD"
    RETURNED = "RETURNED"


class TemperatureZone(StrEnum):
    """Storage condition temperature envelopes."""

    CRYOGENIC = "CRYOGENIC"  # -196°C to -150°C (LN2 dry shipper)
    ULTRA_COLD = "ULTRA_COLD"  # -80°C to -60°C (Dry ice)
    FROZEN = "FROZEN"  # -25°C to -15°C
    REFRIGERATED = "REFRIGERATED"  # 2°C to 8°C
    CONTROLLED_AMBIENT = "CONTROLLED_AMBIENT"  # 15°C to 25°C


class ExcursionSeverity(StrEnum):
    """Temperature excursion severity classifications."""

    NONE = "NONE"
    MINOR_WARNING = "MINOR_WARNING"
    CRITICAL_EXCURSION_QUARANTINE = "CRITICAL_EXCURSION_QUARANTINE"


class InHomeVisitStatus(StrEnum):
    """Lifecycle states for decentralized in-home nursing visits."""

    SCHEDULED = "SCHEDULED"
    CONFIRMED = "CONFIRMED"
    EN_ROUTE = "EN_ROUTE"
    ARRIVED_ON_SITE = "ARRIVED_ON_SITE"
    PROCEDURES_IN_PROGRESS = "PROCEDURES_IN_PROGRESS"
    VISIT_COMPLETED = "VISIT_COMPLETED"
    CANCELLED = "CANCELLED"
    RESCHEDULED = "RESCHEDULED"


class ProcedureType(StrEnum):
    """Standard decentralized in-home nursing clinical procedures."""

    VITAL_SIGNS = "VITAL_SIGNS"
    PK_BLOOD_DRAW = "PK_BLOOD_DRAW"
    IP_ADMINISTRATION = "IP_ADMINISTRATION"
    TWELVE_LEAD_ECG = "12_LEAD_ECG"
    SAFETY_LABS = "SAFETY_LABS"
    PHYSICAL_EXAM = "PHYSICAL_EXAM"
    BIOMARKER_SAMPLING = "BIOMARKER_SAMPLING"
    PATIENT_DIARY_REVIEW = "PATIENT_DIARY_REVIEW"


@dataclass
class RoomCapabilities:
    """Participant capability flags in a telehealth video consultation."""

    can_present: bool = False
    can_annotate: bool = False
    can_sign: bool = False
    can_record: bool = False
    can_admit: bool = False


@dataclass
class RoomTokenPayload:
    """Cryptographic room authorization token claim payload."""

    room_id: str
    session_id: str
    study_id: str
    subject_id: str
    participant_id: str
    participant_role: ParticipantRole
    display_name: str
    expires_at_utc: datetime
    capabilities: RoomCapabilities
    signature: str = ""


@dataclass
class WebRTCSignalingPayload:
    """WebRTC session signaling envelope."""

    session_id: str
    sender_id: str
    sender_role: ParticipantRole
    message_type: SignalingMessageType
    sdp: str | None = None
    candidate: str | None = None
    sdp_mid: str | None = None
    sdp_m_line_index: int | None = None
    payload: dict[str, Any] = field(default_factory=dict)
    timestamp_utc: datetime = field(default_factory=datetime.utcnow)


@dataclass
class CoViewStatePayload:
    """Synchronized interactive eConsent co-viewing session state."""

    session_id: str
    document_id: str
    page_index: int
    clause_id: str | None = None
    presenter_id: str = ""
    presenter_role: ParticipantRole = ParticipantRole.INVESTIGATOR
    scroll_percent: float = 0.0
    active_highlights: list[dict[str, Any]] = field(default_factory=list)
    lock_presenter_control: bool = True
    timestamp_utc: datetime = field(default_factory=datetime.utcnow)


@dataclass
class GxPRecordingSeal:
    """Cryptographic digital seal verifying video recording authenticity (21 CFR Part 11)."""

    recording_id: str
    session_id: str
    study_id: str
    duration_seconds: int
    resolution: str
    media_sha256_hash: str
    digital_seal_signature: str
    recorded_by: str
    sealed_at_utc: datetime
    participant_manifest: list[dict[str, Any]]
    part11_certification_statement: str


@dataclass
class PediatricAssentPayload:
    """Tiered pediatric assent and LAR consent verification manifest."""

    assent_id: str
    study_id: str
    subject_id: str
    tier: PediatricAssentTier
    minor_age: int
    comprehension_score_percent: float
    comprehension_passed: bool
    assent_signature: str | None = None
    assent_signed_at_utc: datetime | None = None
    lar_name: str | None = None
    lar_relationship: LARRelationship | None = None
    lar_legal_basis: str | None = None
    lar_signature: str | None = None
    lar_signed_at_utc: datetime | None = None
    investigator_name: str | None = None
    investigator_signature: str | None = None
    investigator_signed_at_utc: datetime | None = None
    cryptographic_bundle_hash: str = ""


@dataclass
class TemperatureReading:
    """Single time-series temperature reading from cold-chain data logger."""

    timestamp_utc: datetime
    temperature_celsius: float
    logger_id: str
    battery_percent: float = 100.0


@dataclass
class CustodyHandoff:
    """Immutable Chain of Custody (CoC) handoff verification event."""

    handoff_id: str
    stage: str
    released_by: str
    received_by: str
    timestamp_utc: datetime
    tamper_seal_intact: bool
    current_temperature_celsius: float
    notes: str = ""
    signature_manifestation: str = ""


@dataclass
class DTPShipmentPayload:
    """Direct-to-Patient courier dispatch and tracking payload."""

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
    custody_handoffs: list[CustodyHandoff] = field(default_factory=list)
    temperature_readings: list[TemperatureReading] = field(default_factory=list)
    delivery_signature: dict[str, Any] | None = None


@dataclass
class ProtocolProcedureRecord:
    """In-home clinical procedure execution record."""

    procedure_type: ProcedureType
    description: str
    completed: bool
    timestamp_utc: datetime | None = None
    sample_barcodes: list[str] = field(default_factory=list)
    performed_by: str = ""
    notes: str = ""


@dataclass
class InHomeVisitPayload:
    """In-home visiting healthcare nurse schedule and mobile eCRF bundle."""

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
    procedures: list[ProtocolProcedureRecord] = field(default_factory=list)
    mobile_ecrf_data: dict[str, Any] = field(default_factory=dict)
    offline_sync_id: str = ""
    sync_status: str = "SYNCHRONIZED"


@dataclass
class DCTDashboardMetrics:
    """Aggregated clinical DCT overview metrics."""

    total_telehealth_sessions: int
    active_video_rooms: int
    total_dtp_shipments: int
    active_cold_chain_shipments: int
    temperature_excursion_alerts: int
    scheduled_nurse_visits: int
    completed_home_visits: int
    pending_offline_syncs: int
    pediatric_assents_completed: int
