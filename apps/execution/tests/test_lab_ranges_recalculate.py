import asyncio
import hashlib
import hmac
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select, update

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalObservation,
    LabReferenceRange,
)
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user", roles="cra", change_reason="system_operation"
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
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_lab_range_evaluation_and_recalculation_gxp() -> None:
    """Verify that clinical observations evaluate lab ranges during creation,
    recalculation correctly updates modified indicators, and audit logging/versioning
    conventions are preserved without changing outlier semantics.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a clinical subject
        subject_payload = {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-LAB",
            "demographics": {
                "name": "Jane Smith",
                "birthdate": "1990-01-01",
                "gender": "F",
            },
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_subj.status_code == 200

        # 2. Insert LabReferenceRange into database
        async with db_manager.get_session_maker()() as session, session.begin():
            ref_range = LabReferenceRange(
                study_id="STUDY-LAB",
                test_code="WBC",
                test_name="White Blood Cell Count",
                source="CENTRAL",
                site_id=None,
                unit="10^9/L",
                normalized_unit="10^9/L",
                sex_applicability="ALL",
                age_low=None,
                age_high=None,
                low_bound=4.0,
                high_bound=11.0,
                critical_low=2.0,
                critical_high=20.0,
            )
            session.add(ref_range)

        # 3. Create active observations
        # Observation 1: NORMAL value (5.0)
        obs_1_payload = {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-LAB",
            "domain": "LB",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "value": 5.0,
            "unit": "10^9/L",
        }
        res_obs_1 = await client.post(
            "/api/v1/execution/observations",
            json=obs_1_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_obs_1.status_code == 200
        obs_1_data = res_obs_1.json()
        assert obs_1_data["lab_indicator"] == "NORMAL"
        assert obs_1_data["lab_out_of_range"] is False
        assert obs_1_data["range_indicator"] == "NORMAL"
        assert obs_1_data["is_out_of_range"] is False
        assert obs_1_data["reference_range_low"] == 4.0
        assert obs_1_data["reference_range_high"] == 11.0

        # Observation 2: LOW value (3.0)
        obs_2_payload = {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-LAB",
            "domain": "LB",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "value": 3.0,
            "unit": "10^9/L",
        }
        res_obs_2 = await client.post(
            "/api/v1/execution/observations",
            json=obs_2_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_obs_2.status_code == 200
        obs_2_data = res_obs_2.json()
        assert obs_2_data["lab_indicator"] == "LOW"
        assert obs_2_data["lab_out_of_range"] is True
        assert obs_2_data["range_indicator"] == "LOW"
        assert obs_2_data["is_out_of_range"] is True
        assert obs_2_data["reference_range_low"] == 4.0
        assert obs_2_data["reference_range_high"] == 11.0

        # Observation 3: CRITICAL LOW value (1.0)
        obs_3_payload = {
            "subject_id": "SUBJ-001",
            "study_id": "STUDY-LAB",
            "domain": "LB",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "value": 1.0,
            "unit": "10^9/L",
        }
        res_obs_3 = await client.post(
            "/api/v1/execution/observations",
            json=obs_3_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_obs_3.status_code == 200
        obs_3_data = res_obs_3.json()
        assert obs_3_data["lab_indicator"] == "LOW LOW"
        assert obs_3_data["lab_out_of_range"] is True
        assert obs_3_data["range_indicator"] == "LOW LOW"
        assert obs_3_data["is_out_of_range"] is True
        assert obs_3_data["reference_range_low"] == 4.0
        assert obs_3_data["reference_range_high"] == 11.0

        # 4. Modify the reference range in database to make LOW value (3.0) NORMAL
        async with db_manager.get_session_maker()() as session, session.begin():
            stmt = (
                update(LabReferenceRange)
                .where(
                    LabReferenceRange.study_id == "STUDY-LAB",
                    LabReferenceRange.test_code == "WBC",
                )
                .values(low_bound=2.5)
            )
            await session.execute(stmt)

        # 5. Invoke batch recalculation on the lab range endpoint using CRA role
        recalc_payload = {
            "study_id": "STUDY-LAB",
            "test_code": "WBC",
        }
        res_recalc = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=get_auth_headers(roles="cra", change_reason="Recalculating limits"),
        )
        assert res_recalc.status_code == 200
        recalc_data = res_recalc.json()
        assert recalc_data["status"] == "success"
        # Since observation 2 (3.0) was LOW and now becomes NORMAL, and all 3 observations
        # have their matched bounds snapshot updated, updated_count should be 3.
        assert recalc_data["updated_count"] == 3

        # Second recalculation should yield 0 updates because they are already up to date
        res_recalc_2 = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Recalculating limits again"
            ),
        )
        assert res_recalc_2.status_code == 200
        assert res_recalc_2.json()["updated_count"] == 0

        # 6. Verify database records are updated correctly and version incremented
        async with db_manager.get_session_maker()() as session:
            # Observation 2
            stmt_obs_2 = select(ClinicalObservation).where(
                ClinicalObservation.id == obs_2_data["id"]
            )
            res_obs_2_db = await session.execute(stmt_obs_2)
            obs_2_db = res_obs_2_db.scalar_one()
            assert obs_2_db.lab_indicator == "NORMAL"
            assert obs_2_db.lab_out_of_range is False
            assert obs_2_db.version == 2

            # Observation 1
            stmt_obs_1 = select(ClinicalObservation).where(
                ClinicalObservation.id == obs_1_data["id"]
            )
            res_obs_1_db = await session.execute(stmt_obs_1)
            obs_1_db = res_obs_1_db.scalar_one()
            assert obs_1_db.lab_indicator == "NORMAL"
            assert obs_1_db.lab_out_of_range is False
            assert obs_1_db.version == 2  # Because bounds changed from 4.0 to 2.5

            # Verify audit trail contains update for clinical_observations and correct details
            stmt_audit = select(AuditLog).where(
                AuditLog.table_name == "clinical_observations",
                AuditLog.action == "UPDATE",
            )
            res_audit = await session.execute(stmt_audit)
            audit_logs = res_audit.scalars().all()
            assert len(audit_logs) == 3
            for log in audit_logs:
                assert log.user_id == "test_user"
                assert log.change_reason == "Recalculating limits"


@pytest.mark.asyncio
async def test_lab_range_recalculation_no_match() -> None:
    """Verify that when no matching reference range exists, recalculation behaves
    consistently and does not raise a server error.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a clinical subject
        await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "SUBJ-002",
                "study_id": "STUDY-NOMATCH",
                "demographics": {"gender": "M", "birthdate": "1980-01-01"},
            },
            headers=get_auth_headers(roles="cra"),
        )

        # Create active observation for a test with no reference ranges configured
        obs_payload = {
            "subject_id": "SUBJ-002",
            "study_id": "STUDY-NOMATCH",
            "domain": "LB",
            "test_code": "GLUCOSE",
            "test_name": "Glucose",
            "value": 100.0,
            "unit": "mg/dL",
        }
        res_obs = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_obs.status_code == 200
        data = res_obs.json()
        assert data["lab_indicator"] is None
        assert data["lab_out_of_range"] is False
        assert data["matched_normal_bounds"] is None

        # Call recalculate endpoint
        recalc_payload = {
            "study_id": "STUDY-NOMATCH",
            "test_code": "GLUCOSE",
        }
        res_recalc = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_recalc.status_code == 200
        recalc_data = res_recalc.json()
        assert recalc_data["status"] == "success"
        # Since it was None and remains None, updated_count should be 0
        assert recalc_data["updated_count"] == 0


