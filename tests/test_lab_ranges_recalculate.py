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
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
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

        # 4. Modify the reference range in database to make LOW value (3.0) NORMAL
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
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
