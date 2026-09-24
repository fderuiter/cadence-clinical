"""FastAPI Router for Decentralized Clinical Trials (DCT) Operations.

Endpoints:
- Telehealth WebRTC Video Consultations & GxP Recording Archival (PRD-DCT-005)
- Tiered Pediatric Assent & LAR Consent Sealing (PRD-DCT-006)
- Direct-to-Patient (DTP) Logistics, Cold Chain, & Handoffs (PRD-DCT-003)
- In-Home Healthcare Nurse Visits & Mobile eCRF Sync (PRD-DCT-004)
- ADR-2199 / ADR-2207, 21 CFR Part 11
"""

from __future__ import annotations

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, HTTPException, Query, Request, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.adapters.database import db_manager
from apps.interop.adapters.dct_models import (
    DTPShipmentRecord,
    InHomeVisitRecord,
    PediatricAssentRecord,
    TelehealthSessionRecord,
)
from apps.interop.adapters.dct_repository import DCTRepository
from apps.interop.application.dct_service import DCTService
from apps.interop.domain.dct_models import (
    CoViewStatePayload,
    DTPShipmentStatus,
    ExcursionSeverity,
    InHomeVisitStatus,
    PediatricAssentTier,
    TemperatureReading,
    TemperatureZone,
    WebRTCSignalingPayload,
)
from apps.interop.presentation.dct_dtos import (
    ArchiveRecordingRequest,
    ArchiveRecordingResponse,
    CoViewStateRequest,
    CustodyHandoffRequest,
    DCTMetricsResponse,
    DeliveryConfirmRequest,
    DTPShipmentCreateRequest,
    DTPShipmentDTO,
    DTPShipmentListResponse,
    DTPShipmentResponse,
    InHomeVisitCreateRequest,
    InHomeVisitDTO,
    InHomeVisitListResponse,
    InHomeVisitResponse,
    InHomeVisitStatusUpdateRequest,
    MobileECRFSyncRequest,
    PediatricAssentDTO,
    PediatricAssentListResponse,
    PediatricAssentResponse,
    PediatricAssentSubmitRequest,
    RoomTokenRequest,
    RoomTokenResponse,
    TelehealthSessionCreateRequest,
    TelehealthSessionDTO,
    TelehealthSessionListResponse,
    TelehealthSessionResponse,
    TemperatureIngestRequest,
    VerifyAssentResponse,
    WebRTCSignalingRequest,
)
from packages.database import DatabaseSessionDependency
from packages.security.trial_roles import get_normalized_request_roles

router = APIRouter(
    prefix="/api/v1/interop/dct", tags=["Decentralized Clinical Trials (DCT)"]
)
get_db_session = DatabaseSessionDependency(db_manager)


def _extract_principal(request: Request) -> tuple[str, str, str]:
    """Extract user_id, user_role, and change_reason from request headers/state."""
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or request.headers.get("x-user-id")
        or "system"
    )
    roles = get_normalized_request_roles(request)
    primary_role = roles[0] if roles else "site_crc"
    change_reason = (
        request.headers.get("X-Reason-For-Change")
        or request.headers.get("x-reason-for-change")
        or "GxP Decentralized Clinical Trials operation"
    )
    return user_id, primary_role, change_reason


def _get_dct_service(session: AsyncSession) -> DCTService:
    """Instantiate DCTService with repository."""
    repo = DCTRepository(session)
    return DCTService(repo)


def _map_session_dto(r: TelehealthSessionRecord) -> TelehealthSessionDTO:
    """Map DB session record to DTO."""
    return TelehealthSessionDTO(
        id=r.id,
        room_id=r.room_id,
        study_id=r.study_id,
        site_id=r.site_id,
        subject_id=r.subject_id,
        title=r.title,
        scheduled_start_utc=r.scheduled_start_utc,
        scheduled_end_utc=r.scheduled_end_utc,
        status=r.status,  # type: ignore[arg-type]
        co_view_state=r.co_view_state or {},
        signaling_messages=r.signaling_messages or [],
        participant_records=r.participant_records or [],
        recording_seal_metadata=r.recording_seal_metadata,
        created_at=r.created_at,
        created_by=r.created_by,
    )