@pytest.mark.asyncio
async def test_lab_range_recalculation_unauthorized_role() -> None:
    """Verify that unauthorized / read-only role calls (e.g., roles="subject")
    are blocked with a 403 response, and no observations/versions are mutated.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create subject and observation as CRA first
        await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "SUBJ-003",
                "study_id": "STUDY-UNAUTH",
                "demographics": {"gender": "F", "birthdate": "1990-01-01"},
            },
            headers=get_auth_headers(roles="cra"),
        )

        obs_payload = {
            "subject_id": "SUBJ-003",
            "study_id": "STUDY-UNAUTH",
            "domain": "LB",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "value": 3.0,
            "unit": "10^9/L",
        }
        res_obs = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_obs.status_code == 200
        obs_id = res_obs.json()["id"]

        # Attempt to recalculate with roles="subject" (unauthorized)
        recalc_payload = {
            "study_id": "STUDY-UNAUTH",
            "test_code": "WBC",
        }
        res_recalc = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=get_auth_headers(roles="subject"),
        )
        assert res_recalc.status_code == 403

        # Confirm the observation remains unchanged (version = 1, same attributes)
        async with db_manager.get_session_maker()() as session:
            stmt = select(ClinicalObservation).where(ClinicalObservation.id == obs_id)
            res_db = await session.execute(stmt)
            obs_db = res_db.scalar_one()
            assert obs_db.version == 1


@pytest.mark.asyncio
async def test_lab_range_recalculation_missing_reason() -> None:
    """Verify that omitting X-Change-Reason header entirely is rejected with HTTP 403."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        recalc_payload = {
            "study_id": "STUDY-TEST",
            "test_code": "WBC",
        }
        # Build valid headers and remove X-Change-Reason
        headers = get_auth_headers(roles="cra")
        del headers["X-Change-Reason"]

        res = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=headers,
        )
        assert res.status_code == 403
        assert "Missing change justification reason" in res.json()["detail"]


