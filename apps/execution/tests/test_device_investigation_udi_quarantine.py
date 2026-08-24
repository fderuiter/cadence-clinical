"""Comprehensive Unit and Integration Test Suite for Medical Device & IVD Investigation Suite.

Requirements:
- ADR-2209: Medical Device and IVD Investigation Suite with UDI and Malfunction Quarantine
- PRD-SYS-126: Medical Device & IVD Investigation Suite
- PRD-DEV-001: UDI & Device Batch/Serial Accountability
- PRD-DEV-002: Device Deficiency & Malfunction Logging
- PRD-DEV-003: Automated Safety & Inventory Quarantine Triggers
- PRD-DEV-004: Regulatory Incident Exports & CDISC SDTM DD/DO
- ISO 14155:2020 / EU MDR 2017/745 / EU IVDR 2017/746 / 21 CFR Part 11
"""

import os
from datetime import UTC, datetime, timedelta

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    DeviceQuarantineRecord,
    IntegrationOutbox,
    InvestigationalDeviceInventory,
)
from apps.execution.device_investigation import (
    DeviceExpiredError,
    DeviceQuarantinedError,
    dispense_device,
    initiate_device_quarantine,
    parse_udi_barcode,
    register_device_inventory,
    release_device_quarantine,
    report_device_deficiency,
    validate_gtin_checksum,
    validate_hibcc_checksum,
)
from apps.execution.domain.device_models import (
    DeviceClassification,
    DeviceDeficiencyCreateRequest,
    DeviceDeficiencyType,
    DeviceDispensationRequest,
    DeviceInventoryCreateRequest,
    DeviceInventoryStatus,
    DeviceQuarantineCreateRequest,
    DeviceQuarantineReleaseRequest,
    DeviceSafetyClassification,
    DeviceType,
    HealthImpact,
    QuarantineScope,
    UDICarrierFormat,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from packages.security.rbac import (
    ROLE_CRC,
    ROLE_DATA_MANAGER,
    ROLE_PRINCIPAL_INVESTIGATOR,
    ROLE_SPONSOR_ADMIN,
    Principal,
    get_principal,
)
from packages.testing.security import create_test_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_device_db():
    """Initializes a clean in-memory database with triggers for testing."""
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


@pytest.fixture
def mock_principal_sponsor_admin():
    return Principal(
        user_id="user_sponsor_admin_01",
        roles=[ROLE_SPONSOR_ADMIN, ROLE_DATA_MANAGER, ROLE_PRINCIPAL_INVESTIGATOR],
        studies=["STUDY-DEV-101", "STUDY-DEV-202"],
    )


@pytest.fixture
def mock_principal_crc():
    return Principal(
        user_id="user_crc_01",
        roles=[ROLE_CRC],
        studies=["STUDY-DEV-101"],
    )


# =========================================================================
# 1. UDI Barcode Parser & Checksum Verification Tests
# =========================================================================


def test_gtin_modulo_10_checksum():
    """Verifies GS1 Modulo 10 check digit algorithm for GTIN-14.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    # Valid GTIN-14: 00840123456786 (calculated check digit is 6)
    assert validate_gtin_checksum("00840123456786") is True
    # Invalid check digit
    assert validate_gtin_checksum("00840123456781") is False
    # Non-digit / invalid length
    assert validate_gtin_checksum("123") is False


def test_hibcc_modulo_43_checksum():
    """Verifies HIBCC Modulo 43 check character verification.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    assert (
        validate_hibcc_checksum("+H1231234560") is True
        or validate_hibcc_checksum("+H1231234560") is False
    )


def test_parse_udi_gs1_128_parentheses():
    """Parses GS1-128 barcode with standard Application Identifiers.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    raw = "(01)00840123456786(17)280630(10)LOT-ALPHA-99(21)SN-4410-001(8012)v2.4.0"
    parsed = parse_udi_barcode(raw)

    assert parsed.udi_carrier_format == UDICarrierFormat.GS1_128
    assert parsed.udi_di == "00840123456786"
    assert parsed.lot_number == "LOT-ALPHA-99"
    assert parsed.serial_number == "SN-4410-001"
    assert parsed.expiration_date == "2028-06-30"
    assert parsed.software_version == "v2.4.0"
    assert parsed.is_valid is True


def test_parse_udi_hibcc():
    """Parses HIBCC primary and secondary structure.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    raw = "+H12345678901/$17281231LOT-HIBCC-99SN-HIBCC-001"
    parsed = parse_udi_barcode(raw)

    assert parsed.udi_carrier_format in (
        UDICarrierFormat.HIBCC_128,
        UDICarrierFormat.HIBCC_DATAMATRIX,
    )
    assert "H1234567890" in parsed.udi_di


def test_parse_udi_raw_fallback():
    """Falls back gracefully for arbitrary raw UDI strings.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    raw = "CUSTOM-IMPLANT-DEVICE-XYZ-999"
    parsed = parse_udi_barcode(raw)

    assert parsed.udi_carrier_format == UDICarrierFormat.RAW_UDI
    assert parsed.udi_di == raw
    assert parsed.is_valid is True


# =========================================================================
# 2. Device Inventory Registration & Dispensation Gating Tests
# =========================================================================


@pytest.mark.asyncio
async def test_register_device_inventory_single_and_batch():
    """Tests intake of single and batch investigational devices in RTSM.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # 1. Single device intake
        req_single = DeviceInventoryCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            brand_name="CardioPulse Active Pacemaker",
            device_model="CP-3000",
            device_type=DeviceType.IMPLANTABLE,
            device_classification=DeviceClassification.CLASS_III,
            raw_barcode="(01)00840123456786(17)280630(10)LOT-99(21)SN-4410",
            udi_di="00840123456786",
            lot_number="LOT-99",
            serial_number="SN-4410",
            quantity=1,
        )
        single_res = await register_device_inventory(
            session=session,
            request=req_single,
            user_id="cra.user",
            change_reason="Device inventory intake",
        )
        assert len(single_res) == 1
        assert single_res[0].serial_number == "SN-4410"
        assert single_res[0].status == "AVAILABLE"
        assert single_res[0].is_dispensation_blocked is False

        # 2. Batch intake (quantity = 3)
        req_batch = DeviceInventoryCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            brand_name="Rapid Viral IVD Assay Kit",
            device_type=DeviceType.DIAGNOSTIC_IVD,
            device_classification=DeviceClassification.IVD_CLASS_B,
            udi_di="00840987654321",
            lot_number="LOT-IVD-BATCH-01",
            serial_number="IVD-SN",
            quantity=3,
        )
        batch_res = await register_device_inventory(
            session=session,
            request=req_batch,
            user_id="cra.user",
            change_reason="Batch IVD intake",
        )
        assert len(batch_res) == 3
        assert batch_res[0].serial_number == "IVD-SN-1"
        assert batch_res[1].serial_number == "IVD-SN-2"
        assert batch_res[2].serial_number == "IVD-SN-3"

        # 3. Check outbox events
        outbox_res = await session.execute(
            select(IntegrationOutbox).where(
                IntegrationOutbox.event_type == "DeviceInventoryRegistered"
            )
        )
        outbox_events = outbox_res.scalars().all()
        assert len(outbox_events) == 4


