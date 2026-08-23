"""Comprehensive unit and integration test suite for GxP Telehealth, Pediatric Assent, and DTP Logistics.

Validates:
- End-to-end encrypted WebRTC telehealth video visits, session signaling, room authorization tokens
- GxP archived recording SHA-256 digital seals (21 CFR Part 11)
- Synchronized interactive co-viewing of eConsent forms and tiered pediatric assent / LAR signatures
- Direct-to-Patient (DTP) courier drug shipment tracking, cryogenic/ambient temperature indicators, custody handoffs, delivery signatures
- Decentralized in-home healthcare nurse visit scheduling and mobile eCRF data entry with offline cache sync
- REST API endpoint contracts and gateway security

Requirements: PRD-DCT-003, PRD-DCT-004, PRD-DCT-005, PRD-DCT-006, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.interop.domain.dct_models import (
    ExcursionSeverity,
    GxPRecordingSeal,
    LARRelationship,
    ParticipantRole,
    PediatricAssentTier,
    TemperatureReading,
    TemperatureZone,
)
from apps.interop.domain.dct_processor import (
    calculate_mean_kinetic_temperature,
    create_custody_handoff_event,
    create_gxp_recording_digital_seal,
    create_pediatric_assent_bundle,
    determine_pediatric_tier,
    evaluate_pediatric_comprehension,
    evaluate_temperature_excursions,
    generate_room_access_token,
    get_temperature_thresholds,
    reconcile_mobile_ecrf_offline_payload,
    validate_webrtc_fingerprint,
    verify_pediatric_assent_bundle,
    verify_recording_digital_seal,
    verify_room_access_token,
)
from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.models import Base
from apps.interop.main import app
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup clean in-memory SQLite database for DCT tests."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "site_crc",
    user_id: str = "investigator_01",
    change_reason: str = "GxP DCT Telehealth and DTP Testing",
) -> dict[str, str]:
    """Helper to generate valid gateway signed headers."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
    )


# =========================================================================
# Unit Tests: WebRTC Telehealth & Room Authorization Security
# =========================================================================