@pytest.mark.asyncio
async def test_lab_range_recalculation_blank_reason() -> None:
    """Verify that blank/empty X-Change-Reason header is rejected with HTTP 403."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        recalc_payload = {
            "study_id": "STUDY-TEST",
            "test_code": "WBC",
        }
        # Build headers with a blank reason
        headers = get_auth_headers(roles="cra", change_reason="")
        headers["X-Change-Reason"] = ""

        res = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=headers,
        )
        assert res.status_code == 403
        assert "Missing change justification reason" in res.json()["detail"]


@pytest.mark.asyncio
async def test_lab_range_recalculation_authorized_data_manager() -> None:
    """Verify that roles="data_manager" or roles="Data Manager" with a valid change reason succeeds."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create clinical subject
        await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "SUBJ-004",
                "study_id": "STUDY-DM",
                "demographics": {"gender": "F", "birthdate": "1990-01-01"},
            },
            headers=get_auth_headers(roles="cra"),
        )

        # Create active observation
        obs_payload = {
            "subject_id": "SUBJ-004",
            "study_id": "STUDY-DM",
            "domain": "LB",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "value": 5.0,
            "unit": "10^9/L",
        }
        res_obs = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_obs.status_code == 200

        # Recalculate using roles="data_manager"
        recalc_payload = {
            "study_id": "STUDY-DM",
            "test_code": "WBC",
        }
        res_recalc_1 = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=get_auth_headers(
                roles="data_manager", change_reason="DM recalibration"
            ),
        )
        assert res_recalc_1.status_code == 200
        assert res_recalc_1.json()["status"] == "success"

        # Recalculate using normalized roles="Data Manager"
        res_recalc_2 = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=get_auth_headers(
                roles="Data Manager", change_reason="DM second recalculation"
            ),
        )
        assert res_recalc_2.status_code == 200
        assert res_recalc_2.json()["status"] == "success"