def _map_assent_dto(r: PediatricAssentRecord) -> PediatricAssentDTO:
    """Map DB assent record to DTO."""
    return PediatricAssentDTO(
        assent_id=r.assent_id,
        study_id=r.study_id,
        subject_id=r.subject_id,
        tier=PediatricAssentTier(r.tier),
        minor_age=r.minor_age,
        comprehension_score_percent=r.comprehension_score_percent,
        comprehension_passed=r.comprehension_passed,
        assent_signature=r.assent_signature,
        lar_name=r.lar_name,
        lar_relationship=r.lar_relationship,
        lar_legal_basis=r.lar_legal_basis,
        lar_signature=r.lar_signature,
        investigator_name=r.investigator_name,
        investigator_signature=r.investigator_signature,
        cryptographic_bundle_hash=r.cryptographic_bundle_hash,
        created_at=r.created_at,
    )


def _map_shipment_dto(r: DTPShipmentRecord) -> DTPShipmentDTO:
    """Map DB DTP shipment record to DTO."""
    return DTPShipmentDTO(
        shipment_id=r.shipment_id,
        tracking_number=r.tracking_number,
        courier_name=r.courier_name,
        study_id=r.study_id,
        site_id=r.site_id,
        subject_pseudonym_id=r.subject_pseudonym_id,
        destination_address_pseudonym=r.destination_address_pseudonym,
        temperature_zone=TemperatureZone(r.temperature_zone),
        kit_serials=r.kit_serials or [],
        tamper_seal_id=r.tamper_seal_id,
        status=DTPShipmentStatus(r.status),
        current_temp_celsius=r.current_temp_celsius,
        min_temp_celsius=r.min_temp_celsius,
        max_temp_celsius=r.max_temp_celsius,
        mkt_celsius=r.mkt_celsius,
        excursion_severity=ExcursionSeverity(r.excursion_severity),
        custody_handoffs=r.custody_handoffs or [],
        temperature_readings=r.temperature_readings or [],
        delivery_signature=r.delivery_signature,
        created_at=r.created_at,
    )


def _map_visit_dto(r: InHomeVisitRecord) -> InHomeVisitDTO:
    """Map DB in-home visit record to DTO."""
    return InHomeVisitDTO(
        visit_id=r.visit_id,
        study_id=r.study_id,
        site_id=r.site_id,
        subject_id=r.subject_id,
        visit_number=r.visit_number,
        visit_name=r.visit_name,
        nurse_id=r.nurse_id,
        nurse_name=r.nurse_name,
        nurse_license_number=r.nurse_license_number,
        scheduled_start_utc=r.scheduled_start_utc,
        status=InHomeVisitStatus(r.status),
        procedures=r.procedures or [],
        mobile_ecrf_data=r.mobile_ecrf_data or {},
        offline_sync_id=r.offline_sync_id,
        sync_status=r.sync_status,
        created_at=r.created_at,
    )


# =========================================================================
# WebRTC Telehealth Video Consultations (PRD-DCT-005)
# =========================================================================


