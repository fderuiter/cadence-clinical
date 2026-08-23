"""Application service coordinating DCT Telehealth, Pediatric Assent, DTP Logistics, and Home Health.

Requirements:
- PRD-DCT-003: Direct-to-Patient (DTP) Investigational Product Dispatch Tracking
- PRD-DCT-004: Decentralized In-Home Healthcare Nurse Visit Coordination
- PRD-DCT-005: GxP-Compliant Telehealth Video Consultation & Encrypted Recording
- PRD-DCT-006: Tiered Pediatric Assent & Legally Authorized Representative (LAR) Consent
- ADR-2199 / ADR-2207, 21 CFR Part 11
"""

from __future__ import annotations

import os
import secrets
import uuid
from datetime import UTC, datetime
from typing import Any

from apps.interop.domain.dct_models import (
    CoViewStatePayload,
    DCTDashboardMetrics,
    ExcursionSeverity,
    GxPRecordingSeal,
    InHomeVisitStatus,
    LARRelationship,
    ParticipantRole,
    PediatricAssentPayload,
    PediatricAssentTier,
    RoomTokenPayload,
    SignalingMessageType,
    TemperatureReading,
    TemperatureZone,
    WebRTCSignalingPayload,
)
from apps.interop.domain.dct_processor import (
    create_custody_handoff_event,
    create_gxp_recording_digital_seal,
    create_pediatric_assent_bundle,
    determine_pediatric_tier,
    evaluate_pediatric_comprehension,
    evaluate_temperature_excursions,
    generate_room_access_token,
    reconcile_mobile_ecrf_offline_payload,
    verify_pediatric_assent_bundle,
)
from apps.interop.domain.ports import IDCTRepositoryPort
from apps.interop.infrastructure.dct_models import (
    DTPShipmentRecord,
    InHomeVisitRecord,
)