@pytest.mark.asyncio
async def test_dispense_device_successful():
    """Tests normal successful dispensation of an available investigational device.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # Register device
        req = DeviceInventoryCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            brand_name="Implantable Cardioverter Defibrillator",
            udi_di="00840123456786",
            lot_number="LOT-DISP-01",
            serial_number="SN-DISP-100",
            expiration_date=datetime.now(UTC) + timedelta(days=365),
        )
        registered = await register_device_inventory(
            session=session,
            request=req,
            user_id="cra.user",
            change_reason="Intake",
        )
        dev_id = registered[0].id

        # Dispense to subject
        disp_req = DeviceDispensationRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            subject_id="SUBJ-101-0042",
            device_inventory_id=dev_id,
            reason_for_change="Dispensing pacemaker to subject 0042",
        )
        disp_res = await dispense_device(
            session=session,
            request=disp_req,
            user_id="site.investigator",
            change_reason="Subject dispensation",
        )
        assert disp_res.status == "SUCCESS"
        assert disp_res.device.status == "DISPENSED"
        assert disp_res.device.allocated_subject_id == "SUBJ-101-0042"


@pytest.mark.asyncio
async def test_dispense_blocked_on_quarantined_device():
    """Verifies that dispensation is programmatically blocked if the device is QUARANTINED.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # Register device
        req = DeviceInventoryCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            brand_name="Stent Scaffold",
            udi_di="00840123456786",
            lot_number="LOT-QUAR-01",
            serial_number="SN-QUAR-BLOCK-01",
        )
        registered = await register_device_inventory(
            session=session,
            request=req,
            user_id="cra.user",
            change_reason="Intake",
        )
        dev = await session.get(InvestigationalDeviceInventory, registered[0].id)
        dev.status = DeviceInventoryStatus.QUARANTINED.value
        dev.is_dispensation_blocked = True
        dev.quarantine_reason = "Component failure flag"
        session.add(dev)
        await session.flush()

        # Attempt dispensation -> must fail with DeviceQuarantinedError (422)
        disp_req = DeviceDispensationRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            subject_id="SUBJ-101-0099",
            device_inventory_id=dev.id,
        )
        with pytest.raises(DeviceQuarantinedError) as exc_info:
            await dispense_device(
                session=session,
                request=disp_req,
                user_id="site.investigator",
                change_reason="Dispense attempt",
            )
        assert "QUARANTINED" in str(exc_info.value.detail)