@pytest.mark.asyncio
async def test_sex_specific_range_recalculation() -> None:
    """Task 1: Verify that sex-specific ranges select correctly for subjects
    whose decrypted demographics resolve to matching and mismatching sex.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create subjects
        res_m = await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "SUBJ-M-001",
                "study_id": "STUDY-SEX-RECALC",
                "demographics": {
                    "name": "John Doe",
                    "birthdate": "1990-01-01",
                    "gender": "M",
                },
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert res_m.status_code == 200

        res_f = await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "SUBJ-F-001",
                "study_id": "STUDY-SEX-RECALC",
                "demographics": {
                    "name": "Jane Doe",
                    "birthdate": "1990-01-01",
                    "gender": "F",
                },
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert res_f.status_code == 200

        # 2. Insert two LabReferenceRange rows for same study_id and test_code
        async with db_manager.get_session_maker()() as session, session.begin():
            range_m = LabReferenceRange(
                study_id="STUDY-SEX-RECALC",
                test_code="ALT",
                test_name="Alanine Aminotransferase",
                source="CENTRAL",
                site_id=None,
                unit="U/L",
                normalized_unit="U/L",
                sex_applicability="M",
                age_low=None,
                age_high=None,
                low_bound=10.0,
                high_bound=50.0,
                critical_low=5.0,
                critical_high=100.0,
            )
            range_f = LabReferenceRange(
                study_id="STUDY-SEX-RECALC",
                test_code="ALT",
                test_name="Alanine Aminotransferase",
                source="CENTRAL",
                site_id=None,
                unit="U/L",
                normalized_unit="U/L",
                sex_applicability="F",
                age_low=None,
                age_high=None,
                low_bound=5.0,
                high_bound=35.0,
                critical_low=2.0,
                critical_high=70.0,
            )
            session.add_all([range_m, range_f])

        # 3. Post observations
        obs_m_res = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-M-001",
                "study_id": "STUDY-SEX-RECALC",
                "domain": "LB",
                "test_code": "ALT",
                "test_name": "Alanine Aminotransferase",
                "value": 45.0,
                "unit": "U/L",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert obs_m_res.status_code == 200
        obs_m_id = obs_m_res.json()["id"]

        obs_f_res = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-F-001",
                "study_id": "STUDY-SEX-RECALC",
                "domain": "LB",
                "test_code": "ALT",
                "test_name": "Alanine Aminotransferase",
                "value": 45.0,
                "unit": "U/L",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert obs_f_res.status_code == 200
        obs_f_id = obs_f_res.json()["id"]

        # 4. Trigger recalculation
        recalc_res = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json={
                "study_id": "STUDY-SEX-RECALC",
                "test_code": "ALT",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert recalc_res.status_code == 200
        assert recalc_res.json()["status"] == "success"

        # 5. Assert each observation matches the range for its derived sex
        async with db_manager.get_session_maker()() as session:
            # Query male observation
            res_m_db = await session.execute(
                select(ClinicalObservation).where(ClinicalObservation.id == obs_m_id)
            )
            obs_m_db = res_m_db.scalar_one()
            assert obs_m_db.lab_indicator == "NORMAL"  # 45 is within 10 - 50
            assert obs_m_db.lab_out_of_range is False
            assert obs_m_db.matched_normal_bounds == '{"low": 10.0, "high": 50.0}'

            # Query female observation
            res_f_db = await session.execute(
                select(ClinicalObservation).where(ClinicalObservation.id == obs_f_id)
            )
            obs_f_db = res_f_db.scalar_one()
            assert obs_f_db.lab_indicator == "HIGH"  # 45 is > 35
            assert obs_f_db.lab_out_of_range is True
            assert obs_f_db.matched_normal_bounds == '{"low": 5.0, "high": 35.0}'


@pytest.mark.asyncio
async def test_age_bounded_range_recalculation() -> None:
    """Task 2: Verify that age-bounded ranges select correctly for subjects
    of different derived ages.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create subjects with birthdates that resolve to distinct ages relative to observation_date
        # Observation date will be "2025-01-01"
        # Pediatric birthdate: "2020-01-01" -> derived age: 5 years
        res_ped = await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "SUBJ-PED-001",
                "study_id": "STUDY-AGE-RECALC",
                "demographics": {
                    "name": "Tommy Doe",
                    "birthdate": "2020-01-01",
                    "gender": "M",
                },
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert res_ped.status_code == 200

        # Adult birthdate: "1990-01-01" -> derived age: 35 years
        res_adult = await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "SUBJ-ADULT-001",
                "study_id": "STUDY-AGE-RECALC",
                "demographics": {
                    "name": "Jane Doe",
                    "birthdate": "1990-01-01",
                    "gender": "F",
                },
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert res_adult.status_code == 200

        # 2. Insert two ranges with non-overlapping age windows and distinct bounds
        async with db_manager.get_session_maker()() as session, session.begin():
            range_ped = LabReferenceRange(
                study_id="STUDY-AGE-RECALC",
                test_code="CR",
                test_name="Creatinine",
                source="CENTRAL",
                site_id=None,
                unit="mg/dL",
                normalized_unit="mg/dL",
                sex_applicability="ALL",
                age_low=0.0,
                age_high=12.0,
                low_bound=0.3,
                high_bound=0.7,
                critical_low=0.1,
                critical_high=1.5,
            )
            range_adult = LabReferenceRange(
                study_id="STUDY-AGE-RECALC",
                test_code="CR",
                test_name="Creatinine",
                source="CENTRAL",
                site_id=None,
                unit="mg/dL",
                normalized_unit="mg/dL",
                sex_applicability="ALL",
                age_low=18.0,
                age_high=100.0,
                low_bound=0.6,
                high_bound=1.2,
                critical_low=0.3,
                critical_high=2.5,
            )
            session.add_all([range_ped, range_adult])

        # 3. Post observations with a fixed observation_date
        obs_ped_res = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-PED-001",
                "study_id": "STUDY-AGE-RECALC",
                "domain": "LB",
                "test_code": "CR",
                "test_name": "Creatinine",
                "value": 0.8,
                "unit": "mg/dL",
                "observation_date": "2025-01-01T12:00:00",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert obs_ped_res.status_code == 200
        obs_ped_id = obs_ped_res.json()["id"]

        obs_adult_res = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-ADULT-001",
                "study_id": "STUDY-AGE-RECALC",
                "domain": "LB",
                "test_code": "CR",
                "test_name": "Creatinine",
                "value": 0.8,
                "unit": "mg/dL",
                "observation_date": "2025-01-01T12:00:00",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert obs_adult_res.status_code == 200
        obs_adult_id = obs_adult_res.json()["id"]

        # 4. Trigger recalculation
        recalc_res = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json={
                "study_id": "STUDY-AGE-RECALC",
                "test_code": "CR",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert recalc_res.status_code == 200
        assert recalc_res.json()["status"] == "success"

        # 5. Assert each observation selects the range whose age window contains its derived age
        async with db_manager.get_session_maker()() as session:
            # Pediatric observation
            res_ped_db = await session.execute(
                select(ClinicalObservation).where(ClinicalObservation.id == obs_ped_id)
            )
            obs_ped_db = res_ped_db.scalar_one()
            # Age 5 matches Pediatric range (0.3 - 0.7). Value 0.8 is HIGH.
            assert obs_ped_db.lab_indicator == "HIGH"
            assert obs_ped_db.lab_out_of_range is True
            assert obs_ped_db.matched_normal_bounds == '{"low": 0.3, "high": 0.7}'

            # Adult observation
            res_adult_db = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == obs_adult_id
                )
            )
            obs_adult_db = res_adult_db.scalar_one()
            # Age 35 matches Adult range (0.6 - 1.2). Value 0.8 is NORMAL.
            assert obs_adult_db.lab_indicator == "NORMAL"
            assert obs_adult_db.lab_out_of_range is False
            assert obs_adult_db.matched_normal_bounds == '{"low": 0.6, "high": 1.2}'


