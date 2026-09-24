"""In Vitro Diagnostic (IVD) Specimen Suite & FDA GUDID Verification Unit and Integration Tests.

Requirements:
- PRD-SYS-188: In Vitro Diagnostic (IVD) Specimen & Assay Investigation Suite
- PRD-SYS-053: GxP Quality and Part 11 Audit Trail Verification
- ISO 20916:2019 / EU IVDR 2017/746 / 21 CFR Part 809 / 21 CFR Part 11
- ADR-2209 / ADR-2199
"""

import os

import httpx
import pytest
import pytest_asyncio

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
)
from apps.execution.device_investigation import (
    lookup_gudid,
    verify_udi_with_gudid,
)
from apps.execution.domain.device_models import (
    IVDAssayPerformanceRequest,
    IVDContainerType,
    IVDIntegrityStatus,
    IVDSampleIntegrityCheckRequest,
    IVDSpecimenCreateRequest,
    IVDSpecimenCustodyEventRequest,
    IVDSpecimenFreezeThawEventRequest,
    IVDSpecimenStatus,
    IVDSpecimenType,
)
from apps.execution.ivd_specimen import (
    compute_and_record_assay_performance,
    get_ivd_specimen,
    log_specimen_custody_event,
    log_specimen_freeze_thaw_event,
    register_ivd_specimen,
    validate_sample_integrity,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from packages.security.rbac import (
    ROLE_CRC,
    ROLE_SPONSOR_ADMIN,
    Principal,
    get_principal,
)
from packages.testing.security import create_test_auth_headers

pytestmark = pytest.mark.xdist_group("ivd_specimen_tests")


@pytest_asyncio.fixture(autouse=True)
async def setup_ivd_db():
    """Initializes in-memory database with triggers for IVD testing."""
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
    await db_manager.engine.dispose()
    TrialLockManager.reset()


@pytest.fixture
def mock_principal_sponsor():
    return Principal(
        user_id="sponsor_ivd_admin",
        roles=[ROLE_SPONSOR_ADMIN, ROLE_CRC],
        study_id="STUDY-IVD-101",
        site_id="SITE-101",
        token="token_ivd_test",
    )


def test_fda_gudid_lookup_and_udi_verification():
    """Tests Unique Device Identifier (UDI) verification against FDA AccessGUDID database.

    @req:PRD-SYS-188
    @req:PRD-SYS-053
    """
    # 1. Lookup existing GUDID record
    gudid_rec = lookup_gudid("00840123456786")
    assert gudid_rec is not None
    assert gudid_rec.brand_name == "CardioPulse Active Pacemaker"
    assert gudid_rec.device_class == "Class III"
    assert gudid_rec.premarket_submission_number == "P190001"
    assert gudid_rec.is_single_use is True
    assert gudid_rec.has_serial_number is True

    # 2. Verify valid scanned UDI with all required PIs
    res_valid = verify_udi_with_gudid(
        raw_barcode="(01)00840123456786(17)281231(10)LOT-GUDID-01(21)SN-9988",
    )
    assert res_valid.is_in_gudid is True
    assert res_valid.is_checksum_valid is True
    assert res_valid.is_verified is True
    assert res_valid.pi_validation.get("has_lot_number") is True
    assert res_valid.pi_validation.get("has_serial_number") is True
    assert res_valid.pi_validation.get("has_expiration_date") is True
    assert len(res_valid.discrepancies) == 0

    # 3. Verify missing required Production Identifier (Serial number missing for Class III device)
    res_missing_sn = verify_udi_with_gudid(
        udi_di="00840123456786",
        lot_number="LOT-GUDID-01",
        expiration_date="2028-12-31",
    )
    assert res_missing_sn.is_in_gudid is True
    assert res_missing_sn.is_verified is False
    assert any("Serial number" in d for d in res_missing_sn.discrepancies)


@pytest.mark.asyncio
async def test_ivd_specimen_registration_and_retrieval():
    """Tests registering an IVD specimen and retrieving its status and attributes.

    @req:PRD-SYS-188
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        req = IVDSpecimenCreateRequest(
            study_id="STUDY-IVD-101",
            site_id="SITE-101",
            subject_id="SUBJ-101-0042",
            specimen_identifier="SPEC-101-0042-SERUM-01",
            specimen_type=IVDSpecimenType.SERUM,
            container_type=IVDContainerType.SST_TUBE,
            initial_volume_ul=1500.0,
            storage_temperature_celsius=-80.0,
            storage_location="FREEZER_ULT_01_RACK_A_SLOT_05",
            max_allowed_freeze_thaw=3,
            reason_for_change="Intake serum specimen",
        )
        specimen = await register_ivd_specimen(
            session=session,
            request=req,
            user_id="lab_tech_01",
            change_reason="Initial collection",
        )
        await session.commit()

        assert specimen.specimen_identifier == "SPEC-101-0042-SERUM-01"
        assert specimen.status == IVDSpecimenStatus.COLLECTED.value
        assert specimen.is_quarantined is False
        assert specimen.initial_volume_ul == 1500.0
        assert specimen.freeze_thaw_count == 0

        # Query back
        fetched = await get_ivd_specimen(session, "SPEC-101-0042-SERUM-01")
        assert fetched.id == specimen.id
        assert fetched.storage_location == "FREEZER_ULT_01_RACK_A_SLOT_05"


@pytest.mark.asyncio
async def test_ivd_specimen_chain_of_custody_and_seal_breach():
    """Tests specimen chain-of-custody logging and automated quarantine upon broken seal.

    @req:PRD-SYS-188
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # 1. Register specimen
        req = IVDSpecimenCreateRequest(
            study_id="STUDY-IVD-101",
            site_id="SITE-101",
            subject_id="SUBJ-101-0042",
            specimen_identifier="SPEC-CUSTODY-01",
            specimen_type=IVDSpecimenType.PLASMA,
            container_type=IVDContainerType.EDTA_TUBE,
            initial_volume_ul=2000.0,
            reason_for_change="Plasma collection",
        )
        await register_ivd_specimen(session, req, "crc_01", "Plasma collection")
        await session.commit()

        # 2. Log regular custody transfer (intact seal)
        custody_req_1 = IVDSpecimenCustodyEventRequest(
            from_user_id="crc_01",
            to_user_id="courier_fedex",
            from_location="SITE-101-CENTRAL_LAB",
            to_location="IN_TRANSIT_COLD_CHAIN",
            transport_condition="DRY_ICE_-80C",
            temperature_at_transfer_celsius=-78.5,
            seal_intact=True,
            reason_for_change="Courier dispatch",
        )
        ev1 = await log_specimen_custody_event(
            session, "SPEC-CUSTODY-01", custody_req_1, "crc_01", "Dispatch"
        )
        await session.commit()
        assert ev1.seal_intact is True

        # 3. Log custody transfer with broken security seal -> triggers quarantine
        custody_req_2 = IVDSpecimenCustodyEventRequest(
            from_user_id="courier_fedex",
            to_user_id="central_lab_accessioner",
            from_location="IN_TRANSIT_COLD_CHAIN",
            to_location="CENTRAL_BIOREPOSITORY_INTAKE",
            transport_condition="DRY_ICE_-80C",
            temperature_at_transfer_celsius=-60.0,
            seal_intact=False,
            reason_for_change="Received damaged seal container",
        )
        ev2 = await log_specimen_custody_event(
            session,
            "SPEC-CUSTODY-01",
            custody_req_2,
            "central_lab_accessioner",
            "Intake",
        )
        await session.commit()
        assert ev2.seal_intact is False

        # Verify specimen is now quarantined
        specimen = await get_ivd_specimen(session, "SPEC-CUSTODY-01")
        assert specimen.status == IVDSpecimenStatus.QUARANTINED.value
        assert specimen.is_quarantined is True
        assert "seal broken" in specimen.quarantine_reason.lower()
        assert len(specimen.custody_events) == 2


@pytest.mark.asyncio
async def test_ivd_freeze_thaw_cycles_and_max_limit_quarantine():
    """Tests freeze-thaw thermal cycle tracking and automated quarantine when exceeding threshold.

    @req:PRD-SYS-188
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # Register specimen with max 2 freeze-thaw cycles
        req = IVDSpecimenCreateRequest(
            study_id="STUDY-IVD-101",
            site_id="SITE-101",
            subject_id="SUBJ-101-0042",
            specimen_identifier="SPEC-FT-01",
            specimen_type=IVDSpecimenType.SERUM,
            max_allowed_freeze_thaw=2,
            reason_for_change="Freeze-thaw test specimen",
        )
        await register_ivd_specimen(session, req, "crc_01", "Create FT test")
        await session.commit()

        # Cycle 1: Allowed
        ft_1 = await log_specimen_freeze_thaw_event(
            session,
            "SPEC-FT-01",
            IVDSpecimenFreezeThawEventRequest(
                thaw_duration_minutes=10.0,
                thaw_temperature_celsius=22.0,
                operator_user_id="analyst_01",
                purpose="Aliquot run 1",
                is_excursion=False,
            ),
            "analyst_01",
            "Cycle 1",
        )
        await session.commit()
        assert ft_1.cycle_number == 1

        specimen_1 = await get_ivd_specimen(session, "SPEC-FT-01")
        assert specimen_1.freeze_thaw_count == 1
        assert specimen_1.is_quarantined is False

        # Cycle 2: Allowed (reaches max limit 2)
        ft_2 = await log_specimen_freeze_thaw_event(
            session,
            "SPEC-FT-01",
            IVDSpecimenFreezeThawEventRequest(
                thaw_duration_minutes=12.0,
                thaw_temperature_celsius=22.0,
                operator_user_id="analyst_01",
                purpose="Aliquot run 2",
                is_excursion=False,
            ),
            "analyst_01",
            "Cycle 2",
        )
        await session.commit()
        assert ft_2.cycle_number == 2
        specimen_2 = await get_ivd_specimen(session, "SPEC-FT-01")
        assert specimen_2.is_quarantined is False

        # Cycle 3: Exceeds max limit 2 -> Auto-Quarantine
        ft_3 = await log_specimen_freeze_thaw_event(
            session,
            "SPEC-FT-01",
            IVDSpecimenFreezeThawEventRequest(
                thaw_duration_minutes=15.0,
                thaw_temperature_celsius=22.0,
                operator_user_id="analyst_01",
                purpose="Aliquot run 3",
                is_excursion=False,
            ),
            "analyst_01",
            "Cycle 3 breach",
        )
        await session.commit()
        assert ft_3.cycle_number == 3

        specimen_3 = await get_ivd_specimen(session, "SPEC-FT-01")
        assert specimen_3.freeze_thaw_count == 3
        assert specimen_3.is_quarantined is True
        assert specimen_3.status == IVDSpecimenStatus.QUARANTINED.value
        assert "exceeded maximum allowed limit" in specimen_3.quarantine_reason


@pytest.mark.asyncio
async def test_ivd_sample_integrity_hil_validation():
    """Tests sample integrity validation with Hemolysis, Lipemia, and Icterus (HIL) indices.

    @req:PRD-SYS-188
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # Register specimen
        req = IVDSpecimenCreateRequest(
            study_id="STUDY-IVD-101",
            site_id="SITE-101",
            subject_id="SUBJ-101-0042",
            specimen_identifier="SPEC-HIL-01",
            specimen_type=IVDSpecimenType.SERUM,
            reason_for_change="HIL test specimen",
        )
        await register_ivd_specimen(session, req, "crc_01", "Create HIL test")
        await session.commit()

        # 1. Valid sample integrity check (all indices within acceptable limits)
        check_valid = await validate_sample_integrity(
            session,
            "SPEC-HIL-01",
            IVDSampleIntegrityCheckRequest(
                hemolysis_index_mg_dl=25.0,  # <= 100
                lipemia_index_mg_dl=80.0,  # <= 300
                icterus_index_mg_dl=4.0,  # <= 20
                clot_detected=False,
                volume_adequate=True,
            ),
            "lab_tech_01",
            "Valid HIL check",
        )
        await session.commit()
        assert check_valid.integrity_status == IVDIntegrityStatus.VALID.value
        assert check_valid.is_quarantined is False

        # 2. Compromised sample check (severe hemolysis > 100 mg/dL and clot detected)
        check_compromised = await validate_sample_integrity(
            session,
            "SPEC-HIL-01",
            IVDSampleIntegrityCheckRequest(
                hemolysis_index_mg_dl=250.0,  # > 100 -> Hemolysis breach
                lipemia_index_mg_dl=120.0,
                icterus_index_mg_dl=5.0,
                clot_detected=True,  # Clot breach
                volume_adequate=True,
            ),
            "lab_tech_01",
            "Compromised HIL check",
        )
        await session.commit()
        assert (
            check_compromised.integrity_status == IVDIntegrityStatus.COMPROMISED.value
        )
        assert check_compromised.is_quarantined is True
        assert "Hemolysis" in check_compromised.quarantine_reason
        assert "Microclots" in check_compromised.quarantine_reason


@pytest.mark.asyncio
async def test_ivd_assay_performance_metrics_calculation():
    """Tests calculation and evaluation of IVD assay statistical performance metrics (Sensitivity, Specificity, PPV, NPV, CV%, LOD/LOQ).

    @req:PRD-SYS-188
    @req:PRD-SYS-053
    """
    async with db_manager.session_maker() as session:
        # TP=95, FP=5, TN=95, FN=5 -> Sens=95%, Spec=95%, PPV=95%, NPV=95%, Acc=95%
        # Replicates: [100.0, 101.5, 99.2, 100.8, 98.5]
        perf_req = IVDAssayPerformanceRequest(
            study_id="STUDY-IVD-101",
            assay_name="Multiplex Viral RT-PCR Assay",
            assay_lot_number="LOT-ASSAY-2026-01",
            device_udi_di="00840987654321",
            true_positives=95,
            false_positives=5,
            true_negatives=95,
            false_negatives=5,
            replicate_measurements=[100.0, 101.5, 99.2, 100.8, 98.5],
            blank_measurements=[0.05, 0.04, 0.06, 0.05, 0.05],
            linearity_concentrations=[1.0, 5.0, 10.0, 50.0, 100.0],
            linearity_signals=[1.02, 5.08, 9.95, 50.3, 100.1],
            reason_for_change="Validate IVD multiplex assay",
        )
        perf_res = await compute_and_record_assay_performance(
            session, perf_req, "biostat_01", "Compute assay validation"
        )
        await session.commit()

        assert perf_res.sensitivity_percent == 95.0
        assert perf_res.specificity_percent == 95.0
        assert perf_res.ppv_percent == 95.0
        assert perf_res.npv_percent == 95.0
        assert perf_res.accuracy_percent == 95.0
        assert perf_res.intra_assay_cv_percent < 5.0  # Precision is high
        assert perf_res.limit_of_detection is not None
        assert perf_res.limit_of_quantitation is not None
        assert perf_res.linearity_r_squared is not None
        assert perf_res.linearity_r_squared >= 0.99
        assert perf_res.passes_acceptance_criteria is True
        assert perf_res.status == "PASSED"


@pytest.mark.asyncio
async def test_ivd_rest_endpoints_integration(mock_principal_sponsor):
    """Tests FastAPI REST endpoints for the complete IVD biospecimen workflow.

    @req:PRD-SYS-188
    @req:PRD-SYS-053
    """
    app.dependency_overrides[get_principal] = lambda: mock_principal_sponsor
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        headers = create_test_auth_headers(
            user_id="sponsor_ivd_admin",
            roles=[ROLE_SPONSOR_ADMIN, ROLE_CRC],
            change_reason="IVD API verification",
        )

        # 1. GUDID Lookup
        gudid_res = await client.post(
            "/api/v1/execution/devices/gudid/lookup",
            json={"udi_di": "00840987654321"},
            headers=headers,
        )
        assert gudid_res.status_code == 200
        assert gudid_res.json()["brand_name"] == "Rapid Viral IVD Assay Kit"

        # 2. UDI Verification
        verify_res = await client.post(
            "/api/v1/execution/devices/verify-udi?udi_di=00840987654321&lot_number=LOT-101&expiration_date=2028-12-31",
            headers=headers,
        )
        assert verify_res.status_code == 200
        assert verify_res.json()["is_verified"] is True

        # 3. Specimen Collection Intake
        spec_create_res = await client.post(
            "/api/v1/execution/devices/ivd/specimens",
            json={
                "study_id": "STUDY-IVD-101",
                "site_id": "SITE-101",
                "subject_id": "SUBJ-101-0042",
                "specimen_identifier": "SPEC-API-TEST-01",
                "specimen_type": "SERUM",
                "container_type": "SST_TUBE",
                "initial_volume_ul": 1200.0,
                "reason_for_change": "API Specimen Intake",
            },
            headers=headers,
        )
        assert spec_create_res.status_code == 201
        assert spec_create_res.json()["specimen_identifier"] == "SPEC-API-TEST-01"

        # 4. List Specimens
        list_res = await client.get(
            "/api/v1/execution/devices/ivd/specimens?study_id=STUDY-IVD-101",
            headers=headers,
        )
        assert list_res.status_code == 200
        assert len(list_res.json()) >= 1

        # 5. Log Freeze-Thaw
        ft_res = await client.post(
            "/api/v1/execution/devices/ivd/specimens/SPEC-API-TEST-01/freeze-thaw",
            json={
                "thaw_duration_minutes": 8.0,
                "thaw_temperature_celsius": 21.5,
                "operator_user_id": "lab_tech_01",
                "purpose": "First Thaw Aliquot",
                "reason_for_change": "Thaw run",
            },
            headers=headers,
        )
        assert ft_res.status_code == 201
        assert ft_res.json()["cycle_number"] == 1

        # 6. Sample Integrity Check
        hil_res = await client.post(
            "/api/v1/execution/devices/ivd/specimens/SPEC-API-TEST-01/integrity-check",
            json={
                "hemolysis_index_mg_dl": 15.0,
                "lipemia_index_mg_dl": 45.0,
                "icterus_index_mg_dl": 2.0,
                "clot_detected": False,
                "volume_adequate": True,
                "reason_for_change": "Intake integrity check",
            },
            headers=headers,
        )
        assert hil_res.status_code == 200
        assert hil_res.json()["integrity_status"] == "VALID"

        # 7. Assay Performance
        perf_res = await client.post(
            "/api/v1/execution/devices/ivd/assays/performance",
            json={
                "study_id": "STUDY-IVD-101",
                "assay_name": "Rapid Antigen Assay",
                "assay_lot_number": "LOT-AG-01",
                "true_positives": 50,
                "false_positives": 2,
                "true_negatives": 50,
                "false_negatives": 1,
                "replicate_measurements": [10.2, 10.5, 9.8, 10.1],
                "reason_for_change": "Validate Rapid Antigen",
            },
            headers=headers,
        )
        assert perf_res.status_code == 201
        assert perf_res.json()["status"] == "PASSED"
        assert perf_res.json()["sensitivity_percent"] > 90.0

    app.dependency_overrides.clear()