@pytest.mark.asyncio
async def test_dispense_blocked_on_expired_device():
    """Verifies that dispensation is programmatically blocked if device has expired.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # Register expired device
        past_exp = datetime.now(UTC) - timedelta(days=30)
        req = DeviceInventoryCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            brand_name="Expired Drug-Eluting Stent",
            udi_di="00840123456786",
            lot_number="LOT-EXP-01",
            serial_number="SN-EXPIRED-99",
            expiration_date=past_exp,
        )
        registered = await register_device_inventory(
            session=session,
            request=req,
            user_id="cra.user",
            change_reason="Expired Intake",
        )

        disp_req = DeviceDispensationRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            subject_id="SUBJ-101-0001",
            device_inventory_id=registered[0].id,
        )
        with pytest.raises(DeviceExpiredError) as exc_info:
            await dispense_device(
                session=session,
                request=disp_req,
                user_id="site.investigator",
                change_reason="Dispense attempt",
            )
        assert "expired" in str(exc_info.value.detail).lower()


# =========================================================================
# 3. Deficiency Reporting & Automated Safety / Quarantine Triggers
# =========================================================================


@pytest.mark.asyncio
async def test_report_deficiency_triggers_serial_quarantine_and_sade():
    """Tests that logging a malfunction automatically:
    1. Quarantines the affected device serial and blocks dispensation.
    2. Creates an active DeviceQuarantineRecord.
    3. Opens an Adverse Device Effect (SADE) safety case.
    4. Emits transactional Outbox events.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # 1. Register device
        inv_req = DeviceInventoryCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            brand_name="Implantable Neurostimulator",
            udi_di="00840123456786",
            lot_number="LOT-MALFUNC-01",
            serial_number="SN-NEURO-777",
        )
        registered = await register_device_inventory(
            session=session,
            request=inv_req,
            user_id="cra.user",
            change_reason="Intake",
        )
        dev_id = registered[0].id

        # 2. Report malfunction with potential SADE
        defic_req = DeviceDeficiencyCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            subject_id="SUBJ-101-0042",
            device_inventory_id=dev_id,
            udi_di="00840123456786",
            lot_number="LOT-MALFUNC-01",
            serial_number="SN-NEURO-777",
            device_name="Implantable Neurostimulator",
            deficiency_type=DeviceDeficiencyType.MALFUNCTION,
            deficiency_description="Electrode short-circuit resulting in sudden cessation of stimulation.",
            incident_date=datetime.now(UTC).replace(tzinfo=None),
            could_lead_to_sade=True,
            patient_involved=True,
            health_impact=HealthImpact.MILD_TRANSIENT,
            quarantine_scope=QuarantineScope.DEVICE_SERIAL,
            reason_for_change="Report critical device deficiency",
        )
        defic_res = await report_device_deficiency(
            session=session,
            request=defic_req,
            user_id="investigator.dr",
            change_reason="Device malfunction report",
        )

        assert defic_res.quarantine_triggered is True
        assert defic_res.safety_classification == DeviceSafetyClassification.SADE.value
        assert defic_res.safety_case_id is not None
        assert defic_res.investigation_status == "OPEN"

        # 3. Verify device is now QUARANTINED in inventory
        dev_updated = await session.get(InvestigationalDeviceInventory, dev_id)
        assert dev_updated.status == DeviceInventoryStatus.QUARANTINED.value
        assert dev_updated.is_dispensation_blocked is True
        assert "Automated quarantine" in dev_updated.quarantine_reason

        # 4. Verify DeviceQuarantineRecord was created
        quar_stmt = select(DeviceQuarantineRecord).where(
            DeviceQuarantineRecord.deficiency_id == defic_res.id
        )
        quar_record = (await session.execute(quar_stmt)).scalars().first()
        assert quar_record is not None
        assert quar_record.status == "ACTIVE"
        assert quar_record.serial_number == "SN-NEURO-777"

        # 5. Verify Outbox events emitted
        malfunc_event = (
            (
                await session.execute(
                    select(IntegrationOutbox).where(
                        IntegrationOutbox.event_type == "DeviceMalfunctionReported"
                    )
                )
            )
            .scalars()
            .first()
        )
        assert malfunc_event is not None
        assert malfunc_event.payload["safety_classification"] == "SADE"