@pytest.mark.asyncio
async def test_missing_and_undecryptable_demographics_recalculation() -> None:
    """Task 3: Verify the safe fallback to gender="U" and age=None when demographics cannot be derived.
    Covers corrupted/un-decryptable demographics (triggers InvalidToken) and missing subject row.
    """
    from apps.execution.database.models import ClinicalSubject

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Directly insert a subject using DB session with corrupted/undecryptable demographics
        async with db_manager.get_session_maker()() as session, session.begin():
            bad_subject = ClinicalSubject(
                subject_id="SUBJ-BAD-001",
                study_id="STUDY-FALLBACK",
                encrypted_demographics="this_is_a_corrupted_not_fernet_token_and_will_fail_to_decrypt",
                enrollment_index=1,
            )
            session.add(bad_subject)

        # 2. Insert two LabReferenceRange rows for STUDY-FALLBACK and test_code PLT
        # One is sex-agnostic ("ALL") and age-unbounded (None, None)
        # One is sex-specific ("M") and age-unbounded
        async with db_manager.get_session_maker()() as session, session.begin():
            range_all = LabReferenceRange(
                study_id="STUDY-FALLBACK",
                test_code="PLT",
                test_name="Platelet Count",
                source="CENTRAL",
                site_id=None,
                unit="10^9/L",
                normalized_unit="10^9/L",
                sex_applicability="ALL",
                age_low=None,
                age_high=None,
                low_bound=150.0,
                high_bound=450.0,
                critical_low=50.0,
                critical_high=1000.0,
            )
            range_m = LabReferenceRange(
                study_id="STUDY-FALLBACK",
                test_code="PLT",
                test_name="Platelet Count",
                source="CENTRAL",
                site_id=None,
                unit="10^9/L",
                normalized_unit="10^9/L",
                sex_applicability="M",
                age_low=None,
                age_high=None,
                low_bound=100.0,
                high_bound=400.0,
                critical_low=50.0,
                critical_high=1000.0,
            )
            session.add_all([range_all, range_m])

        # 3. Post observations:
        # Obs A: subject exists but has corrupted/undecryptable demographics
        obs_a_res = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-BAD-001",
                "study_id": "STUDY-FALLBACK",
                "domain": "LB",
                "test_code": "PLT",
                "test_name": "Platelet Count",
                "value": 300.0,
                "unit": "10^9/L",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert obs_a_res.status_code == 200
        obs_a_id = obs_a_res.json()["id"]

        # Obs B: subject does not exist in the database
        obs_b_res = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-ABSENT-001",
                "study_id": "STUDY-FALLBACK",
                "domain": "LB",
                "test_code": "PLT",
                "test_name": "Platelet Count",
                "value": 300.0,
                "unit": "10^9/L",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert obs_b_res.status_code == 200
        obs_b_id = obs_b_res.json()["id"]

        # 4. Trigger recalculation
        recalc_res = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json={
                "study_id": "STUDY-FALLBACK",
                "test_code": "PLT",
            },
            headers=get_auth_headers(roles="cra"),
        )
        assert recalc_res.status_code == 200
        assert recalc_res.json()["status"] == "success"

        # 5. Assert the engine did not raise, and observations matched ONLY the sex-agnostic, age-unbounded range
        async with db_manager.get_session_maker()() as session:
            # Query Obs A
            res_a_db = await session.execute(
                select(ClinicalObservation).where(ClinicalObservation.id == obs_a_id)
            )
            obs_a_db = res_a_db.scalar_one()
            assert obs_a_db.lab_indicator == "NORMAL"
            assert obs_a_db.lab_out_of_range is False
            # Ensure it selected the ALL range (150-450) and NOT the M range (100-400)
            assert obs_a_db.matched_normal_bounds == '{"low": 150.0, "high": 450.0}'

            # Query Obs B
            res_b_db = await session.execute(
                select(ClinicalObservation).where(ClinicalObservation.id == obs_b_id)
            )
            obs_b_db = res_b_db.scalar_one()
            assert obs_b_db.lab_indicator == "NORMAL"
            assert obs_b_db.lab_out_of_range is False
            assert obs_b_db.matched_normal_bounds == '{"low": 150.0, "high": 450.0}'