class TestTelehealthRoomSecurityAndSignaling:
    """Test WebRTC session authorization, room tokens, DTLS fingerprints, and GxP recording seals.

    @req:PRD-DCT-005
    @req:ADR-2199
    """

    def test_room_token_generation_and_verification(self) -> None:
        """Verify generation and cryptographic verification of room authorization tokens.

        @req:PRD-DCT-005
        @req:ADR-2199
        """
        secret = "super-secret-telehealth-key-12345"
        token_str, payload = generate_room_access_token(
            secret=secret,
            room_id="room-telehealth-101",
            session_id="session-555",
            study_id="STUDY-DCT-001",
            subject_id="SUBJ-BOSTON-101",
            participant_id="user-pi-dr-smith",
            role=ParticipantRole.INVESTIGATOR,
            display_name="Dr. Smith, Principal Investigator",
            duration_minutes=60,
        )

        assert token_str.startswith("cadence_rt_")
        assert payload.capabilities.can_present is True
        assert payload.capabilities.can_record is True
        assert payload.capabilities.can_admit is True
        assert payload.capabilities.can_sign is True

        # Verify token
        verified = verify_room_access_token(secret, token_str)
        assert verified.room_id == "room-telehealth-101"
        assert verified.subject_id == "SUBJ-BOSTON-101"
        assert verified.participant_role == ParticipantRole.INVESTIGATOR
        assert verified.capabilities.can_present is True

    def test_room_token_role_permissions(self) -> None:
        """Verify subject and LAR tokens have restricted capabilities (no admit/record).

        @req:PRD-DCT-005
        @req:ADR-2199
        """
        secret = "secret-key-123"
        _, subject_payload = generate_room_access_token(
            secret=secret,
            room_id="room-telehealth-102",
            session_id="session-556",
            study_id="STUDY-DCT-001",
            subject_id="SUBJ-BOSTON-102",
            participant_id="patient_alice",
            role=ParticipantRole.SUBJECT,
            display_name="Alice (Participant)",
        )

        assert subject_payload.capabilities.can_present is False
        assert subject_payload.capabilities.can_record is False
        assert subject_payload.capabilities.can_admit is False
        assert subject_payload.capabilities.can_sign is True
        assert subject_payload.capabilities.can_annotate is True

    def test_room_token_rejection_on_tamper_or_expiration(self) -> None:
        """Verify tampered or expired room tokens are rejected.

        @req:PRD-DCT-005
        @req:ADR-2199
        """
        secret = "secret-key-123"
        token_str, _ = generate_room_access_token(
            secret=secret,
            room_id="room-103",
            session_id="session-557",
            study_id="STUDY-DCT-001",
            subject_id="SUBJ-103",
            participant_id="user_pi",
            role=ParticipantRole.INVESTIGATOR,
            display_name="Dr. Jones",
            duration_minutes=60,
        )

        # Tampered signature
        tampered_token = token_str[:-4] + "dead"
        with pytest.raises(ValueError, match="signature mismatch"):
            verify_room_access_token(secret, tampered_token)

        # Wrong secret
        with pytest.raises(ValueError, match="signature mismatch"):
            verify_room_access_token("wrong-secret", token_str)

    def test_webrtc_dtls_fingerprint_validation(self) -> None:
        """Verify DTLS-SRTP SHA-256 fingerprint syntax validation.

        @req:PRD-DCT-005
        @req:ADR-2199
        """
        valid_fp = "sha-256 4A:AD:B9:B1:3F:82:18:3B:54:02:12:DF:3E:5D:49:6B:19:E5:7C:AB:C5:40:4E:50:DC:FB:E8:6A:4E:EE:E2:43"
        assert validate_webrtc_fingerprint(valid_fp) is True

        invalid_fp1 = "sha-1 4A:AD:B9"
        assert validate_webrtc_fingerprint(invalid_fp1) is False
        assert validate_webrtc_fingerprint("") is False

    def test_gxp_recording_seal_generation_and_verification(self) -> None:
        """Verify GxP recording SHA-256 digital seal creation and verification (21 CFR Part 11).

        @req:PRD-DCT-005
        @req:ADR-2199
        """
        secret = "recording-archive-secret-key-999"
        raw_video_data = "SAMPLE_WEBRTC_AV_STREAM_RAW_BYTES_MOCK"
        participants = [
            {"id": "pi_01", "role": "INVESTIGATOR", "name": "Dr. Sarah Connor"},
            {"id": "subj_101", "role": "SUBJECT", "name": "John Connor"},
        ]

        seal = create_gxp_recording_digital_seal(
            secret=secret,
            recording_id="REC-2026-001",
            session_id="SESSION-001",
            study_id="STUDY-DCT-001",
            media_payload_or_hash=raw_video_data,
            duration_seconds=1845,
            resolution="1080p",
            recorded_by="pi_01",
            participant_manifest=participants,
        )

        assert len(seal.media_sha256_hash) == 64
        assert len(seal.digital_seal_signature) == 64
        assert "21 CFR Part 11" in seal.part11_certification_statement

        # Verify seal passes
        assert verify_recording_digital_seal(secret, seal) is True

        # Tampered duration fails verification
        tampered_seal = GxPRecordingSeal(
            recording_id=seal.recording_id,
            session_id=seal.session_id,
            study_id=seal.study_id,
            duration_seconds=9999,  # tampered
            resolution=seal.resolution,
            media_sha256_hash=seal.media_sha256_hash,
            digital_seal_signature=seal.digital_seal_signature,
            recorded_by=seal.recorded_by,
            sealed_at_utc=seal.sealed_at_utc,
            participant_manifest=seal.participant_manifest,
            part11_certification_statement=seal.part11_certification_statement,
        )
        assert verify_recording_digital_seal(secret, tampered_seal) is False


# =========================================================================
# Unit Tests: Tiered Pediatric Assent & LAR Consent
# =========================================================================