class DCTService:
    """Orchestrates decentralized clinical trial operations with GxP compliance."""

    def __init__(self, repository: IDCTRepositoryPort) -> None:
        self.repository = repository
        self.secret_key = os.getenv(
            "GATEWAY_SECRET", "cadence-clinical-gxp-secret-key-2026"
        )

    # =========================================================================
    # WebRTC Telehealth Video Consultations
    # =========================================================================

    async def schedule_telehealth_session(
        self,
        study_id: str,
        subject_id: str,
        title: str,
        scheduled_start_utc: datetime,
        site_id: str | None = None,
        scheduled_end_utc: datetime | None = None,
        user_id: str = "system",
        user_role: str = "investigator",
        change_reason: str = "Schedule telehealth clinical visit",
    ) -> Any:
        """Schedule a new WebRTC telehealth video visit with room security secret."""
        room_id = f"room-dct-{uuid.uuid4().hex[:12]}"
        room_secret = secrets.token_urlsafe(32)

        record_dict = {
            "room_id": room_id,
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": subject_id,
            "title": title,
            "scheduled_start_utc": scheduled_start_utc,
            "scheduled_end_utc": scheduled_end_utc,
            "status": "SCHEDULED",
            "room_secret": room_secret,
            "co_view_state": {},
            "signaling_messages": [],
            "participant_records": [],
            "created_by": user_id,
            "reason_for_change": change_reason,
        }

        persisted = await self.repository.create_telehealth_session(record_dict)

        await self.repository.write_dct_audit_log(
            study_id=study_id,
            action="SCHEDULE_TELEHEALTH_SESSION",
            entity_type="TELEHEALTH_SESSION",
            entity_id=persisted.id,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
            details={"room_id": room_id, "title": title, "subject_id": subject_id},
        )
        return persisted

    async def generate_room_token(
        self,
        session_id: str,
        participant_id: str,
        participant_role: ParticipantRole,
        display_name: str,
        duration_minutes: int = 120,
        user_id: str = "system",
        user_role: str = "investigator",
    ) -> tuple[str, RoomTokenPayload]:
        """Generate room authorization token for participant."""
        session = await self.repository.get_telehealth_session_by_id(session_id)
        if not session:
            # Fallback check by room_id
            session = await self.repository.get_telehealth_session_by_room(session_id)
        if not session:
            raise ValueError(f"Telehealth session '{session_id}' not found")

        token_str, payload = generate_room_access_token(
            secret=session.room_secret,
            room_id=session.room_id,
            session_id=session.id,
            study_id=session.study_id,
            subject_id=session.subject_id,
            participant_id=participant_id,
            role=participant_role,
            display_name=display_name,
            duration_minutes=duration_minutes,
        )

        # Update participant list
        participants = list(session.participant_records or [])
        participants.append(
            {
                "participant_id": participant_id,
                "role": participant_role.value,
                "display_name": display_name,
                "joined_at_utc": datetime.now(UTC).isoformat(),
            }
        )
        session.participant_records = participants
        if session.status == "SCHEDULED":
            session.status = "WAITING_ROOM"
        await self.repository.update_telehealth_session(session)

        return token_str, payload

    async def update_session_signaling(
        self,
        session_id: str,
        signaling_payload: WebRTCSignalingPayload,
        user_id: str = "system",
        user_role: str = "investigator",
    ) -> Any:
        """Append WebRTC signaling message (offer, answer, ICE candidates)."""
        session = await self.repository.get_telehealth_session_by_id(session_id)
        if not session:
            session = await self.repository.get_telehealth_session_by_room(session_id)
        if not session:
            raise ValueError(f"Telehealth session '{session_id}' not found")

        msgs = list(session.signaling_messages or [])
        msgs.append(
            {
                "sender_id": signaling_payload.sender_id,
                "sender_role": signaling_payload.sender_role.value,
                "message_type": signaling_payload.message_type.value,
                "sdp": signaling_payload.sdp,
                "candidate": signaling_payload.candidate,
                "sdp_mid": signaling_payload.sdp_mid,
                "sdp_m_line_index": signaling_payload.sdp_m_line_index,
                "payload": signaling_payload.payload,
                "timestamp_utc": signaling_payload.timestamp_utc.isoformat(),
            }
        )
        session.signaling_messages = msgs
        if session.status in (
            "SCHEDULED",
            "WAITING_ROOM",
        ) and signaling_payload.message_type in (
            SignalingMessageType.OFFER,
            SignalingMessageType.ANSWER,
        ):
            session.status = "IN_PROGRESS"

        return await self.repository.update_telehealth_session(session)

    async def sync_co_view_state(
        self,
        session_id: str,
        co_view_payload: CoViewStatePayload,
        user_id: str = "system",
        user_role: str = "investigator",
    ) -> Any:
        """Update synchronized eConsent co-viewing position and annotations."""
        session = await self.repository.get_telehealth_session_by_id(session_id)
        if not session:
            session = await self.repository.get_telehealth_session_by_room(session_id)
        if not session:
            raise ValueError(f"Telehealth session '{session_id}' not found")

        session.co_view_state = {
            "document_id": co_view_payload.document_id,
            "page_index": co_view_payload.page_index,
            "clause_id": co_view_payload.clause_id,
            "presenter_id": co_view_payload.presenter_id,
            "presenter_role": co_view_payload.presenter_role.value,
            "scroll_percent": co_view_payload.scroll_percent,
            "active_highlights": co_view_payload.active_highlights,
            "lock_presenter_control": co_view_payload.lock_presenter_control,
            "updated_at_utc": co_view_payload.timestamp_utc.isoformat(),
        }
        return await self.repository.update_telehealth_session(session)

    async def archive_telehealth_recording(
        self,
        session_id: str,
        media_payload_or_hash: str,
        duration_seconds: int,
        resolution: str,
        recorded_by: str,
        participant_manifest: list[dict[str, Any]],
        user_id: str = "system",
        user_role: str = "investigator",
        change_reason: str = "GxP 21 CFR Part 11 Telehealth Session Recording Archival",
    ) -> GxPRecordingSeal:
        """Archive telehealth recording with SHA-256 digital seal and 21 CFR Part 11 audit."""
        session = await self.repository.get_telehealth_session_by_id(session_id)
        if not session:
            session = await self.repository.get_telehealth_session_by_room(session_id)
        if not session:
            raise ValueError(f"Telehealth session '{session_id}' not found")

        recording_id = f"REC-GXP-{uuid.uuid4().hex[:12].upper()}"
        seal = create_gxp_recording_digital_seal(
            secret=session.room_secret,
            recording_id=recording_id,
            session_id=session.id,
            study_id=session.study_id,
            media_payload_or_hash=media_payload_or_hash,
            duration_seconds=duration_seconds,
            resolution=resolution,
            recorded_by=recorded_by,
            participant_manifest=participant_manifest,
        )

        session.recording_seal_metadata = {
            "recording_id": seal.recording_id,
            "duration_seconds": seal.duration_seconds,
            "resolution": seal.resolution,
            "media_sha256_hash": seal.media_sha256_hash,
            "digital_seal_signature": seal.digital_seal_signature,
            "recorded_by": seal.recorded_by,
            "sealed_at_utc": seal.sealed_at_utc.isoformat(),
            "part11_certification_statement": seal.part11_certification_statement,
        }
        session.status = "COMPLETED"
        await self.repository.update_telehealth_session(session)

        await self.repository.write_dct_audit_log(
            study_id=session.study_id,
            action="ARCHIVE_TELEHEALTH_RECORDING",
            entity_type="GXP_RECORDING_SEAL",
            entity_id=recording_id,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
            details={
                "session_id": session.id,
                "media_sha256": seal.media_sha256_hash,
                "duration_seconds": duration_seconds,
            },
        )
        return seal

    # =========================================================================
    # Tiered Pediatric Assent & LAR Consent
    # =========================================================================

    async def submit_pediatric_assent(
        self,
        study_id: str,
        subject_id: str,
        minor_age: int,
        answers: list[dict[str, Any]],
        assent_signature: str | None,
        lar_name: str | None,
        lar_relationship: LARRelationship | None,
        lar_legal_basis: str | None,
        lar_signature: str | None,
        investigator_name: str | None,
        investigator_signature: str | None,
        user_id: str = "system",
        user_role: str = "investigator",
        change_reason: str = "Execute pediatric assent and LAR consent",
    ) -> PediatricAssentPayload:
        """Evaluate comprehension, assemble multi-tier signatures, and persist sealed assent."""
        tier = determine_pediatric_tier(minor_age)
        score_percent, passed = evaluate_pediatric_comprehension(tier, answers)

        bundle = create_pediatric_assent_bundle(
            study_id=study_id,
            subject_id=subject_id,
            tier=tier,
            minor_age=minor_age,
            comprehension_score_percent=score_percent,
            assent_signature=assent_signature,
            lar_name=lar_name,
            lar_relationship=lar_relationship,
            lar_legal_basis=lar_legal_basis,
            lar_signature=lar_signature,
            investigator_name=investigator_name,
            investigator_signature=investigator_signature,
        )

        record_dict = {
            "assent_id": bundle.assent_id,
            "study_id": study_id,
            "subject_id": subject_id,
            "tier": bundle.tier.value,
            "minor_age": minor_age,
            "comprehension_score_percent": score_percent,
            "comprehension_passed": passed,
            "assent_signature": assent_signature,
            "assent_signed_at": bundle.assent_signed_at_utc,
            "lar_name": lar_name,
            "lar_relationship": lar_relationship.value if lar_relationship else None,
            "lar_legal_basis": lar_legal_basis,
            "lar_signature": lar_signature,
            "lar_signed_at": bundle.lar_signed_at_utc,
            "investigator_name": investigator_name,
            "investigator_signature": investigator_signature,
            "investigator_signed_at": bundle.investigator_signed_at_utc,
            "cryptographic_bundle_hash": bundle.cryptographic_bundle_hash,
            "created_by": user_id,
            "reason_for_change": change_reason,
        }

        await self.repository.create_pediatric_assent(record_dict)

        await self.repository.write_dct_audit_log(
            study_id=study_id,
            action="EXECUTE_PEDIATRIC_ASSENT",
            entity_type="PEDIATRIC_ASSENT",
            entity_id=bundle.assent_id,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
            details={
                "tier": tier.value,
                "minor_age": minor_age,
                "score": score_percent,
                "bundle_hash": bundle.cryptographic_bundle_hash,
            },
        )
        return bundle

    async def verify_pediatric_assent(self, assent_id: str) -> bool:
        """Verify cryptographic bundle seal of persisted pediatric assent."""
        record = await self.repository.get_pediatric_assent_by_id(assent_id)
        if not record:
            return False

        bundle = PediatricAssentPayload(
            assent_id=record.assent_id,
            study_id=record.study_id,
            subject_id=record.subject_id,
            tier=PediatricAssentTier(record.tier),
            minor_age=record.minor_age,
            comprehension_score_percent=record.comprehension_score_percent,
            comprehension_passed=record.comprehension_passed,
            assent_signature=record.assent_signature,
            assent_signed_at_utc=record.assent_signed_at,
            lar_name=record.lar_name,
            lar_relationship=LARRelationship(record.lar_relationship)
            if record.lar_relationship
            else None,
            lar_legal_basis=record.lar_legal_basis,
            lar_signature=record.lar_signature,
            lar_signed_at_utc=record.lar_signed_at,
            investigator_name=record.investigator_name,
            investigator_signature=record.investigator_signature,
            investigator_signed_at_utc=record.investigator_signed_at,
            cryptographic_bundle_hash=record.cryptographic_bundle_hash,
        )
        return verify_pediatric_assent_bundle(bundle)

    # =========================================================================
    # Direct-to-Patient (DTP) Logistics & Cold Chain
    # =========================================================================

    async def create_dtp_shipment(
        self,
        study_id: str,
        site_id: str,
        subject_pseudonym_id: str,
        destination_address_pseudonym: str,
        courier_name: str,
        temperature_zone: TemperatureZone,
        kit_serials: list[str],
        tamper_seal_id: str,
        user_id: str = "system",
        user_role: str = "site_crc",
        change_reason: str = "Dispatch Direct-to-Patient drug shipment",
    ) -> DTPShipmentRecord:
        """Create a new DTP courier dispatch order with temperature condition profile."""
        shipment_id = f"DTP-{uuid.uuid4().hex[:10].upper()}"
        tracking_number = f"CAD-EXP-{secrets.randbelow(899999) + 100000}"

        # Initialize base temperature reading
        min_lim, max_lim = {
            TemperatureZone.CRYOGENIC: (-196.0, -150.0),
            TemperatureZone.ULTRA_COLD: (-80.0, -60.0),
            TemperatureZone.FROZEN: (-25.0, -15.0),
            TemperatureZone.REFRIGERATED: (2.0, 8.0),
            TemperatureZone.CONTROLLED_AMBIENT: (15.0, 25.0),
        }.get(temperature_zone, (15.0, 25.0))
        mid_temp = round((min_lim + max_lim) / 2.0, 1)

        initial_reading = {
            "timestamp_utc": datetime.now(UTC).isoformat(),
            "temperature_celsius": mid_temp,
            "logger_id": f"LOG-{tamper_seal_id[:8]}",
            "battery_percent": 100.0,
        }

        record = DTPShipmentRecord(
            shipment_id=shipment_id,
            tracking_number=tracking_number,
            courier_name=courier_name,
            study_id=study_id,
            site_id=site_id,
            subject_pseudonym_id=subject_pseudonym_id,
            destination_address_pseudonym=destination_address_pseudonym,
            temperature_zone=temperature_zone.value,
            kit_serials=kit_serials,
            tamper_seal_id=tamper_seal_id,
            status="ORDER_CREATED",
            current_temp_celsius=mid_temp,
            min_temp_celsius=mid_temp,
            max_temp_celsius=mid_temp,
            mkt_celsius=mid_temp,
            excursion_severity="NONE",
            custody_handoffs=[],
            temperature_readings=[initial_reading],
            delivery_signature=None,
            created_by=user_id,
            reason_for_change=change_reason,
        )

        persisted = await self.repository.create_dtp_shipment(record)
        await self.repository.write_dct_audit_log(
            study_id=study_id,
            action="CREATE_DTP_SHIPMENT",
            entity_type="DTP_SHIPMENT",
            entity_id=persisted.id,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
            details={
                "tracking_number": tracking_number,
                "zone": temperature_zone.value,
            },
        )
        return persisted

    async def ingest_dtp_temperature_readings(
        self,
        tracking_number: str,
        readings: list[TemperatureReading],
        user_id: str = "system",
        user_role: str = "courier",
        change_reason: str = "Ingest cold chain logger data",
    ) -> DTPShipmentRecord:
        """Ingest temperature data points, compute MKT, and flag excursions."""
        shipment = await self.repository.get_dtp_shipment_by_tracking(tracking_number)
        if not shipment:
            raise ValueError(
                f"DTP shipment with tracking '{tracking_number}' not found"
            )

        current_readings_raw = list(shipment.temperature_readings or [])
        for r in readings:
            current_readings_raw.append(
                {
                    "timestamp_utc": r.timestamp_utc.isoformat(),
                    "temperature_celsius": r.temperature_celsius,
                    "logger_id": r.logger_id,
                    "battery_percent": r.battery_percent,
                }
            )

        all_domain_readings = [
            TemperatureReading(
                timestamp_utc=datetime.fromisoformat(r["timestamp_utc"]),
                temperature_celsius=float(r["temperature_celsius"]),
                logger_id=r.get("logger_id", "LOGGER-01"),
                battery_percent=float(r.get("battery_percent", 100.0)),
            )
            for r in current_readings_raw
        ]

        zone = TemperatureZone(shipment.temperature_zone)
        min_t, max_t, mkt_t, severity = evaluate_temperature_excursions(
            zone, all_domain_readings
        )

        shipment.temperature_readings = current_readings_raw
        shipment.current_temp_celsius = (
            readings[-1].temperature_celsius
            if readings
            else shipment.current_temp_celsius
        )
        shipment.min_temp_celsius = min_t
        shipment.max_temp_celsius = max_t
        shipment.mkt_celsius = mkt_t
        shipment.excursion_severity = severity.value

        if severity == ExcursionSeverity.CRITICAL_EXCURSION_QUARANTINE:
            shipment.status = "TEMP_EXCURSION_HOLD"

        return await self.repository.update_dtp_shipment(shipment)

    async def record_dtp_custody_handoff(
        self,
        tracking_number: str,
        stage: str,
        released_by: str,
        received_by: str,
        tamper_seal_intact: bool,
        current_temperature_celsius: float,
        notes: str = "",
        user_id: str = "system",
        user_role: str = "courier",
        change_reason: str = "Record Chain of Custody handoff",
    ) -> DTPShipmentRecord:
        """Record an immutable Chain of Custody handoff in shipment lifecycle."""
        shipment = await self.repository.get_dtp_shipment_by_tracking(tracking_number)
        if not shipment:
            raise ValueError(
                f"DTP shipment with tracking '{tracking_number}' not found"
            )

        handoff = create_custody_handoff_event(
            stage=stage,
            released_by=released_by,
            received_by=received_by,
            tamper_seal_intact=tamper_seal_intact,
            current_temperature_celsius=current_temperature_celsius,
            notes=notes,
            secret_key=self.secret_key,
        )

        handoffs = list(shipment.custody_handoffs or [])
        handoffs.append(
            {
                "handoff_id": handoff.handoff_id,
                "stage": handoff.stage,
                "released_by": handoff.released_by,
                "received_by": handoff.received_by,
                "timestamp_utc": handoff.timestamp_utc.isoformat(),
                "tamper_seal_intact": handoff.tamper_seal_intact,
                "current_temperature_celsius": handoff.current_temperature_celsius,
                "notes": handoff.notes,
                "signature_manifestation": handoff.signature_manifestation,
            }
        )
        shipment.custody_handoffs = handoffs

        if stage == "COURIER_PICKUP":
            shipment.status = "COURIER_DISPATCHED"
        elif stage == "TRANSIT_HUB":
            shipment.status = "IN_TRANSIT"
        elif stage == "FINAL_MILE":
            shipment.status = "OUT_FOR_DELIVERY"

        return await self.repository.update_dtp_shipment(shipment)

    async def confirm_dtp_delivery(
        self,
        tracking_number: str,
        recipient_name: str,
        signature_manifestation: str,
        tamper_seal_verified: bool = True,
        logger_greenlight: bool = True,
        user_id: str = "system",
        user_role: str = "courier",
        change_reason: str = "Confirm final delivery to subject residence",
    ) -> DTPShipmentRecord:
        """Confirm final mile delivery with recipient signature and logger verification."""
        shipment = await self.repository.get_dtp_shipment_by_tracking(tracking_number)
        if not shipment:
            raise ValueError(
                f"DTP shipment with tracking '{tracking_number}' not found"
            )

        shipment.delivery_signature = {
            "recipient_name": recipient_name,
            "signature_manifestation": signature_manifestation,
            "tamper_seal_verified": tamper_seal_verified,
            "logger_greenlight": logger_greenlight,
            "delivered_at_utc": datetime.now(UTC).isoformat(),
        }
        shipment.status = "DELIVERED"
        return await self.repository.update_dtp_shipment(shipment)

    # =========================================================================
    # Decentralized In-Home Healthcare Nurse Visits
    # =========================================================================

    async def schedule_in_home_visit(
        self,
        study_id: str,
        site_id: str,
        subject_id: str,
        visit_number: str,
        visit_name: str,
        nurse_id: str,
        nurse_name: str,
        nurse_license_number: str,
        scheduled_start_utc: datetime,
        procedures: list[dict[str, Any]],
        user_id: str = "system",
        user_role: str = "site_crc",
        change_reason: str = "Schedule in-home nursing visit",
    ) -> InHomeVisitRecord:
        """Schedule in-home visiting clinician appointment with protocol task checklist."""
        visit_id = f"VISIT-HOME-{uuid.uuid4().hex[:8].upper()}"

        record = InHomeVisitRecord(
            visit_id=visit_id,
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_number=visit_number,
            visit_name=visit_name,
            nurse_id=nurse_id,
            nurse_name=nurse_name,
            nurse_license_number=nurse_license_number,
            scheduled_start_utc=scheduled_start_utc,
            status="SCHEDULED",
            procedures=procedures,
            mobile_ecrf_data={},
            offline_sync_id=None,
            sync_status="SYNCHRONIZED",
            created_by=user_id,
            reason_for_change=change_reason,
        )

        persisted = await self.repository.create_in_home_visit(record)
        await self.repository.write_dct_audit_log(
            study_id=study_id,
            action="SCHEDULE_IN_HOME_VISIT",
            entity_type="IN_HOME_VISIT",
            entity_id=persisted.id,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
            details={"visit_id": visit_id, "nurse_name": nurse_name},
        )
        return persisted

    async def update_in_home_visit_status(
        self,
        visit_id: str,
        status: InHomeVisitStatus,
        procedures: list[dict[str, Any]] | None = None,
        user_id: str = "system",
        user_role: str = "nurse",
        change_reason: str = "Update visiting nurse visit progress",
    ) -> InHomeVisitRecord:
        """Update home healthcare visit status (EN_ROUTE, ARRIVED, PROCEDURES_IN_PROGRESS, etc.)."""
        visit = await self.repository.get_in_home_visit_by_id(visit_id)
        if not visit:
            raise ValueError(f"In-home visit '{visit_id}' not found")

        visit.status = status.value
        if procedures is not None:
            visit.procedures = procedures

        return await self.repository.update_in_home_visit(visit)

    async def reconcile_mobile_ecrf_offline_sync(
        self,
        visit_id: str,
        client_payload: dict[str, Any],
        client_sync_id: str,
        user_id: str = "system",
        user_role: str = "nurse",
        change_reason: str = "Synchronize mobile eCRF offline cached data",
    ) -> InHomeVisitRecord:
        """Reconcile in-home mobile eCRF data entry collected offline."""
        visit = await self.repository.get_in_home_visit_by_id(visit_id)
        if not visit:
            raise ValueError(f"In-home visit '{visit_id}' not found")

        reconciled_data, sync_status = reconcile_mobile_ecrf_offline_payload(
            server_state=visit.mobile_ecrf_data,
            client_payload=client_payload,
            client_sync_id=client_sync_id,
        )

        visit.mobile_ecrf_data = reconciled_data
        visit.offline_sync_id = client_sync_id
        visit.sync_status = sync_status
        if visit.status != "VISIT_COMPLETED":
            visit.status = "VISIT_COMPLETED"

        updated = await self.repository.update_in_home_visit(visit)
        await self.repository.write_dct_audit_log(
            study_id=visit.study_id,
            action="RECONCILE_MOBILE_ECRF_OFFLINE_SYNC",
            entity_type="IN_HOME_VISIT",
            entity_id=visit.id,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
            details={
                "sync_id": client_sync_id,
                "form_keys": list(client_payload.get("data", {}).keys()),
            },
        )
        return updated

    async def get_dct_dashboard_metrics(self, study_id: str) -> DCTDashboardMetrics:
        """Retrieve overview metrics for DCT console dashboard."""
        return await self.repository.get_dct_metrics(study_id)
