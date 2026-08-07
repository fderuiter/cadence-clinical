import hashlib
import hmac
import os
import time
from datetime import datetime

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    BiostatExport,
    ClinicalObservation,
    ClinicalSubject,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_dm", roles="Data Manager", change_reason="system_operation"
):
    """Generate Gateway signature-compliant authentication headers."""
    import json

    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest_asyncio.fixture
async def populate_test_data():
    """Populates mock subject and observations into the test database."""
    async with db_manager.get_session_maker()() as session:
        # Create a valid clinical subject
        demo_enc = encrypt_demographics(
            {
                "birthdate": "1990-05-15",
                "gender": "male",
                "race": "white",
                "arm": "Active Arm",
            }
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            site_id="SITE-A",
            encrypted_demographics=demo_enc,
        )
        session.add(subj)

        # Create EX observation (Exposure Start) to calculate age & TRTSDT
        ex_obs = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="EX",
            test_code="EXSTDTC",
            test_name="Exposure Start Date",
            value_string="2020-05-15",
            observation_date=datetime.fromisoformat("2020-05-15"),
        )
        session.add(ex_obs)

        # Create AE term observation
        ae_term = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Term",
            value_string="Headache",
            page_id="ae_page_1",
            observation_date=datetime.fromisoformat("2026-08-01"),
        )
        session.add(ae_term)

        # Create AE onset date
        ae_stdtc = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            test_code="AESTDTC",
            test_name="Adverse Event Onset",
            value_string="2026-08-01",
            page_id="ae_page_1",
            observation_date=datetime.fromisoformat("2026-08-01"),
        )
        session.add(ae_stdtc)

        # Create VS observation
        vs_obs = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
            unit="mmHg",
            normalized_value=120.0,
            normalized_unit="mmHg",
            page_id="vs_page_1",
            observation_date=datetime.fromisoformat("2026-08-05"),
        )
        session.add(vs_obs)

        # Add an invalid observation to support testing validation failures
        subj_invalid = ClinicalSubject(
            subject_id="SUBJ-INVALID",
            study_id="  ",  # Blank/whitespace study ID
            site_id="SITE-A",
            encrypted_demographics=encrypt_demographics(
                {
                    "birthdate": "1990-05-15",
                    "gender": "male",
                    "race": "white",
                }
            ),
        )
        session.add(subj_invalid)

        # An observation with blank study_id to fail STUDYID validation
        invalid_ex = ClinicalObservation(
            subject_id="SUBJ-INVALID",
            study_id="  ",  # Blank/whitespace study ID
            domain="EX",
            test_code="EXSTDTC",
            test_name="Exposure Start Date",
            value_string="2020-05-15",
            observation_date=datetime.fromisoformat("2020-05-15"),
        )
        session.add(invalid_ex)

        await session.commit()


@pytest.mark.asyncio
async def test_sdtm_domain_export_success(populate_test_data) -> None:
    """Verify successful export of an SDTM domain (e.g. DM) with valid authenticated headers."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="sponsor_statistician")
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001",
            headers=headers,
        )
        assert res.status_code == 200
        data = res.json()

        # Conforms to Dataset-JSON schema
        assert data["datasetJSONVersion"] == "1.0.0"
        assert "clinicalData" in data
        assert "IG.DM" in data["clinicalData"]["itemGroupData"]

        group = data["clinicalData"]["itemGroupData"]["IG.DM"]
        assert group["records"] == 1
        assert len(group["itemData"]) == 1

        # Verify that BiostatExport record is saved with status SUCCESS
        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(BiostatExport.export_type == "SDTM")
            db_res = await session.execute(stmt)
            export_log = db_res.scalars().first()
            assert export_log is not None
            assert export_log.status == "SUCCESS"
            assert export_log.dataset_name == "DM"


@pytest.mark.asyncio
async def test_adam_dataset_export_success(populate_test_data) -> None:
    """Verify successful derivation and export of an ADaM dataset (e.g. ADSL) with valid credentials."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        res = await client.get(
            "/api/v1/execution/biostat/adam/ADSL?study_id=STUDY-001",
            headers=headers,
        )
        assert res.status_code == 200
        data = res.json()

        assert data["datasetJSONVersion"] == "1.0.0"
        assert "IG.ADSL" in data["clinicalData"]["itemGroupData"]

        # Verify BiostatExport log
        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(BiostatExport.export_type == "ADaM")
            db_res = await session.execute(stmt)
            export_log = db_res.scalars().first()
            assert export_log is not None
            assert export_log.status == "SUCCESS"
            assert export_log.dataset_name == "ADSL"


@pytest.mark.asyncio
async def test_biostat_bundle_export_success(populate_test_data) -> None:
    """Verify successful export of bundled SDTM/ADaM datasets in a single bundle."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="CRA")
        res = await client.get(
            "/api/v1/execution/biostat/bundle?study_id=STUDY-001",
            headers=headers,
        )
        if res.status_code != 200:
            print("BUNDLE EXPORT FAILURE DETAIL:", res.json())
        assert res.status_code == 200
        data = res.json()

        assert data["datasetJSONVersion"] == "1.0.0"
        groups = data["clinicalData"]["itemGroupData"]
        assert "IG.DM" in groups
        assert "IG.AE" in groups
        assert "IG.VS" in groups
        assert "IG.ADSL" in groups
        assert "IG.ADAE" in groups
        assert "IG.ADVS" in groups

        # Verify BiostatExport log
        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(BiostatExport.export_type == "BUNDLE")
            db_res = await session.execute(stmt)
            export_log = db_res.scalars().first()
            assert export_log is not None
            assert export_log.status == "SUCCESS"
            assert export_log.dataset_name is None


@pytest.mark.asyncio
async def test_invalid_sdtm_domain_rejection() -> None:
    """Verify that requesting an invalid/unsupported SDTM domain returns HTTP 400."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/INVALID_DOM?study_id=STUDY-001",
            headers=headers,
        )
        assert res.status_code == 400
        assert "Unsupported SDTM domain" in res.json()["detail"]


@pytest.mark.asyncio
async def test_invalid_adam_dataset_rejection() -> None:
    """Verify that requesting an invalid/unsupported ADaM dataset returns HTTP 400."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        res = await client.get(
            "/api/v1/execution/biostat/adam/INVALID_ADAM?study_id=STUDY-001",
            headers=headers,
        )
        assert res.status_code == 400
        assert "Unsupported ADaM dataset" in res.json()["detail"]


@pytest.mark.asyncio
async def test_export_validation_failure_handling(populate_test_data) -> None:
    """Verify that Dataset-JSON validation failure triggers HTTP 422 and logs FAILED status."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=  ",
            headers=headers,
        )
        assert res.status_code == 422
        assert "Dataset-JSON validation failed" in res.json()["detail"]

        # Verify FAILED log entry
        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(
                BiostatExport.export_type == "SDTM", BiostatExport.status == "FAILED"
            )
            db_res = await session.execute(stmt)
            export_log = db_res.scalars().first()
            assert export_log is not None
            assert "STUDYID is empty or missing" in export_log.error_message


@pytest.mark.asyncio
async def test_unauthenticated_access_rejection() -> None:
    """Verify that requests missing proper gateway authentication are blocked with HTTP 401/403."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Request without any headers
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001",
        )
        assert res.status_code == 401
        assert "Missing gateway authentication headers" in res.json()["detail"]