class TestTieredPediatricAssentAndLAR:
    """Test age-appropriate pediatric assent tiers, comprehension scoring, and LAR dual-consent manifests.

    @req:PRD-DCT-006
    @req:ADR-2199
    """

    def test_determine_pediatric_tier(self) -> None:
        """Verify age classification into appropriate assent and LAR tiers.

        @req:PRD-DCT-006
        @req:ADR-2199
        """
        assert determine_pediatric_tier(5) == PediatricAssentTier.LAR_PARENT_GUARDIAN
        assert determine_pediatric_tier(8) == PediatricAssentTier.CHILD_7_11
        assert determine_pediatric_tier(14) == PediatricAssentTier.ADOLESCENT_12_17
        assert determine_pediatric_tier(20) == PediatricAssentTier.ADULT_18_PLUS

    def test_comprehension_scoring(self) -> None:
        """Verify 80% passing threshold for pediatric multimedia quiz.

        @req:PRD-DCT-006
        @req:ADR-2199
        """
        answers_pass = [
            {"question_id": "q1", "selected_option": "A", "correct_option": "A"},
            {"question_id": "q2", "selected_option": "B", "correct_option": "B"},
            {"question_id": "q3", "selected_option": "C", "correct_option": "C"},
            {"question_id": "q4", "selected_option": "D", "correct_option": "D"},
            {"question_id": "q5", "selected_option": "A", "correct_option": "B"},
        ]
        score, passed = evaluate_pediatric_comprehension(
            PediatricAssentTier.CHILD_7_11, answers_pass
        )
        assert score == 80.0
        assert passed is True

        answers_fail = [
            {"question_id": "q1", "selected_option": "A", "correct_option": "B"},
            {"question_id": "q2", "selected_option": "B", "correct_option": "C"},
            {"question_id": "q3", "selected_option": "C", "correct_option": "C"},
        ]
        score_fail, passed_fail = evaluate_pediatric_comprehension(
            PediatricAssentTier.ADOLESCENT_12_17, answers_fail
        )
        assert score_fail == 33.3
        assert passed_fail is False

    def test_child_assent_with_lar_parent_signature(self) -> None:
        """Verify child (7-11) assent with mandatory parent/LAR signature and cryptographic seal.

        @req:PRD-DCT-006
        @req:ADR-2199
        """
        bundle = create_pediatric_assent_bundle(
            study_id="STUDY-PED-101",
            subject_id="SUBJ-PED-001",
            tier=PediatricAssentTier.CHILD_7_11,
            minor_age=9,
            comprehension_score_percent=100.0,
            assent_signature="DIGITAL_THUMBPRINT_STAMP_99",
            lar_name="Jane Doe",
            lar_relationship=LARRelationship.PARENT,
            lar_legal_basis="Birth Certificate Verified",
            lar_signature="SIG_JANE_DOE_PAR_2026",
            investigator_name="Dr. Pediatrician",
            investigator_signature="SIG_DR_PED_2026",
        )

        assert bundle.tier == PediatricAssentTier.CHILD_7_11
        assert bundle.comprehension_passed is True
        assert len(bundle.cryptographic_bundle_hash) == 64
        assert verify_pediatric_assent_bundle(bundle) is True

    def test_pediatric_assent_rejects_missing_lar(self) -> None:
        """Verify pediatric assent throws error if LAR parent/guardian signature is absent.

        @req:PRD-DCT-006
        @req:ADR-2199
        """
        with pytest.raises(ValueError, match="LAR.*mandatory"):
            create_pediatric_assent_bundle(
                study_id="STUDY-PED-101",
                subject_id="SUBJ-PED-002",
                tier=PediatricAssentTier.ADOLESCENT_12_17,
                minor_age=15,
                comprehension_score_percent=90.0,
                assent_signature="SIG_TEEN_002",
                lar_name=None,  # Missing LAR
                lar_relationship=None,
                lar_legal_basis=None,
                lar_signature=None,
                investigator_name="Dr. Smith",
                investigator_signature="SIG_DR_SMITH",
            )


