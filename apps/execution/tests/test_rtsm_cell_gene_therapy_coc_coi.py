"""Unit and Integration Tests for RTSM Cell & Gene Therapy Vein-to-Vein Chain of Custody / Identity (COC/COI).

Requirements: PRD-SCM-005, PRD-SCM-006, ADR-2199, ISBT 128, 21 CFR Part 11
"""

import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    CGTChainOfCustody,
    CGTMilestoneEvent,
    IntegrationOutbox,
    IPKit,
)
from apps.execution.main import app
from apps.execution.rtsm_cgt import (
    generate_crypto_barcode,
    generate_isbt128_din,
    validate_isbt128_din,
    verify_crypto_barcode,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security.signing import generate_gateway_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Initializes clean test database."""
    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()
    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_gateway_headers(
    user_id: str = "test_investigator",
    roles: str = "investigator",
    change_reason: str = "CGT COC/COI test execution",
    site_id: str | None = "SITE-101",
    include_sig_token: bool = True,
) -> dict:
    """Generate signed gateway V2 headers for authenticated test requests."""
    from jose import jwt

    timestamp = str(time.time())
    sig_token = None
    if include_sig_token:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        sig_token = jwt.encode(
            sig_payload, b"internal-gateway-secret-12345", algorithm="HS256"
        )

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=b"internal-gateway-secret-12345",  # pragma: allowlist secret
        change_reason=change_reason,
        site_id=site_id,
        sig_token=sig_token,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    if sig_token:
        headers["X-Sig-Token"] = sig_token
    return headers


# =========================================================================
# 1. Pure Unit Tests: ISBT 128 DIN & Cryptographic Barcode Engine
# =========================================================================


class TestISBT128AndCryptoBarcodes:
    """Unit tests for ISBT 128 Donation Identification Numbers and cryptographic barcodes.

    @req:PRD-SYS-184
    @req:PRD-SCM-005
    @req:ADR-2199
    """

    def test_generate_isbt128_din_structure_and_checksum(self):
        """Validates format and ISO 7064 Mod 37-2 checksum of generated DINs.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        din = generate_isbt128_din(facility_code="W1234", year=26, sequence_num=100001)
        assert len(din) == 15
        assert din.startswith("W123426100001")
        assert validate_isbt128_din(din) is True

    def test_validate_isbt128_din_detects_tampering(self):
        """Validates that modified or invalid DIN check characters fail validation.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        valid_din = generate_isbt128_din(
            facility_code="A9999", year=26, sequence_num=456789
        )
        assert validate_isbt128_din(valid_din) is True

        # Tampered digit in sequence
        tampered_din = (
            valid_din[:10] + ("9" if valid_din[10] != "9" else "0") + valid_din[11:]
        )
        assert validate_isbt128_din(tampered_din) is False

        # Invalid checksum chars
        bad_chk_din = valid_din[:13] + "ZZ"
        assert validate_isbt128_din(bad_chk_din) is False

        # Empty or short string
        assert validate_isbt128_din("") is False
        assert validate_isbt128_din("W1234") is False

    def test_generate_and_verify_crypto_barcode(self):
        """Verifies generation of HMAC-SHA256 tamper-evident 2D barcode payload.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-801"
        site_id = "SITE-101"
        subject_id = "SUBJ-UUID-8899"
        din = generate_isbt128_din("W1234", 26, 123456)
        batch_id = "CART-LOT-2026-X1"

        barcode = generate_crypto_barcode(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            din=din,
            batch_id=batch_id,
        )

        assert barcode.barcode_id.startswith("BAR-")
        assert barcode.subject_id == subject_id
        assert barcode.din == din
        assert barcode.batch_id == batch_id
        assert len(barcode.crypto_hash) == 64  # SHA-256 hex string

        # Verify barcode payload
        is_valid, fail_reason, data = verify_crypto_barcode(
            barcode_payload=barcode.barcode_payload,
            expected_subject_id=subject_id,
            expected_din=din,
            expected_batch_id=batch_id,
        )
        assert is_valid is True
        assert fail_reason is None
        assert data["subject_id"] == subject_id
        assert data["din"] == din
        assert data["batch_id"] == batch_id

    def test_verify_crypto_barcode_detects_tampered_payload(self):
        """Verifies that altering any field in the payload fails cryptographic verification.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        import base64
        import json

        study_id = "STUDY-CGT-801"
        site_id = "SITE-101"
        subject_id = "SUBJ-UUID-ORIGINAL"
        din = generate_isbt128_din("W1234", 26, 111222)

        barcode = generate_crypto_barcode(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            din=din,
        )

        # Decode payload, tamper with subject_id, re-encode with original hash
        raw_bytes = base64.urlsafe_b64decode(barcode.barcode_payload.encode("utf-8"))
        data = json.loads(raw_bytes.decode("utf-8"))
        data["subject_id"] = "SUBJ-UUID-ATTACKER"
        tampered_payload = base64.urlsafe_b64encode(
            json.dumps(data).encode("utf-8")
        ).decode("utf-8")

        is_valid, fail_reason, _ = verify_crypto_barcode(
            barcode_payload=tampered_payload
        )
        assert is_valid is False
        assert "tampered" in (fail_reason or "").lower()

    def test_verify_crypto_barcode_mismatched_expectations(self):
        """Verifies that checking a valid barcode against the wrong expected subject or DIN fails.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        barcode = generate_crypto_barcode(
            study_id="STUDY-1",
            site_id="SITE-1",
            subject_id="SUBJ-A",
            din=generate_isbt128_din("W1234", 26, 333444),
        )

        # Mismatched subject expectation
        is_valid, fail_reason, _ = verify_crypto_barcode(
            barcode_payload=barcode.barcode_payload,
            expected_subject_id="SUBJ-B",
        )
        assert is_valid is False
        assert "subject mismatch" in (fail_reason or "").lower()


# =========================================================================
# 2. Integration Tests: Vein-to-Vein Lifecycle & Cryogenic Transit
# =========================================================================


class TestVeinToVeinLifecycleAndMilestones:
    """Integration tests for apheresis harvest, cryogenic milestones, and batch binding.

    @req:PRD-SYS-184
    @req:PRD-SCM-005
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_apheresis_harvest_initialization_flow(self):
        """Tests full apheresis registration and initial barcode creation via REST API.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        subject_id = "SUBJ-001-ALPHA"
        headers = get_gateway_headers(
            user_id="nurse_clara",
            roles="crc",
            site_id=site_id,
            change_reason="Initial apheresis harvest",
        )

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "collector_id": "nurse_clara",
                    "collector_name": "Clara Oswald, RN",
                    "collector_role": "Apheresis Nurse",
                    "initial_temp_celsius": -196.0,
                    "dry_shipper_id": "DEWAR-LN2-8801",
                    "seal_id": "SEAL-9941",
                    "notes": "Uneventful apheresis harvest.",
                },
                headers=headers,
            )

        assert resp.status_code == 201
        data = resp.json()
        assert data["study_id"] == study_id
        assert data["subject_id"] == subject_id
        assert data["current_stage"] == "APHERESIS_COLLECTION"
        assert data["status"] == "COLLECTED"
        assert data["din"] is not None
        assert data["tamper_evident_barcode"] is not None
        assert data["lowest_temp_celsius"] == -196.0

        # Verify persisted database records and outbox
        async with db_manager.get_session_maker()() as session:
            stmt = select(CGTChainOfCustody).where(CGTChainOfCustody.id == data["id"])
            res = await session.execute(stmt)
            coc = res.scalar_one_or_none()
            assert coc is not None
            assert coc.subject_id == subject_id

            stmt_ms = select(CGTMilestoneEvent).where(
                CGTMilestoneEvent.chain_of_custody_id == data["id"]
            )
            res_ms = await session.execute(stmt_ms)
            milestones = res_ms.scalars().all()
            assert len(milestones) == 1
            assert milestones[0].stage == "APHERESIS_COLLECTION"
            assert milestones[0].temperature_celsius == -196.0
            assert milestones[0].temperature_status == "OPTIMAL"

            stmt_outbox = select(IntegrationOutbox).where(
                IntegrationOutbox.event_type == "rtsm.cgt.apheresis_collected.v1"
            )
            res_outbox = await session.execute(stmt_outbox)
            outbox_entries = res_outbox.scalars().all()
            assert len(outbox_entries) >= 1

    @pytest.mark.asyncio
    async def test_cryogenic_transit_milestones_and_gps_tracking(self):
        """Tests logging GPS milestones and cryogenic transit temperature points.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        subject_id = "SUBJ-001-BETA"
        headers = get_gateway_headers(roles="sponsor_admin", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            # 1. Initialize Apheresis
            resp_aph = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "collector_id": "collector_1",
                    "collector_name": "Collector One",
                    "initial_temp_celsius": -196.0,
                },
                headers=headers,
            )
            coc_id = resp_aph.json()["id"]

            # 2. Log Outbound Courier Milestone with GPS
            resp_m1 = await client.post(
                "/api/v1/execution/rtsm/cgt/milestone",
                json={
                    "coc_id": coc_id,
                    "stage": "COURIER_TRANSIT_OUTBOUND",
                    "location_name": "Chicago O'Hare Cryo Hub",
                    "facility_id": "HUB-ORD",
                    "latitude": 41.9742,
                    "longitude": -87.9073,
                    "temperature_celsius": -185.0,
                    "custodian_id": "courier_world_courier",
                    "custodian_name": "World Courier Agent 44",
                    "custodian_role": "Specialized Biologics Courier",
                    "carrier": "World Courier",
                    "tracking_number": "WC-9901-2026",
                    "seal_intact": True,
                },
                headers=headers,
            )
            assert resp_m1.status_code == 201
            m1_data = resp_m1.json()
            assert m1_data["temperature_status"] == "OPTIMAL"
            assert m1_data["latitude"] == 41.9742

            # 3. Log Temperature Excursion (> -135°C)
            resp_m2 = await client.post(
                "/api/v1/execution/rtsm/cgt/milestone",
                json={
                    "coc_id": coc_id,
                    "stage": "COURIER_TRANSIT_OUTBOUND",
                    "location_name": "In-Flight Transit Waypoint",
                    "latitude": 40.7128,
                    "longitude": -74.0060,
                    "temperature_celsius": -120.0,  # Excursion above -135°C
                    "custodian_id": "courier_agent",
                    "custodian_name": "Courier Agent",
                    "custodian_role": "Courier",
                    "seal_intact": True,
                    "notes": "Cryogenic dry shipper temperature elevation noted.",
                },
                headers=headers,
            )
            assert resp_m2.status_code == 201
            assert resp_m2.json()["temperature_status"] == "EXCURSION"

        # Verify COC record incremented excursion count
        async with db_manager.get_session_maker()() as session:
            coc = (
                await session.execute(
                    select(CGTChainOfCustody).where(CGTChainOfCustody.id == coc_id)
                )
            ).scalar_one()
            assert coc.cryo_temp_excursion_count == 1
            assert coc.current_stage == "COURIER_TRANSIT_OUTBOUND"

    @pytest.mark.asyncio
    async def test_manufacturing_batch_binding(self):
        """Tests binding cell manufacturing batch records and regenerating cryptographic barcode.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        subject_id = "SUBJ-001-GAMMA"
        headers = get_gateway_headers(roles="sponsor_admin", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp_aph = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "collector_id": "collector_1",
                    "collector_name": "Collector One",
                },
                headers=headers,
            )
            coc_id = resp_aph.json()["id"]
            orig_barcode = resp_aph.json()["tamper_evident_barcode"]

            # Bind manufacturing batch
            batch_id = "CART-AUTO-BATCH-7701"
            resp_bind = await client.post(
                "/api/v1/execution/rtsm/cgt/manufacturing-bind",
                json={
                    "coc_id": coc_id,
                    "batch_id": batch_id,
                    "manufacturing_facility_id": "CMO-VIRAL-VECTOR-NJ",
                    "operator_id": "cmo_operator_99",
                    "operator_name": "Dr. Jean Dupont",
                    "temperature_celsius": -196.0,
                    "notes": "Transduction efficiency 88%, viability 96%. Batch released.",
                },
                headers=headers,
            )
            assert resp_bind.status_code == 200
            bind_data = resp_bind.json()
            assert bind_data["batch_id"] == batch_id
            assert bind_data["status"] == "MANUFACTURED"
            assert bind_data["current_stage"] == "MANUFACTURING_PROCESSING"
            assert bind_data["tamper_evident_barcode"] != orig_barcode

            # Verify the updated barcode cryptographically binds the batch_id
            is_valid, fail_reason, data = verify_crypto_barcode(
                barcode_payload=bind_data["tamper_evident_barcode"],
                expected_subject_id=subject_id,
                expected_batch_id=batch_id,
            )
            assert is_valid is True
            assert data["batch_id"] == batch_id