@pytest.mark.asyncio
async def test_lab_range_recalculation_critical_alert() -> None:
    """Verify that recalculating range flags triggers a critical alert when transitioning into a LOW LOW state."""
    from unittest.mock import AsyncMock, patch

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a clinical subject
        await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "SUBJ-RECALC-ALERT",
                "study_id": "STUDY-RECALC-ALERT",
                "demographics": {"gender": "M", "birthdate": "1980-01-01"},
            },
            headers=get_auth_headers(roles="cra"),
        )

        # 2. Insert initially wide LabReferenceRange into database (so value 1.0 is NORMAL)
        async with db_manager.get_session_maker()() as session, session.begin():
            ref_range = LabReferenceRange(
                study_id="STUDY-RECALC-ALERT",
                test_code="WBC",
                test_name="White Blood Cell Count",
                source="CENTRAL",
                site_id=None,
                unit="10^9/L",
                normalized_unit="10^9/L",
                sex_applicability="ALL",
                age_low=None,
                age_high=None,
                low_bound=0.5,
                high_bound=11.0,
                critical_low=0.2,
                critical_high=20.0,
            )
            session.add(ref_range)

        # 3. Create active observation (value 1.0, NORMAL)
        obs_payload = {
            "subject_id": "SUBJ-RECALC-ALERT",
            "study_id": "STUDY-RECALC-ALERT",
            "domain": "LB",
            "test_code": "WBC",
            "test_name": "White Blood Cell Count",
            "value": 1.0,
            "unit": "10^9/L",
        }
        res_obs = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_auth_headers(roles="cra"),
        )
        assert res_obs.status_code == 200
        obs_data = res_obs.json()
        assert obs_data["lab_indicator"] == "NORMAL"

        # 4. Modify LabReferenceRange so value 1.0 becomes critical (LOW LOW)
        async with db_manager.get_session_maker()() as session, session.begin():
            stmt = (
                update(LabReferenceRange)
                .where(
                    LabReferenceRange.study_id == "STUDY-RECALC-ALERT",
                    LabReferenceRange.test_code == "WBC",
                )
                .values(low_bound=5.0, critical_low=2.0)
            )
            await session.execute(stmt)

        # 5. Patch publish_notification and trigger recalculate
        with patch(
            "apps.execution.notifications_client.publish_notification",
            new_callable=AsyncMock,
        ) as mock_pub:
            mock_pub.return_value = True

            recalc_payload = {
                "study_id": "STUDY-RECALC-ALERT",
                "test_code": "WBC",
            }
            res_recalc = await client.post(
                "/api/v1/execution/lab-ranges/recalculate",
                json=recalc_payload,
                headers=get_auth_headers(
                    roles="cra", change_reason="Recalculating limits to critical"
                ),
            )
            assert res_recalc.status_code == 200
            assert res_recalc.json()["updated_count"] == 1

            # Wait briefly for background tasks to process
            await asyncio.sleep(0.1)

            # 6. Verify notification was dispatched
            assert mock_pub.call_count >= 1
            notif_payload = mock_pub.call_args_list[0][0][0]
            assert notif_payload["category"] == "ALERTS"
            assert notif_payload["priority"] == "CRITICAL"
            assert notif_payload["related_entity_type"] == "lab-observation"
            assert notif_payload["related_entity_id"] == obs_data["id"]
            assert notif_payload["related_entity_subject_id"] == "SUBJ-RECALC-ALERT"