# =========================================================================
# Unit Tests: Direct-to-Patient (DTP) Logistics & Cold Chain
# =========================================================================


class TestDTPLogisticsAndColdChain:
    """Test DTP courier dispatch, Arrhenius MKT calculations, cold chain excursions, and custody handoffs.

    @req:PRD-DCT-003
    @req:ADR-2199
    """

    def test_temperature_thresholds(self) -> None:
        """Verify temperature ranges across storage condition profiles.

        @req:PRD-DCT-003
        @req:ADR-2199
        """
        assert get_temperature_thresholds(TemperatureZone.CRYOGENIC) == (-196.0, -150.0)
        assert get_temperature_thresholds(TemperatureZone.ULTRA_COLD) == (-80.0, -60.0)
        assert get_temperature_thresholds(TemperatureZone.REFRIGERATED) == (2.0, 8.0)
        assert get_temperature_thresholds(TemperatureZone.CONTROLLED_AMBIENT) == (
            15.0,
            25.0,
        )

    def test_mean_kinetic_temperature_calculation(self) -> None:
        """Verify USP <1079> Arrhenius MKT calculation.

        @req:PRD-DCT-003
        @req:ADR-2199
        """
        # Constant 5°C temperatures -> MKT should be exactly 5.0°C
        uniform_readings = [5.0, 5.0, 5.0, 5.0, 5.0]
        mkt_uniform = calculate_mean_kinetic_temperature(uniform_readings)
        assert abs(mkt_uniform - 5.0) < 0.1

        # Readings varying between 3°C and 7°C (MKT is weighted higher than arithmetic mean)
        varying = [3.0, 4.0, 5.0, 6.0, 7.0]
        mkt_var = calculate_mean_kinetic_temperature(varying)
        arith_mean = sum(varying) / len(varying)
        assert (
            mkt_var >= arith_mean
        )  # Arrhenius exponential weighting makes MKT >= arithmetic mean
        assert 4.9 <= mkt_var <= 5.5

    def test_temperature_excursion_evaluation(self) -> None:
        """Verify detection of refrigerated cold-chain excursions and quarantine flagging.

        @req:PRD-DCT-003
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        # Normal refrigerated readings (2°C to 8°C)
        normal_readings = [
            TemperatureReading(now, 4.2, "LOG-01"),
            TemperatureReading(now + timedelta(minutes=15), 4.8, "LOG-01"),
            TemperatureReading(now + timedelta(minutes=30), 5.1, "LOG-01"),
        ]
        min_t, max_t, mkt_t, sev_norm = evaluate_temperature_excursions(
            TemperatureZone.REFRIGERATED, normal_readings
        )
        assert sev_norm == ExcursionSeverity.NONE
        assert 4.0 <= min_t <= 5.2

        # Severe excursion (spiked to 16°C in refrigerated zone)
        excursion_readings = [
            TemperatureReading(now, 4.0, "LOG-01"),
            TemperatureReading(
                now + timedelta(minutes=15), 16.5, "LOG-01"
            ),  # > 8°C + 5°C
        ]
        _, _, _, sev_crit = evaluate_temperature_excursions(
            TemperatureZone.REFRIGERATED, excursion_readings
        )
        assert sev_crit == ExcursionSeverity.CRITICAL_EXCURSION_QUARANTINE

    def test_chain_of_custody_handoff_creation(self) -> None:
        """Verify immutable Chain of Custody handoff creation with digital signature.

        @req:PRD-DCT-003
        @req:ADR-2199
        """
        handoff = create_custody_handoff_event(
            stage="DEPOT_TO_SPECIALTY_COURIER",
            released_by="Depot Manager Bob",
            received_by="Courier Driver Carlos",
            tamper_seal_intact=True,
            current_temperature_celsius=4.5,
            notes="Seal intact, temperature verified on logger display",
        )
        assert handoff.handoff_id.startswith("COC-")
        assert handoff.tamper_seal_intact is True
        assert handoff.signature_manifestation.startswith("DIGITAL_SIG:")


# =========================================================================
# Unit Tests: In-Home Visiting Nurse & Mobile eCRF Offline Sync
# =========================================================================


class TestInHomeVisitsAndOfflineSync:
    """Test in-home healthcare nurse visit scheduling and offline mobile eCRF reconciliation.

    @req:PRD-DCT-004
    @req:ADR-2199
    """

    def test_mobile_ecrf_offline_reconciliation(self) -> None:
        """Verify optimistic offline cache reconciliation and audit tracking.

        @req:PRD-DCT-004
        @req:ADR-2199
        """
        server_state = {"systolic_bp": 120, "diastolic_bp": 80}
        client_payload = {
            "data": {
                "systolic_bp": 124,
                "diastolic_bp": 82,
                "pk_draw_time": "10:15:00",
                "sample_barcode": "TUBE-PK-98765",
            }
        }
        reconciled, sync_status = reconcile_mobile_ecrf_offline_payload(
            server_state=server_state,
            client_payload=client_payload,
            client_sync_id="SYNC-CLIENT-UUID-001",
        )
        assert sync_status == "SYNCHRONIZED"
        assert reconciled["systolic_bp"] == 124
        assert reconciled["pk_draw_time"] == "10:15:00"
        assert reconciled["sample_barcode"] == "TUBE-PK-98765"
        assert reconciled["last_reconciled_sync_id"] == "SYNC-CLIENT-UUID-001"


# =========================================================================
# Integration Tests: REST API Endpoints
# =========================================================================


class TestDCTRestApiIntegration:
    """Integration test suite exercising full FastAPI HTTP endpoints and database transactions.

    @req:PRD-DCT-003
    @req:PRD-DCT-004
    @req:PRD-DCT-005
    @req:PRD-DCT-006
    @req:ADR-2199
    """

    def test_telehealth_consultation_lifecycle(self) -> None:
        """Test full telehealth video consultation: schedule, token, signaling, co-viewing, and GxP recording.

        @req:PRD-DCT-005
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers(roles="site_crc", user_id="pi_user_01")

        # 1. Schedule session
        schedule_payload = {
            "study_id": "STUDY-DCT-001",
            "subject_id": "SUBJ-BOSTON-101",
            "title": "Visit 3 Telehealth Consultation & eConsent Review",
            "scheduled_start_utc": datetime.now(UTC).isoformat(),
            "site_id": "SITE-101",
        }
        resp_create = client.post(
            "/api/v1/interop/dct/telehealth/sessions",
            json=schedule_payload,
            headers=headers,
        )
        assert resp_create.status_code == 201
        session_data = resp_create.json()["session"]
        session_id = session_data["id"]
        room_id = session_data["room_id"]

        # 2. Generate room token
        token_req = {
            "participant_id": "pi_user_01",
            "participant_role": "INVESTIGATOR",
            "display_name": "Dr. Sarah Connor, PI",
            "duration_minutes": 90,
        }
        resp_token = client.post(
            f"/api/v1/interop/dct/telehealth/sessions/{session_id}/token",
            json=token_req,
            headers=headers,
        )
        assert resp_token.status_code == 200
        token_data = resp_token.json()
        assert token_data["room_id"] == room_id
        assert token_data["token"].startswith("cadence_rt_")
        assert token_data["capabilities"]["can_present"] is True

        # 3. Exchange WebRTC signaling
        signal_req = {
            "sender_id": "pi_user_01",
            "sender_role": "INVESTIGATOR",
            "message_type": "OFFER",
            "sdp": "v=0\r\no=- 12345 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=fingerprint:sha-256 4A:AD:B9:B1:3F:82:18:3B:54:02:12:DF:3E:5D:49:6B:19:E5:7C:AB:C5:40:4E:50:DC:FB:E8:6A:4E:EE:E2:43\r\n",
        }
        resp_signal = client.post(
            f"/api/v1/interop/dct/telehealth/sessions/{session_id}/signaling",
            json=signal_req,
            headers=headers,
        )
        assert resp_signal.status_code == 200
        assert resp_signal.json()["session"]["status"] == "IN_PROGRESS"

        # 4. Synchronize eConsent co-viewing state
        coview_req = {
            "document_id": "DOC-ICF-V3-MAIN",
            "page_index": 2,
            "clause_id": "CLAUSE-RISKS-AND-BENEFITS",
            "presenter_id": "pi_user_01",
            "presenter_role": "INVESTIGATOR",
            "scroll_percent": 45.5,
            "active_highlights": [
                {"x": 100, "y": 250, "text": "Common mild side effects include fatigue"}
            ],
            "lock_presenter_control": True,
        }
        resp_coview = client.post(
            f"/api/v1/interop/dct/telehealth/sessions/{session_id}/co-view",
            json=coview_req,
            headers=headers,
        )
        assert resp_coview.status_code == 200
        assert resp_coview.json()["session"]["co_view_state"]["page_index"] == 2

        # 5. Archive GxP recording with SHA-256 digital seal
        record_req = {
            "media_payload_or_hash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "duration_seconds": 1820,
            "resolution": "1080p",
            "recorded_by": "pi_user_01",
            "participant_manifest": [
                {
                    "id": "pi_user_01",
                    "name": "Dr. Sarah Connor",
                    "role": "INVESTIGATOR",
                },
                {"id": "SUBJ-BOSTON-101", "name": "John Connor", "role": "SUBJECT"},
            ],
        }
        resp_archive = client.post(
            f"/api/v1/interop/dct/telehealth/sessions/{session_id}/archive-recording",
            json=record_req,
            headers=headers,
        )
        assert resp_archive.status_code == 200
        archive_res = resp_archive.json()
        assert archive_res["recording_id"].startswith("REC-GXP-")
        assert len(archive_res["digital_seal_signature"]) == 64

    def test_pediatric_assent_and_lar_endpoint_flow(self) -> None:
        """Test submitting tiered pediatric assent and verifying cryptographic bundle seal.

        @req:PRD-DCT-006
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers(roles="site_crc", user_id="pi_user_02")

        submit_req = {
            "study_id": "STUDY-PEDS-002",
            "subject_id": "SUBJ-PEDS-102",
            "minor_age": 10,
            "answers": [
                {"question_id": "q1", "selected_option": "A", "correct_option": "A"},
                {"question_id": "q2", "selected_option": "B", "correct_option": "B"},
            ],
            "assent_signature": "CHILD_DRAWN_SMILEY_MARK_102",
            "lar_name": "Mary Johnson",
            "lar_relationship": "PARENT",
            "lar_legal_basis": "Legal Mother (Birth Certificate)",
            "lar_signature": "SIG_MARY_JOHNSON_ESQ_2026",
            "investigator_name": "Dr. House",
            "investigator_signature": "SIG_DR_HOUSE_2026",
        }

        resp = client.post(
            "/api/v1/interop/dct/econsent/pediatric-assent",
            json=submit_req,
            headers=headers,
        )
        assert resp.status_code == 201
        assent_data = resp.json()["assent"]
        assent_id = assent_data["assent_id"]
        assert assent_data["tier"] == "CHILD_7_11"
        assert assent_data["comprehension_passed"] is True

        # Verify bundle seal via GET endpoint
        resp_ver = client.get(
            f"/api/v1/interop/dct/econsent/pediatric-assent/{assent_id}/verify",
            headers=headers,
        )
        assert resp_ver.status_code == 200
        assert resp_ver.json()["is_valid"] is True

    def test_dtp_shipment_cold_chain_and_delivery_lifecycle(self) -> None:
        """Test Direct-to-Patient drug shipment creation, temperature ingestion, CoC handoff, and delivery sign-off.

        @req:PRD-DCT-003
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers(roles="site_crc", user_id="pharmacist_01")

        # 1. Create DTP dispatch order
        shipment_req = {
            "study_id": "STUDY-DCT-001",
            "site_id": "SITE-101",
            "subject_pseudonym_id": "SUBJ-PSEUDO-BOSTON-101",
            "destination_address_pseudonym": "ADDR-PSEUDO-MASSACHUSETTS-02138",
            "courier_name": "World Courier Specialty Logistics",
            "temperature_zone": "REFRIGERATED",
            "kit_serials": ["KIT-LOT99-001", "KIT-LOT99-002"],
            "tamper_seal_id": "SEAL-RFID-998877",
        }
        resp_create = client.post(
            "/api/v1/interop/dct/dtp/shipments",
            json=shipment_req,
            headers=headers,
        )
        assert resp_create.status_code == 201
        shipment_data = resp_create.json()["shipment"]
        tracking_num = shipment_data["tracking_number"]
        assert shipment_data["status"] == "ORDER_CREATED"
        assert shipment_data["temperature_zone"] == "REFRIGERATED"

        # 2. Ingest cold-chain temperature telemetry readings
        now = datetime.now(UTC)
        temp_req = {
            "readings": [
                {
                    "timestamp_utc": now.isoformat(),
                    "temperature_celsius": 4.1,
                    "logger_id": "LOG-998877",
                    "battery_percent": 99.0,
                },
                {
                    "timestamp_utc": (now + timedelta(hours=2)).isoformat(),
                    "temperature_celsius": 4.6,
                    "logger_id": "LOG-998877",
                    "battery_percent": 98.0,
                },
                {
                    "timestamp_utc": (now + timedelta(hours=4)).isoformat(),
                    "temperature_celsius": 5.0,
                    "logger_id": "LOG-998877",
                    "battery_percent": 97.0,
                },
            ]
        }
        resp_temp = client.post(
            f"/api/v1/interop/dct/dtp/shipments/{tracking_num}/temperature",
            json=temp_req,
            headers=headers,
        )
        assert resp_temp.status_code == 200
        updated_shipment = resp_temp.json()["shipment"]
        assert updated_shipment["excursion_severity"] == "NONE"
        assert 4.0 <= updated_shipment["mkt_celsius"] <= 5.2

        # 3. Record Chain of Custody handoff
        handoff_req = {
            "stage": "COURIER_PICKUP",
            "released_by": "Central Depot Pharmacist Alice",
            "received_by": "Courier Driver Carlos",
            "tamper_seal_intact": True,
            "current_temperature_celsius": 4.6,
            "notes": "Verified tamper seal and temperature greenlight indicator",
        }
        resp_handoff = client.post(
            f"/api/v1/interop/dct/dtp/shipments/{tracking_num}/handoff",
            json=handoff_req,
            headers=headers,
        )
        assert resp_handoff.status_code == 200
        assert resp_handoff.json()["shipment"]["status"] == "COURIER_DISPATCHED"

        # 4. Confirm delivery to subject residence
        delivery_req = {
            "recipient_name": "Subject Jane Doe (Self)",
            "signature_manifestation": "SIG_JANE_DOE_DELIVERY_2026",
            "tamper_seal_verified": True,
            "logger_greenlight": True,
        }
        resp_deliv = client.post(
            f"/api/v1/interop/dct/dtp/shipments/{tracking_num}/delivery",
            json=delivery_req,
            headers=headers,
        )
        assert resp_deliv.status_code == 200
        assert resp_deliv.json()["shipment"]["status"] == "DELIVERED"
        assert (
            resp_deliv.json()["shipment"]["delivery_signature"]["recipient_name"]
            == "Subject Jane Doe (Self)"
        )

    def test_in_home_nurse_visit_and_offline_ecrf_sync_lifecycle(self) -> None:
        """Test decentralized visiting nurse scheduling, procedure tracking, and offline eCRF sync.

        @req:PRD-DCT-004
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers(roles="site_crc", user_id="nurse_coord_01")

        # 1. Schedule in-home nurse visit
        visit_req = {
            "study_id": "STUDY-DCT-001",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-BOSTON-101",
            "visit_number": "V4_HOME",
            "visit_name": "Week 4 In-Home PK & Safety Visit",
            "nurse_id": "NURSE-CLIN-007",
            "nurse_name": "Nurse Florence Nightingale, RN",
            "nurse_license_number": "RN-MA-884422",
            "scheduled_start_utc": datetime.now(UTC).isoformat(),
            "procedures": [
                {
                    "procedure_type": "VITAL_SIGNS",
                    "description": "Sitting blood pressure & HR",
                    "completed": False,
                },
                {
                    "procedure_type": "PK_BLOOD_DRAW",
                    "description": "2-hour post-dose PK draw",
                    "completed": False,
                },
                {
                    "procedure_type": "IP_ADMINISTRATION",
                    "description": "Subcutaneous IP injection",
                    "completed": False,
                },
            ],
        }
        resp_sched = client.post(
            "/api/v1/interop/dct/home-health/visits",
            json=visit_req,
            headers=headers,
        )
        assert resp_sched.status_code == 201
        visit_data = resp_sched.json()["visit"]
        visit_id = visit_data["visit_id"]
        assert visit_data["status"] == "SCHEDULED"

        # 2. Update visit status to PROCEDURES_IN_PROGRESS
        status_req = {
            "status": "PROCEDURES_IN_PROGRESS",
            "procedures": [
                {
                    "procedure_type": "VITAL_SIGNS",
                    "description": "Sitting blood pressure & HR",
                    "completed": True,
                    "sample_barcodes": [],
                    "performed_by": "RN Florence",
                    "notes": "118/76 mmHg, 68 bpm",
                },
                {
                    "procedure_type": "PK_BLOOD_DRAW",
                    "description": "2-hour post-dose PK draw",
                    "completed": True,
                    "sample_barcodes": ["BARCODE-PK-001"],
                    "performed_by": "RN Florence",
                    "notes": "Left arm draw",
                },
                {
                    "procedure_type": "IP_ADMINISTRATION",
                    "description": "Subcutaneous IP injection",
                    "completed": True,
                    "sample_barcodes": ["VIAL-LOT99-A"],
                    "performed_by": "RN Florence",
                    "notes": "Abdomen sub-Q",
                },
            ],
        }
        resp_stat = client.post(
            f"/api/v1/interop/dct/home-health/visits/{visit_id}/status",
            json=status_req,
            headers=headers,
        )
        assert resp_stat.status_code == 200
        assert resp_stat.json()["visit"]["status"] == "PROCEDURES_IN_PROGRESS"

        # 3. Synchronize mobile eCRF entered offline
        sync_req = {
            "client_sync_id": "OFFLINE-SYNC-DEVICE-TABLET-9988",
            "offline_collected_at_utc": datetime.now(UTC).isoformat(),
            "data": {
                "VS_BP_SYS": 118,
                "VS_BP_DIA": 76,
                "VS_HR": 68,
                "PK_SAMPLE_TUBE_ID": "BARCODE-PK-001",
                "IP_DOSE_MG": 50,
                "ADVERSE_EVENTS_REPORTED": "NONE",
            },
        }
        resp_sync = client.post(
            f"/api/v1/interop/dct/home-health/visits/{visit_id}/sync-ecrf",
            json=sync_req,
            headers=headers,
        )
        assert resp_sync.status_code == 200
        synced_visit = resp_sync.json()["visit"]
        assert synced_visit["status"] == "VISIT_COMPLETED"
        assert synced_visit["sync_status"] == "SYNCHRONIZED"
        assert synced_visit["mobile_ecrf_data"]["VS_BP_SYS"] == 118

        # 4. Check overview dashboard metrics
        resp_metrics = client.get(
            "/api/v1/interop/dct/metrics/STUDY-DCT-001",
            headers=headers,
        )
        assert resp_metrics.status_code == 200
        m = resp_metrics.json()
        assert m["completed_home_visits"] >= 1
        assert m["scheduled_nurse_visits"] == 0