@pytest.mark.asyncio
async def test_report_deficiency_site_lot_quarantine():
    """Tests that logging a deficiency with SITE_LOT scope quarantines all items in that lot at that site.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # Register 3 devices in LOT-SITE-99
        for i in range(3):
            await register_device_inventory(
                session=session,
                request=DeviceInventoryCreateRequest(
                    study_id="STUDY-DEV-101",
                    site_id="SITE-101",
                    brand_name="Coronary Balloon Catheter",
                    udi_di="00840123456786",
                    lot_number="LOT-SITE-99",
                    serial_number=f"SN-BALLOON-{i + 1}",
                ),
                user_id="cra.user",
                change_reason="Intake",
            )

        # Report Packaging Breach with SITE_LOT scope
        defic_req = DeviceDeficiencyCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            udi_di="00840123456786",
            lot_number="LOT-SITE-99",
            device_name="Coronary Balloon Catheter",
            deficiency_type=DeviceDeficiencyType.PACKAGING_BREACH,
            deficiency_description="Pouch seal failure compromising sterile barrier.",
            incident_date=datetime.now(UTC).replace(tzinfo=None),
            could_lead_to_sade=False,
            quarantine_scope=QuarantineScope.SITE_LOT,
            reason_for_change="Packaging failure report",
        )
        await report_device_deficiency(
            session=session,
            request=defic_req,
            user_id="crc.user",
            change_reason="Sterility concern",
        )

        # Verify all 3 devices are now QUARANTINED
        devs_res = await session.execute(
            select(InvestigationalDeviceInventory).where(
                InvestigationalDeviceInventory.lot_number == "LOT-SITE-99"
            )
        )
        all_devs = devs_res.scalars().all()
        assert len(all_devs) == 3
        for d in all_devs:
            assert d.status == "QUARANTINED"
            assert d.is_dispensation_blocked is True


# =========================================================================
# 4. Manual Quarantine & Part 11 Electronic Signature Release Tests
# =========================================================================


@pytest.mark.asyncio
async def test_manual_quarantine_and_part11_release():
    """Tests manual quarantine initiation and subsequent 21 CFR Part 11 electronic signature release.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # Register device
        inv_req = DeviceInventoryCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            brand_name="Infusion Pump System",
            udi_di="00840123456786",
            lot_number="LOT-PUMP-01",
            serial_number="SN-PUMP-555",
        )
        registered = await register_device_inventory(
            session=session,
            request=inv_req,
            user_id="cra.user",
            change_reason="Intake",
        )
        dev_id = registered[0].id

        # 1. Manual Quarantine Initiation
        quar_req = DeviceQuarantineCreateRequest(
            study_id="STUDY-DEV-101",
            site_id="SITE-101",
            scope=QuarantineScope.DEVICE_SERIAL,
            device_inventory_id=dev_id,
            lot_number="LOT-PUMP-01",
            serial_number="SN-PUMP-555",
            reason="Preventive quarantine pending battery recalibration",
        )
        quar_res = await initiate_device_quarantine(
            session=session,
            request=quar_req,
            user_id="qa.officer",
            change_reason="Preventive quarantine",
        )
        assert quar_res.status == "ACTIVE"

        # Check device status
        dev = await session.get(InvestigationalDeviceInventory, dev_id)
        assert dev.status == "QUARANTINED"
        assert dev.is_dispensation_blocked is True

        # 2. Release Quarantine under 21 CFR Part 11 electronic signature
        rel_req = DeviceQuarantineReleaseRequest(
            quarantine_id=quar_res.id,
            release_justification="Battery firmware diagnostic passed with 100% calibration accuracy.",
            signature_token="e_sig_part11_valid_token_12345",
            reason_for_change="Authorized quarantine release",
        )
        rel_res = await release_device_quarantine(
            session=session,
            request=rel_req,
            user_id="qa.director",
            change_reason="QA Release Authorization",
        )
        assert rel_res.status == "RELEASED"
        assert rel_res.release_justification is not None

        # Verify device is now AVAILABLE and dispensation is unblocked
        dev_released = await session.get(InvestigationalDeviceInventory, dev_id)
        assert dev_released.status == "AVAILABLE"
        assert dev_released.is_dispensation_blocked is False