@router.post(
    "/telehealth/sessions",
    response_model=TelehealthSessionResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Schedule WebRTC telehealth video visit",
)
async def schedule_telehealth_session(
    payload: TelehealthSessionCreateRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> TelehealthSessionResponse:
    """Schedule a new WebRTC telehealth video consultation session.

    @req:PRD-DCT-005
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    rec = await service.schedule_telehealth_session(
        study_id=payload.study_id,
        subject_id=payload.subject_id,
        title=payload.title,
        scheduled_start_utc=payload.scheduled_start_utc,
        site_id=payload.site_id,
        scheduled_end_utc=payload.scheduled_end_utc,
        user_id=user_id,
        user_role=role,
        change_reason=reason,
    )
    return TelehealthSessionResponse(
        success=True,
        session=_map_session_dto(rec),
        message="Telehealth video session scheduled successfully",
    )


@router.get(
    "/telehealth/sessions",
    response_model=TelehealthSessionListResponse,
    summary="List telehealth video consultation sessions",
)
async def list_telehealth_sessions(
    study_id: str | None = Query(default=None),
    subject_id: str | None = Query(default=None),
    session: AsyncSession = Depends(get_db_session),
) -> TelehealthSessionListResponse:
    """Query scheduled and active telehealth consultation sessions.

    @req:PRD-DCT-005
    @req:ADR-2199
    """
    repo = DCTRepository(session)
    records = await repo.list_telehealth_sessions(
        study_id=study_id, subject_id=subject_id
    )
    dtos = [_map_session_dto(r) for r in records]
    return TelehealthSessionListResponse(success=True, sessions=dtos, total=len(dtos))


@router.get(
    "/telehealth/sessions/{session_id}",
    response_model=TelehealthSessionResponse,
    summary="Retrieve single telehealth session details",
)
async def get_telehealth_session(
    session_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> TelehealthSessionResponse:
    """Retrieve details of a single telehealth video session by ID or room ID.

    @req:PRD-DCT-005
    @req:ADR-2199
    """
    repo = DCTRepository(session)
    record = await repo.get_telehealth_session_by_id(session_id)
    if not record:
        record = await repo.get_telehealth_session_by_room(session_id)
    if not record:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Telehealth session not found"
        )
    return TelehealthSessionResponse(success=True, session=_map_session_dto(record))


@router.post(
    "/telehealth/sessions/{session_id}/token",
    response_model=RoomTokenResponse,
    summary="Generate HMAC-SHA256 signed room authorization token",
)
async def generate_room_token(
    session_id: str,
    payload: RoomTokenRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> RoomTokenResponse:
    """Issue a role-scoped, cryptographic room authorization token for WebRTC video entry.

    @req:PRD-DCT-005
    @req:ADR-2199
    """
    user_id, role, _ = _extract_principal(request)
    service = _get_dct_service(session)
    try:
        token_str, claim_payload = await service.generate_room_token(
            session_id=session_id,
            participant_id=payload.participant_id,
            participant_role=payload.participant_role,
            display_name=payload.display_name,
            duration_minutes=payload.duration_minutes,
            user_id=user_id,
            user_role=role,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc

    return RoomTokenResponse(
        success=True,
        room_id=claim_payload.room_id,
        token=token_str,
        expires_at_utc=claim_payload.expires_at_utc,
        participant_role=claim_payload.participant_role,
        capabilities={
            "can_present": claim_payload.capabilities.can_present,
            "can_annotate": claim_payload.capabilities.can_annotate,
            "can_sign": claim_payload.capabilities.can_sign,
            "can_record": claim_payload.capabilities.can_record,
            "can_admit": claim_payload.capabilities.can_admit,
        },
    )


@router.post(
    "/telehealth/sessions/{session_id}/signaling",
    response_model=TelehealthSessionResponse,
    summary="Exchange WebRTC session signaling message",
)
async def update_session_signaling(
    session_id: str,
    payload: WebRTCSignalingRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> TelehealthSessionResponse:
    """Append WebRTC session signaling envelope (SDP offer/answer, ICE candidates).

    @req:PRD-DCT-005
    @req:ADR-2199
    """
    user_id, role, _ = _extract_principal(request)
    service = _get_dct_service(session)
    dom_payload = WebRTCSignalingPayload(
        session_id=session_id,
        sender_id=payload.sender_id,
        sender_role=payload.sender_role,
        message_type=payload.message_type,
        sdp=payload.sdp,
        candidate=payload.candidate,
        sdp_mid=payload.sdp_mid,
        sdp_m_line_index=payload.sdp_m_line_index,
        payload=payload.payload,
        timestamp_utc=datetime.now(UTC),
    )
    try:
        rec = await service.update_session_signaling(
            session_id, dom_payload, user_id=user_id, user_role=role
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return TelehealthSessionResponse(success=True, session=_map_session_dto(rec))


@router.post(
    "/telehealth/sessions/{session_id}/co-view",
    response_model=TelehealthSessionResponse,
    summary="Synchronize eConsent interactive co-viewing state",
)
async def sync_co_view_state(
    session_id: str,
    payload: CoViewStateRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> TelehealthSessionResponse:
    """Synchronize eConsent page position, active highlights, and scroll percentage.

    @req:PRD-DCT-005
    @req:ADR-2199
    """
    user_id, role, _ = _extract_principal(request)
    service = _get_dct_service(session)
    co_dom = CoViewStatePayload(
        session_id=session_id,
        document_id=payload.document_id,
        page_index=payload.page_index,
        clause_id=payload.clause_id,
        presenter_id=payload.presenter_id,
        presenter_role=payload.presenter_role,
        scroll_percent=payload.scroll_percent,
        active_highlights=payload.active_highlights,
        lock_presenter_control=payload.lock_presenter_control,
        timestamp_utc=datetime.now(UTC),
    )
    try:
        rec = await service.sync_co_view_state(
            session_id, co_dom, user_id=user_id, user_role=role
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return TelehealthSessionResponse(success=True, session=_map_session_dto(rec))


@router.post(
    "/telehealth/sessions/{session_id}/archive-recording",
    response_model=ArchiveRecordingResponse,
    summary="Archive GxP recording with SHA-256 digital seal (21 CFR Part 11)",
)
async def archive_telehealth_recording(
    session_id: str,
    payload: ArchiveRecordingRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> ArchiveRecordingResponse:
    """Archive telehealth video consultation recording with cryptographic SHA-256 digital seal.

    @req:PRD-DCT-005
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    try:
        seal = await service.archive_telehealth_recording(
            session_id=session_id,
            media_payload_or_hash=payload.media_payload_or_hash,
            duration_seconds=payload.duration_seconds,
            resolution=payload.resolution,
            recorded_by=payload.recorded_by,
            participant_manifest=payload.participant_manifest,
            user_id=user_id,
            user_role=role,
            change_reason=reason,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc

    return ArchiveRecordingResponse(
        success=True,
        recording_id=seal.recording_id,
        media_sha256_hash=seal.media_sha256_hash,
        digital_seal_signature=seal.digital_seal_signature,
        sealed_at_utc=seal.sealed_at_utc,
        part11_certification_statement=seal.part11_certification_statement,
    )


# =========================================================================
# Tiered Pediatric Assent & LAR Consent (PRD-DCT-006)
# =========================================================================


@router.post(
    "/econsent/pediatric-assent",
    response_model=PediatricAssentResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Submit tiered pediatric assent & LAR dual-signature manifest",
)
async def submit_pediatric_assent(
    payload: PediatricAssentSubmitRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> PediatricAssentResponse:
    """Submit age-appropriate pediatric assent and parent/LAR electronic signature bundle.

    @req:PRD-DCT-006
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    answers_dict = [a.model_dump() for a in payload.answers]
    try:
        bundle = await service.submit_pediatric_assent(
            study_id=payload.study_id,
            subject_id=payload.subject_id,
            minor_age=payload.minor_age,
            answers=answers_dict,
            assent_signature=payload.assent_signature,
            lar_name=payload.lar_name,
            lar_relationship=payload.lar_relationship,
            lar_legal_basis=payload.lar_legal_basis,
            lar_signature=payload.lar_signature,
            investigator_name=payload.investigator_name,
            investigator_signature=payload.investigator_signature,
            user_id=user_id,
            user_role=role,
            change_reason=reason,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY, detail=str(exc)
        ) from exc

    dto = PediatricAssentDTO(
        assent_id=bundle.assent_id,
        study_id=bundle.study_id,
        subject_id=bundle.subject_id,
        tier=bundle.tier,
        minor_age=bundle.minor_age,
        comprehension_score_percent=bundle.comprehension_score_percent,
        comprehension_passed=bundle.comprehension_passed,
        assent_signature=bundle.assent_signature,
        lar_name=bundle.lar_name,
        lar_relationship=bundle.lar_relationship.value
        if bundle.lar_relationship
        else None,
        lar_legal_basis=bundle.lar_legal_basis,
        lar_signature=bundle.lar_signature,
        investigator_name=bundle.investigator_name,
        investigator_signature=bundle.investigator_signature,
        cryptographic_bundle_hash=bundle.cryptographic_bundle_hash,
        created_at=datetime.now(UTC),
    )
    return PediatricAssentResponse(
        success=True,
        assent=dto,
        message="Pediatric assent executed and cryptographically sealed",
    )


@router.get(
    "/econsent/pediatric-assent",
    response_model=PediatricAssentListResponse,
    summary="List executed pediatric assents",
)
async def list_pediatric_assents(
    study_id: str | None = Query(default=None),
    subject_id: str | None = Query(default=None),
    session: AsyncSession = Depends(get_db_session),
) -> PediatricAssentListResponse:
    """Query executed pediatric assent and LAR consent records.

    @req:PRD-DCT-006
    @req:ADR-2199
    """
    repo = DCTRepository(session)
    records = await repo.list_pediatric_assents(
        study_id=study_id, subject_id=subject_id
    )
    dtos = [_map_assent_dto(r) for r in records]
    return PediatricAssentListResponse(success=True, assents=dtos, total=len(dtos))


@router.get(
    "/econsent/pediatric-assent/{assent_id}/verify",
    response_model=VerifyAssentResponse,
    summary="Verify cryptographic bundle seal of pediatric assent",
)
async def verify_pediatric_assent(
    assent_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> VerifyAssentResponse:
    """Validate cryptographic SHA-256 seal integrity for pediatric assent bundle.

    @req:PRD-DCT-006
    @req:ADR-2199
    """
    service = _get_dct_service(session)
    is_valid = await service.verify_pediatric_assent(assent_id)
    repo = DCTRepository(session)
    record = await repo.get_pediatric_assent_by_id(assent_id)
    if not record:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Pediatric assent not found"
        )
    return VerifyAssentResponse(
        assent_id=record.assent_id,
        is_valid=is_valid,
        bundle_hash=record.cryptographic_bundle_hash,
    )


# =========================================================================
# Direct-to-Patient (DTP) Logistics & Cold Chain (PRD-DCT-003)
# =========================================================================


@router.post(
    "/dtp/shipments",
    response_model=DTPShipmentResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Create Direct-to-Patient (DTP) courier dispatch order",
)
async def create_dtp_shipment(
    payload: DTPShipmentCreateRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> DTPShipmentResponse:
    """Create a new Direct-to-Patient courier drug shipment with temperature-controlled profile.

    @req:PRD-DCT-003
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    rec = await service.create_dtp_shipment(
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_pseudonym_id=payload.subject_pseudonym_id,
        destination_address_pseudonym=payload.destination_address_pseudonym,
        courier_name=payload.courier_name,
        temperature_zone=payload.temperature_zone,
        kit_serials=payload.kit_serials,
        tamper_seal_id=payload.tamper_seal_id,
        user_id=user_id,
        user_role=role,
        change_reason=reason,
    )
    return DTPShipmentResponse(
        success=True,
        shipment=_map_shipment_dto(rec),
        message="DTP shipment order created",
    )


@router.get(
    "/dtp/shipments",
    response_model=DTPShipmentListResponse,
    summary="List Direct-to-Patient drug shipments",
)
async def list_dtp_shipments(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    subject_pseudonym_id: str | None = Query(default=None),
    session: AsyncSession = Depends(get_db_session),
) -> DTPShipmentListResponse:
    """Query DTP shipments with cold-chain indicators and tracking states.

    @req:PRD-DCT-003
    @req:ADR-2199
    """
    repo = DCTRepository(session)
    records = await repo.list_dtp_shipments(
        study_id=study_id, site_id=site_id, subject_pseudonym_id=subject_pseudonym_id
    )
    dtos = [_map_shipment_dto(r) for r in records]
    return DTPShipmentListResponse(success=True, shipments=dtos, total=len(dtos))


@router.get(
    "/dtp/shipments/{tracking_number}",
    response_model=DTPShipmentResponse,
    summary="Retrieve single DTP shipment details by tracking number",
)
async def get_dtp_shipment(
    tracking_number: str,
    session: AsyncSession = Depends(get_db_session),
) -> DTPShipmentResponse:
    """Retrieve details, temperature telemetry, and Chain of Custody for a DTP shipment.

    @req:PRD-DCT-003
    @req:ADR-2199
    """
    repo = DCTRepository(session)
    record = await repo.get_dtp_shipment_by_tracking(tracking_number)
    if not record:
        record = await repo.get_dtp_shipment_by_id(tracking_number)
    if not record:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="DTP shipment not found"
        )
    return DTPShipmentResponse(success=True, shipment=_map_shipment_dto(record))


@router.post(
    "/dtp/shipments/{tracking_number}/temperature",
    response_model=DTPShipmentResponse,
    summary="Ingest cold-chain temperature telemetry points & calculate MKT",
)
async def ingest_dtp_temperature(
    tracking_number: str,
    payload: TemperatureIngestRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> DTPShipmentResponse:
    """Ingest cold chain logger data points, compute Arrhenius MKT, and flag excursions.

    @req:PRD-DCT-003
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    domain_readings = [
        TemperatureReading(
            timestamp_utc=r.timestamp_utc,
            temperature_celsius=r.temperature_celsius,
            logger_id=r.logger_id,
            battery_percent=r.battery_percent,
        )
        for r in payload.readings
    ]
    try:
        rec = await service.ingest_dtp_temperature_readings(
            tracking_number=tracking_number,
            readings=domain_readings,
            user_id=user_id,
            user_role=role,
            change_reason=reason,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return DTPShipmentResponse(success=True, shipment=_map_shipment_dto(rec))


@router.post(
    "/dtp/shipments/{tracking_number}/handoff",
    response_model=DTPShipmentResponse,
    summary="Record Chain of Custody (CoC) handoff event",
)
async def record_dtp_handoff(
    tracking_number: str,
    payload: CustodyHandoffRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> DTPShipmentResponse:
    """Record signed Chain of Custody handoff across depot, courier hubs, and final mile.

    @req:PRD-DCT-003
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    try:
        rec = await service.record_dtp_custody_handoff(
            tracking_number=tracking_number,
            stage=payload.stage,
            released_by=payload.released_by,
            received_by=payload.received_by,
            tamper_seal_intact=payload.tamper_seal_intact,
            current_temperature_celsius=payload.current_temperature_celsius,
            notes=payload.notes,
            user_id=user_id,
            user_role=role,
            change_reason=reason,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return DTPShipmentResponse(success=True, shipment=_map_shipment_dto(rec))


@router.post(
    "/dtp/shipments/{tracking_number}/delivery",
    response_model=DTPShipmentResponse,
    summary="Confirm final delivery with recipient signature",
)
async def confirm_dtp_delivery(
    tracking_number: str,
    payload: DeliveryConfirmRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> DTPShipmentResponse:
    """Confirm delivery at subject residence with recipient signature and greenlight checks.

    @req:PRD-DCT-003
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    try:
        rec = await service.confirm_dtp_delivery(
            tracking_number=tracking_number,
            recipient_name=payload.recipient_name,
            signature_manifestation=payload.signature_manifestation,
            tamper_seal_verified=payload.tamper_seal_verified,
            logger_greenlight=payload.logger_greenlight,
            user_id=user_id,
            user_role=role,
            change_reason=reason,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return DTPShipmentResponse(success=True, shipment=_map_shipment_dto(rec))


# =========================================================================
# Decentralized In-Home Healthcare Nurse Visits (PRD-DCT-004)
# =========================================================================


@router.post(
    "/home-health/visits",
    response_model=InHomeVisitResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Schedule decentralized in-home nursing visit",
)
async def schedule_in_home_visit(
    payload: InHomeVisitCreateRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> InHomeVisitResponse:
    """Schedule visiting healthcare nurse appointment with protocol procedure tasks.

    @req:PRD-DCT-004
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    procedures_raw = [p.model_dump() for p in payload.procedures]
    rec = await service.schedule_in_home_visit(
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_id=payload.subject_id,
        visit_number=payload.visit_number,
        visit_name=payload.visit_name,
        nurse_id=payload.nurse_id,
        nurse_name=payload.nurse_name,
        nurse_license_number=payload.nurse_license_number,
        scheduled_start_utc=payload.scheduled_start_utc,
        procedures=procedures_raw,
        user_id=user_id,
        user_role=role,
        change_reason=reason,
    )
    return InHomeVisitResponse(
        success=True,
        visit=_map_visit_dto(rec),
        message="In-home nurse visit scheduled successfully",
    )


@router.get(
    "/home-health/visits",
    response_model=InHomeVisitListResponse,
    summary="List in-home visiting clinician appointments",
)
async def list_in_home_visits(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    nurse_id: str | None = Query(default=None),
    subject_id: str | None = Query(default=None),
    session: AsyncSession = Depends(get_db_session),
) -> InHomeVisitListResponse:
    """Query in-home nursing visits by study, site, nurse, or subject.

    @req:PRD-DCT-004
    @req:ADR-2199
    """
    repo = DCTRepository(session)
    records = await repo.list_in_home_visits(
        study_id=study_id, site_id=site_id, nurse_id=nurse_id, subject_id=subject_id
    )
    dtos = [_map_visit_dto(r) for r in records]
    return InHomeVisitListResponse(success=True, visits=dtos, total=len(dtos))


@router.get(
    "/home-health/visits/{visit_id}",
    response_model=InHomeVisitResponse,
    summary="Retrieve single in-home visit details",
)
async def get_in_home_visit(
    visit_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> InHomeVisitResponse:
    """Retrieve in-home nursing visit details, procedure progress, and mobile eCRF data.

    @req:PRD-DCT-004
    @req:ADR-2199
    """
    repo = DCTRepository(session)
    record = await repo.get_in_home_visit_by_id(visit_id)
    if not record:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="In-home visit not found"
        )
    return InHomeVisitResponse(success=True, visit=_map_visit_dto(record))


@router.post(
    "/home-health/visits/{visit_id}/status",
    response_model=InHomeVisitResponse,
    summary="Update visiting nurse visit progress and completed procedures",
)
async def update_in_home_visit_status(
    visit_id: str,
    payload: InHomeVisitStatusUpdateRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> InHomeVisitResponse:
    """Update in-home visit status (EN_ROUTE, ARRIVED_ON_SITE, PROCEDURES_IN_PROGRESS, etc.).

    @req:PRD-DCT-004
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    procedures_raw = (
        [p.model_dump() for p in payload.procedures]
        if payload.procedures is not None
        else None
    )
    try:
        rec = await service.update_in_home_visit_status(
            visit_id=visit_id,
            status=payload.status,
            procedures=procedures_raw,
            user_id=user_id,
            user_role=role,
            change_reason=reason,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return InHomeVisitResponse(success=True, visit=_map_visit_dto(rec))


@router.post(
    "/home-health/visits/{visit_id}/sync-ecrf",
    response_model=InHomeVisitResponse,
    summary="Reconcile offline-cached mobile eCRF clinical data entry",
)
async def reconcile_mobile_ecrf_sync(
    visit_id: str,
    payload: MobileECRFSyncRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> InHomeVisitResponse:
    """Reconcile mobile eCRF clinical data entered offline by visiting nurse.

    @req:PRD-DCT-004
    @req:ADR-2199
    """
    user_id, role, reason = _extract_principal(request)
    service = _get_dct_service(session)
    client_dict = payload.model_dump()
    try:
        rec = await service.reconcile_mobile_ecrf_offline_sync(
            visit_id=visit_id,
            client_payload=client_dict,
            client_sync_id=payload.client_sync_id,
            user_id=user_id,
            user_role=role,
            change_reason=reason,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return InHomeVisitResponse(
        success=True,
        visit=_map_visit_dto(rec),
        message="Mobile eCRF data synchronized successfully",
    )


# =========================================================================
# Overview Dashboard Metrics
# =========================================================================


@router.get(
    "/metrics/{study_id}",
    response_model=DCTMetricsResponse,
    summary="Retrieve aggregated DCT operational metrics",
)
async def get_dct_metrics(
    study_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> DCTMetricsResponse:
    """Retrieve real-time DCT dashboard KPI metrics for study.

    @req:PRD-DCT-003
    @req:PRD-DCT-004
    @req:PRD-DCT-005
    @req:PRD-DCT-006
    @req:ADR-2199
    """
    service = _get_dct_service(session)
    metrics = await service.get_dct_dashboard_metrics(study_id)
    return DCTMetricsResponse(
        success=True,
        study_id=study_id,
        total_telehealth_sessions=metrics.total_telehealth_sessions,
        active_video_rooms=metrics.active_video_rooms,
        total_dtp_shipments=metrics.total_dtp_shipments,
        active_cold_chain_shipments=metrics.active_cold_chain_shipments,
        temperature_excursion_alerts=metrics.temperature_excursion_alerts,
        scheduled_nurse_visits=metrics.scheduled_nurse_visits,
        completed_home_visits=metrics.completed_home_visits,
        pending_offline_syncs=metrics.pending_offline_syncs,
        pediatric_assents_completed=metrics.pediatric_assents_completed,
    )