# =========================================================================
# 3. Bedside Two-Person Verification (Dual e-Signature Step-Up) & Infusion
# =========================================================================


class TestBedsideDualVerificationAndInfusionGating:
    """Tests mandatory two-person bedside verification before unlocking the drug infusion eCRF.

    @req:PRD-SYS-184
    @req:PRD-SCM-005
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_successful_bedside_dual_verification_and_infusion_unlock(self):
        """Tests that valid Nurse + PI dual e-signatures unlock drug infusion.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        subject_id = "SUBJ-BEDSIDE-001"
        headers = get_gateway_headers(roles="investigator", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            # 1. Apheresis
            resp_aph = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "collector_id": "nurse_1",
                    "collector_name": "Nurse One",
                },
                headers=headers,
            )
            coc_id = resp_aph.json()["id"]

            # 2. Manufacturing batch binding
            batch_id = "CART-LOT-4412"
            resp_bind = await client.post(
                "/api/v1/execution/rtsm/cgt/manufacturing-bind",
                json={
                    "coc_id": coc_id,
                    "batch_id": batch_id,
                    "manufacturing_facility_id": "CMO-01",
                    "operator_id": "cmo_op",
                    "operator_name": "CMO Operator",
                },
                headers=headers,
            )
            bag_barcode = resp_bind.json()["tamper_evident_barcode"]

            # 3. Return transit & site receipt
            await client.post(
                "/api/v1/execution/rtsm/cgt/milestone",
                json={
                    "coc_id": coc_id,
                    "stage": "SITE_RECEIPT_AND_STORAGE",
                    "location_name": f"Site {site_id} Pharmacy",
                    "temperature_celsius": -190.0,
                    "custodian_id": "pharm_1",
                    "custodian_name": "Pharmacist One",
                    "custodian_role": "Investigational Pharmacist",
                    "seal_intact": True,
                },
                headers=headers,
            )

            # 4. Bedside Two-Person Electronic Verification (Nurse + PI)
            resp_verify = await client.post(
                "/api/v1/execution/rtsm/cgt/bedside-verify",
                json={
                    "coc_id": coc_id,
                    "scanned_wristband_subject_id": subject_id,
                    "scanned_bag_barcode": bag_barcode,
                    "primary_verifier_id": "nurse_bedside",
                    "primary_verifier_name": "Sarah Smith, RN",
                    "primary_verifier_role": "Bedside Infusion Nurse",
                    "primary_signature_token": "valid-sig-token-nurse-111",
                    "secondary_verifier_id": "dr_roberts_pi",
                    "secondary_verifier_name": "Dr. Alice Roberts, MD",
                    "secondary_verifier_role": "Principal Investigator",
                    "secondary_signature_token": "valid-sig-token-pi-222",
                    "notes": "Both wristband and cell product bag cryptographically confirmed at bedside.",
                },
                headers=headers,
            )
            assert resp_verify.status_code == 200
            ver_data = resp_verify.json()
            assert ver_data["status"] == "VERIFIED"
            assert ver_data["wristband_matched"] is True
            assert ver_data["bag_matched"] is True
            assert ver_data["temperature_chain_verified"] is True
            unlock_token = ver_data["unlock_token"]
            assert len(unlock_token) == 64

            # 5. Execute Infusion Administration
            resp_infuse = await client.post(
                "/api/v1/execution/rtsm/cgt/infuse",
                json={
                    "coc_id": coc_id,
                    "unlock_token": unlock_token,
                    "administrator_id": "nurse_bedside",
                    "administrator_name": "Sarah Smith, RN",
                    "volume_infused_ml": 65.0,
                    "vital_signs_pre_temp_celsius": 36.6,
                    "vital_signs_post_temp_celsius": 37.0,
                },
                headers=headers,
            )
            assert resp_infuse.status_code == 200
            infuse_data = resp_infuse.json()
            assert infuse_data["status"] == "INFUSED"
            assert infuse_data["current_stage"] == "INFUSION_ADMINISTRATION"
            assert infuse_data["infused_at"] is not None

    @pytest.mark.asyncio
    async def test_bedside_dual_verification_rejects_identical_users(self):
        """Tests that two-person verification fails if primary and secondary verifiers are identical.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        subject_id = "SUBJ-BEDSIDE-002"
        headers = get_gateway_headers(roles="investigator", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp_aph = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "collector_id": "nurse_1",
                    "collector_name": "Nurse One",
                },
                headers=headers,
            )
            coc_id = resp_aph.json()["id"]
            bag_barcode = resp_aph.json()["tamper_evident_barcode"]

            # Attempt verification with identical user IDs
            resp_fail = await client.post(
                "/api/v1/execution/rtsm/cgt/bedside-verify",
                json={
                    "coc_id": coc_id,
                    "scanned_wristband_subject_id": subject_id,
                    "scanned_bag_barcode": bag_barcode,
                    "primary_verifier_id": "dr_roberts_pi",
                    "primary_verifier_name": "Dr. Alice Roberts, MD",
                    "primary_verifier_role": "Principal Investigator",
                    "primary_signature_token": "token-1",
                    "secondary_verifier_id": "dr_roberts_pi",  # Same user!
                    "secondary_verifier_name": "Dr. Alice Roberts, MD",
                    "secondary_verifier_role": "Principal Investigator",
                    "secondary_signature_token": "token-2",
                },
                headers=headers,
            )
            assert resp_fail.status_code == 400
            assert "cannot be identical" in resp_fail.json()["detail"].lower()

    @pytest.mark.asyncio
    async def test_bedside_dual_verification_rejects_wristband_mismatch(self):
        """Tests that scanning the wrong patient wristband immediately halts verification.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        subject_id = "SUBJ-BEDSIDE-CORRECT"
        headers = get_gateway_headers(roles="investigator", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp_aph = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "collector_id": "nurse_1",
                    "collector_name": "Nurse One",
                },
                headers=headers,
            )
            coc_id = resp_aph.json()["id"]
            bag_barcode = resp_aph.json()["tamper_evident_barcode"]

            # Attempt verification with mismatched wristband
            resp_fail = await client.post(
                "/api/v1/execution/rtsm/cgt/bedside-verify",
                json={
                    "coc_id": coc_id,
                    "scanned_wristband_subject_id": "SUBJ-BEDSIDE-WRONG-PATIENT",
                    "scanned_bag_barcode": bag_barcode,
                    "primary_verifier_id": "nurse_1",
                    "primary_verifier_name": "Nurse One",
                    "primary_verifier_role": "Nurse",
                    "primary_signature_token": "tok1",
                    "secondary_verifier_id": "dr_pi",
                    "secondary_verifier_name": "Dr. PI",
                    "secondary_verifier_role": "PI",
                    "secondary_signature_token": "tok2",
                },
                headers=headers,
            )
            assert resp_fail.status_code == 400
            assert "mismatch" in resp_fail.json()["detail"].lower()

    @pytest.mark.asyncio
    async def test_infusion_blocked_without_unlock_token(self):
        """Tests that infusion is strictly gated and blocked without valid bedside unlock token.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        subject_id = "SUBJ-LOCKED-001"
        headers = get_gateway_headers(roles="investigator", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp_aph = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "collector_id": "nurse_1",
                    "collector_name": "Nurse One",
                },
                headers=headers,
            )
            coc_id = resp_aph.json()["id"]

            # Attempt infusion without verification
            resp_fail = await client.post(
                "/api/v1/execution/rtsm/cgt/infuse",
                json={
                    "coc_id": coc_id,
                    "unlock_token": "invalid-token",
                    "administrator_id": "nurse_1",
                    "administrator_name": "Nurse One",
                },
                headers=headers,
            )
            assert resp_fail.status_code == 403
            assert "locked" in resp_fail.json()["detail"].lower()


# =========================================================================
# 4. Drug Returns, Reconciliation & Dual Witness Certificates of Destruction
# =========================================================================


class TestReturnsReconciliationAndCertificatesOfDestruction:
    """Tests vial returns, adherence tracking, and formal Certificates of Destruction.

    @req:PRD-SYS-184
    @req:PRD-SCM-006
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_vial_return_reconciliation_and_adherence(self):
        """Tests logging returned kit/vial accountability and calculating patient compliance.

        @req:PRD-SYS-184
        @req:PRD-SCM-006
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        subject_id = "SUBJ-RECON-001"
        headers = get_gateway_headers(roles="crc", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp = await client.post(
                "/api/v1/execution/rtsm/cgt/returns",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "kit_number": "KIT-CGT-9001",
                    "vial_identifier": "VIAL-A-01",
                    "lot_number": "LOT-2026-X8",
                    "expected_quantity": 2,
                    "returned_quantity": 2,
                    "intact_vials": 0,
                    "used_vials": 2,
                    "residual_volume_ml": 0.5,
                    "din": generate_isbt128_din("W1234", 26, 555666),
                    "logged_by": "crc_coordinator",
                    "disposition": "RECONCILED_FOR_DESTRUCTION",
                },
                headers=headers,
            )
            assert resp.status_code == 201
            data = resp.json()
            assert data["adherence_percentage"] == 100.0
            assert data["disposition"] == "RECONCILED_FOR_DESTRUCTION"

    @pytest.mark.asyncio
    async def test_certificate_of_destruction_generation_with_dual_witness_signatures(
        self,
    ):
        """Tests formal Certificate of Destruction generation with dual witness e-signatures.

        @req:PRD-SYS-184
        @req:PRD-SCM-006
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        lot_number = "LOT-DESTROY-2026"
        kit_numbers = ["KIT-DEST-01", "KIT-DEST-02"]
        vial_ids = ["VIAL-D1", "VIAL-D2"]
        headers = get_gateway_headers(roles="investigator", site_id=site_id)

        # Pre-seed IP kits in database
        async with db_manager.get_session_maker()() as session:
            for k_num in kit_numbers:
                kit = IPKit(
                    study_id=study_id,
                    kit_number=k_num,
                    kit_type="CELL_VIAL",
                    lot_number=lot_number,
                    status="AVAILABLE",
                )
                session.add(kit)
            await session.commit()

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp = await client.post(
                "/api/v1/execution/rtsm/cgt/destruction-certificate",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "lot_number": lot_number,
                    "kit_numbers": kit_numbers,
                    "vial_identifiers": vial_ids,
                    "destruction_reason": "Residual cellular product post-infusion reconciliation.",
                    "destruction_method": "HIGH_TEMPERATURE_INCINERATION",
                    "destruction_facility": "Authorized Biohazard Disposal Incinerator 4",
                    "witness_1_id": "pharmacist_jane",
                    "witness_1_name": "Jane Doe, PharmD",
                    "witness_1_role": "Site Pharmacist",
                    "witness_1_signature_token": "pharm-sig-token-111",
                    "witness_2_id": "dr_roberts_pi",
                    "witness_2_name": "Dr. Alice Roberts, MD",
                    "witness_2_role": "Principal Investigator",
                    "witness_2_signature_token": "pi-sig-token-222",
                    "quantity_destroyed": 2,
                    "residual_volume_ml": 1.2,
                },
                headers=headers,
            )

        assert resp.status_code == 201
        cert_data = resp.json()
        assert cert_data["certificate_number"].startswith("COD-2026-")
        assert cert_data["witness_1_id"] == "pharmacist_jane"
        assert cert_data["witness_2_id"] == "dr_roberts_pi"
        assert cert_data["status"] == "COMPLETED"
        assert len(cert_data["cryptographic_seal"]) == 64  # SHA-256 seal

        # Verify IP kits transitioned to DESTROYED
        async with db_manager.get_session_maker()() as session:
            stmt = select(IPKit).where(IPKit.kit_number.in_(kit_numbers))
            res = await session.execute(stmt)
            kits = res.scalars().all()
            for k in kits:
                assert k.status == "DESTROYED"

            # Check audit log
            stmt_audit = select(AuditLog).where(
                AuditLog.table_name == "cgt_destruction_certificates"
            )
            res_audit = await session.execute(stmt_audit)
            audits = res_audit.scalars().all()
            assert len(audits) >= 1

    @pytest.mark.asyncio
    async def test_certificate_of_destruction_rejects_single_witness(self):
        """Tests that attempting destruction certificate generation with identical witnesses fails.

        @req:PRD-SYS-184
        @req:PRD-SCM-006
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-001"
        site_id = "SITE-101"
        headers = get_gateway_headers(roles="investigator", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp_fail = await client.post(
                "/api/v1/execution/rtsm/cgt/destruction-certificate",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "lot_number": "LOT-TEST",
                    "kit_numbers": ["KIT-001"],
                    "vial_identifiers": ["V-001"],
                    "destruction_reason": "Protocol deviation disposal",
                    "destruction_method": "AUTOCLAVE_BIOHAZARD",
                    "destruction_facility": "Site Lab",
                    "witness_1_id": "dr_pi_same",
                    "witness_1_name": "Dr. Same",
                    "witness_1_role": "PI",
                    "witness_1_signature_token": "tok1",
                    "witness_2_id": "dr_pi_same",  # Same user!
                    "witness_2_name": "Dr. Same",
                    "witness_2_role": "PI",
                    "witness_2_signature_token": "tok2",
                },
                headers=headers,
            )
            assert resp_fail.status_code == 400
            assert "cannot be the same" in resp_fail.json()["detail"].lower()


# =========================================================================
# 5. Full End-to-End Vein-to-Vein Traceability Ledger & Part 11 Audit
# =========================================================================


class TestFullVeinToVeinTraceabilityAndPart11Audit:
    """Tests end-to-end Vein-to-Vein query endpoints and 21 CFR Part 11 audit trails.

    @req:PRD-SYS-184
    @req:PRD-SCM-005
    @req:PRD-SCM-006
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_full_traceability_ledger_query(self):
        """Tests querying full vein-to-vein ledger for a subject.

        @req:PRD-SYS-184
        @req:PRD-SCM-005
        @req:PRD-SCM-006
        @req:ADR-2199
        """
        study_id = "STUDY-CGT-TRACE"
        site_id = "SITE-101"
        subject_id = "SUBJ-TRACE-100"
        headers = get_gateway_headers(roles="sponsor_admin", site_id=site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            # 1. Apheresis
            resp_aph = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "collector_id": "nurse_1",
                    "collector_name": "Nurse One",
                },
                headers=headers,
            )
            coc_id = resp_aph.json()["id"]

            # 2. Add Courier Milestones
            await client.post(
                "/api/v1/execution/rtsm/cgt/milestone",
                json={
                    "coc_id": coc_id,
                    "stage": "CRYOPRESERVATION",
                    "location_name": "Site Cryo Lab",
                    "temperature_celsius": -196.0,
                    "custodian_id": "lab_tech_1",
                    "custodian_name": "Lab Tech One",
                    "custodian_role": "Cryo Technician",
                    "seal_intact": True,
                },
                headers=headers,
            )
            await client.post(
                "/api/v1/execution/rtsm/cgt/milestone",
                json={
                    "coc_id": coc_id,
                    "stage": "COURIER_TRANSIT_OUTBOUND",
                    "location_name": "Transit Flight AA-401",
                    "temperature_celsius": -180.0,
                    "custodian_id": "courier_1",
                    "custodian_name": "Courier One",
                    "custodian_role": "Courier",
                    "seal_intact": True,
                },
                headers=headers,
            )

            # 3. Manufacturing Batch Binding
            resp_bind = await client.post(
                "/api/v1/execution/rtsm/cgt/manufacturing-bind",
                json={
                    "coc_id": coc_id,
                    "batch_id": "CART-TRACE-BATCH",
                    "manufacturing_facility_id": "CMO-NJ",
                    "operator_id": "cmo_op",
                    "operator_name": "CMO Operator",
                },
                headers=headers,
            )
            bag_barcode = resp_bind.json()["tamper_evident_barcode"]

            # 4. Bedside Verification
            await client.post(
                "/api/v1/execution/rtsm/cgt/bedside-verify",
                json={
                    "coc_id": coc_id,
                    "scanned_wristband_subject_id": subject_id,
                    "scanned_bag_barcode": bag_barcode,
                    "primary_verifier_id": "nurse_1",
                    "primary_verifier_name": "Nurse One",
                    "primary_verifier_role": "Bedside Nurse",
                    "primary_signature_token": "nurse-sig",
                    "secondary_verifier_id": "dr_pi",
                    "secondary_verifier_name": "Dr. PI",
                    "secondary_verifier_role": "PI",
                    "secondary_signature_token": "pi-sig",
                },
                headers=headers,
            )

            # 5. Query Full Traceability Ledger
            resp_trace = await client.get(
                f"/api/v1/execution/rtsm/cgt/chain-of-custody/{study_id}/{subject_id}",
                headers=headers,
            )
            assert resp_trace.status_code == 200
            trace_data = resp_trace.json()
            assert trace_data["chain_of_custody"]["subject_id"] == subject_id
            assert trace_data["chain_of_custody"]["batch_id"] == "CART-TRACE-BATCH"
            assert trace_data["chain_of_custody"]["infusion_unlocked"] is True
            assert len(trace_data["milestones"]) >= 4
            assert trace_data["bedside_verification"] is not None
            assert trace_data["bedside_verification"]["status"] == "VERIFIED"

    @pytest.mark.asyncio
    async def test_trial_locking_blocks_coc_mutations(self):
        """Tests that site-level trial lock prevents unauthorized COC modifications.

            @req:PRD-SYS-184
        @req:PRD-SCM-005
            @req:ADR-2199
        """
        study_id = "STUDY-CGT-LOCKED"
        site_id = "SITE-LOCKED-01"
        headers = get_gateway_headers(roles="sponsor_admin", site_id=site_id)

        # Lock the site
        TrialLockManager.lock_site(site_id)

        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            resp = await client.post(
                "/api/v1/execution/rtsm/cgt/apheresis",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": "SUBJ-LOCK-01",
                    "collector_id": "nurse_1",
                    "collector_name": "Nurse One",
                },
                headers=headers,
            )
            assert resp.status_code in (403, 423) or "locked" in resp.text.lower()