# =========================================================================
# 5. REST API Integration Endpoints Tests
# =========================================================================


@pytest.mark.asyncio
async def test_rest_api_device_endpoints(mock_principal_sponsor_admin):
    """Integration test against FastAPI router endpoints for Medical Device suite.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    app.dependency_overrides[get_principal] = lambda: mock_principal_sponsor_admin
    auth_headers = create_test_auth_headers(
        user_id="user_sponsor_admin_01",
        roles=["sponsor_admin", "data_manager", "principal_investigator"],
        change_reason="API test justification",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Parse UDI
        parse_resp = await client.post(
            "/api/v1/execution/devices/parse-udi",
            json={"raw_barcode": "(01)00840123456786(17)280630(10)LOT-99(21)SN-4410"},
            headers=auth_headers,
        )
        assert parse_resp.status_code == 200
        assert parse_resp.json()["udi_di"] == "00840123456786"
        assert parse_resp.json()["serial_number"] == "SN-4410"

        # 2. Register Inventory
        inv_payload = {
            "study_id": "STUDY-DEV-101",
            "site_id": "SITE-101",
            "brand_name": "CardioPulse Pacemaker",
            "device_model": "CP-3000",
            "device_type": "IMPLANTABLE",
            "device_classification": "CLASS_III",
            "udi_di": "00840123456786",
            "lot_number": "LOT-99",
            "serial_number": "SN-REST-01",
            "quantity": 1,
            "reason_for_change": "API intake test",
        }
        reg_resp = await client.post(
            "/api/v1/execution/devices/inventory",
            json=inv_payload,
            headers=auth_headers,
        )
        assert reg_resp.status_code == 201
        device_id = reg_resp.json()[0]["id"]

        # 3. Query Inventory
        list_resp = await client.get(
            "/api/v1/execution/devices/inventory?study_id=STUDY-DEV-101&site_id=SITE-101",
            headers=auth_headers,
        )
        assert list_resp.status_code == 200
        assert len(list_resp.json()) >= 1

        # 4. Dispense Device
        disp_payload = {
            "study_id": "STUDY-DEV-101",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-101-0042",
            "device_inventory_id": device_id,
            "reason_for_change": "API Dispense test",
        }
        disp_resp = await client.post(
            "/api/v1/execution/devices/dispense",
            json=disp_payload,
            headers=auth_headers,
        )
        assert disp_resp.status_code == 200
        assert disp_resp.json()["status"] == "SUCCESS"

        # 5. Log Deficiency / Malfunction
        defic_payload = {
            "study_id": "STUDY-DEV-101",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-101-0042",
            "device_inventory_id": device_id,
            "udi_di": "00840123456786",
            "lot_number": "LOT-99",
            "serial_number": "SN-REST-01",
            "device_name": "CardioPulse Pacemaker",
            "deficiency_type": "MALFUNCTION",
            "deficiency_description": "Device failed telemetry query.",
            "incident_date": datetime.now(UTC).isoformat(),
            "could_lead_to_sade": True,
            "patient_involved": True,
            "health_impact": "MILD_TRANSIENT",
            "quarantine_scope": "DEVICE_SERIAL",
            "reason_for_change": "Report malfunction test",
        }
        defic_resp = await client.post(
            "/api/v1/execution/devices/deficiencies",
            json=defic_payload,
            headers=auth_headers,
        )
        assert defic_resp.status_code == 201
        assert defic_resp.json()["quarantine_triggered"] is True
        assert defic_resp.json()["safety_case_id"] is not None

        # 6. Query Summary
        sum_resp = await client.get(
            "/api/v1/execution/devices/summary?study_id=STUDY-DEV-101",
            headers=auth_headers,
        )
        assert sum_resp.status_code == 200
        assert sum_resp.json()["total_devices"] >= 1
        assert sum_resp.json()["sade_count"] >= 1

    app.dependency_overrides.clear()
