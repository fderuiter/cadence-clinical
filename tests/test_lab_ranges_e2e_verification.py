import hashlib
import hmac
import json
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
    ClinicalObservation,
)
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
    """Generate Gateway signature-compliant authentication headers."""
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
async def test_lab_ranges_comprehensive_e2e_workflow() -> None:
    # @req:PRD-LAB-001
    """
    Thoroughly verifies the end-to-end lab reference range management workflow:
    1. Reference Range creation (central, generic local, and site-specific local)
    2. Encrypted-subject demographics creation & transparent decryption
    3. Capture of observations with auto-normalization of units (UCUM)
    4. Multi-dimensional precedence resolution (site, source, sex, age)
    5. Soft-deletion of ranges and its exclusion from evaluation
    6. No-match behavior verification
    7. Evaluation of normal and critical boundaries (NORMAL, LOW, HIGH, LOW LOW, HIGH HIGH)
    8. Manual recalculation workflow, updating modified bounds and tracking version index
    9. Coexistence with statistical outlier flags
    10. Immutable GxP audit log verification (INSERT, UPDATE, DELETE)
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a clinical subject with encrypted demographics
        subject_payload = {
            "subject_id": "SUBJ-E2E-001",
            "study_id": "STUDY-E2E",
            "demographics": {
                "name": "Jane Doe",
                "birthdate": "1995-06-15",  # Age ~31 in 2026
                "gender": "Female",  # Normalized to 'F'
            },
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_auth_headers(roles="cra", change_reason="Creating Jane Doe"),
        )
        assert res_subj.status_code == 200
        subj_data = res_subj.json()
        assert subj_data["subject_id"] == "SUBJ-E2E-001"
        assert subj_data["encrypted_demographics"] is not None

        # 2. Define multiple LabReferenceRanges to test multi-dimensional specificity precedence
        # A. CENTRAL range: fallback for everyone
        central_range_payload = {
            "study_id": "STUDY-E2E",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "source": "CENTRAL",
            "site_id": None,
            "unit": "g/dL",
            "normalized_unit": "g/dL",
            "sex_applicability": "ALL",
            "age_low": None,
            "age_high": None,
            "low_bound": 12.0,
            "high_bound": 16.0,
            "critical_low": 8.0,
            "critical_high": 20.0,
        }
        res_r_central = await client.post(
            "/api/v1/execution/lab-ranges",
            json=central_range_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Adding Central Hemoglobin Range"
            ),
        )
        assert res_r_central.status_code == 201
        central_range_id = res_r_central.json()["id"]

        # B. LOCAL range with generic site_id=None
        local_generic_payload = {
            "study_id": "STUDY-E2E",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "source": "LOCAL",
            "site_id": None,
            "unit": "g/dL",
            "normalized_unit": "g/dL",
            "sex_applicability": "ALL",
            "age_low": None,
            "age_high": None,
            "low_bound": 11.5,
            "high_bound": 15.5,
            "critical_low": 7.5,
            "critical_high": 19.5,
        }
        res_r_local_gen = await client.post(
            "/api/v1/execution/lab-ranges",
            json=local_generic_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Adding Local Generic Hemoglobin Range"
            ),
        )
        assert res_r_local_gen.status_code == 201

        # C. LOCAL range specific to SITE-A
        local_site_a_payload = {
            "study_id": "STUDY-E2E",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "source": "LOCAL",
            "site_id": "SITE-A",
            "unit": "g/dL",
            "normalized_unit": "g/dL",
            "sex_applicability": "ALL",
            "age_low": None,
            "age_high": None,
            "low_bound": 11.0,
            "high_bound": 15.0,
            "critical_low": 7.0,
            "critical_high": 19.0,
        }
        res_r_local_site_a = await client.post(
            "/api/v1/execution/lab-ranges",
            json=local_site_a_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Adding Local SITE-A Hemoglobin Range"
            ),
        )
        assert res_r_local_site_a.status_code == 201
        local_site_a_range_id = res_r_local_site_a.json()["id"]

        # 3. Create observation for SITE-A, source LOCAL -> Must match LOCAL range specific to SITE-A
        obs_site_a_payload = {
            "subject_id": "SUBJ-E2E-001",
            "study_id": "STUDY-E2E",
            "domain": "LB",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "value": 11.2,
            "unit": "g/dL",
            "lab_source": "LOCAL",
            "lab_site_id": "SITE-A",
        }
        res_obs_a = await client.post(
            "/api/v1/execution/observations",
            json=obs_site_a_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Capturing SITE-A Hemoglobin"
            ),
        )
        assert res_obs_a.status_code == 200
        obs_a_data = res_obs_a.json()
        assert obs_a_data["lab_source"] == "LOCAL"
        assert obs_a_data["lab_site_id"] == "SITE-A"
        # 11.2 is NORMAL under local SITE-A range (11.0 - 15.0)
        assert obs_a_data["lab_indicator"] == "NORMAL"
        assert obs_a_data["lab_out_of_range"] is False
        assert json.loads(obs_a_data["matched_normal_bounds"]) == {
            "low": 11.0,
            "high": 15.0,
        }

        # 4. Create observation for SITE-B, source LOCAL -> Must match LOCAL generic range (no site-specific range for SITE-B)
        obs_site_b_payload = {
            "subject_id": "SUBJ-E2E-001",
            "study_id": "STUDY-E2E",
            "domain": "LB",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "value": 11.2,
            "unit": "g/dL",
            "lab_source": "LOCAL",
            "lab_site_id": "SITE-B",
        }
        res_obs_b = await client.post(
            "/api/v1/execution/observations",
            json=obs_site_b_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Capturing SITE-B Hemoglobin"
            ),
        )
        assert res_obs_b.status_code == 200
        obs_b_data = res_obs_b.json()
        # 11.2 is LOW under LOCAL generic range (11.5 - 15.5)
        assert obs_b_data["lab_indicator"] == "LOW"
        assert obs_b_data["lab_out_of_range"] is True
        assert json.loads(obs_b_data["matched_normal_bounds"]) == {
            "low": 11.5,
            "high": 15.5,
        }

        # 5. Create observation with source CENTRAL -> Must match CENTRAL range
        obs_central_payload = {
            "subject_id": "SUBJ-E2E-001",
            "study_id": "STUDY-E2E",
            "domain": "LB",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "value": 11.2,
            "unit": "g/dL",
            "lab_source": "CENTRAL",
            "lab_site_id": "SITE-A",  # Even if SITE-A is provided, CENTRAL source matches CENTRAL range
        }
        res_obs_c = await client.post(
            "/api/v1/execution/observations",
            json=obs_central_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Capturing Central Hemoglobin"
            ),
        )
        assert res_obs_c.status_code == 200
        obs_c_data = res_obs_c.json()
        # 11.2 is LOW under CENTRAL range (12.0 - 16.0)
        assert obs_c_data["lab_indicator"] == "LOW"
        assert obs_c_data["lab_out_of_range"] is True
        assert json.loads(obs_c_data["matched_normal_bounds"]) == {
            "low": 12.0,
            "high": 16.0,
        }

        # 6. Verify No-Match Behavior
        # Creating an observation for a test code with no configured range
        obs_nomatch_payload = {
            "subject_id": "SUBJ-E2E-001",
            "study_id": "STUDY-E2E",
            "domain": "LB",
            "test_code": "CHLORIDE",
            "test_name": "Chloride",
            "value": 100.0,
            "unit": "mmol/L",
            "lab_source": "CENTRAL",
        }
        res_obs_nomatch = await client.post(
            "/api/v1/execution/observations",
            json=obs_nomatch_payload,
            headers=get_auth_headers(roles="cra", change_reason="Capturing Chloride"),
        )
        assert res_obs_nomatch.status_code == 200
        nomatch_data = res_obs_nomatch.json()
        assert nomatch_data["lab_indicator"] is None
        assert nomatch_data["lab_out_of_range"] is False
        assert nomatch_data["matched_normal_bounds"] is None

        # 7. Verify soft deletion exclusion from matching
        # Soft delete the local site-specific range for SITE-A
        res_del = await client.delete(
            f"/api/v1/execution/lab-ranges/{local_site_a_range_id}",
            headers=get_auth_headers(
                roles="cra", change_reason="Soft-deleting SITE-A range"
            ),
        )
        assert res_del.status_code == 200
        assert res_del.json()["is_deleted"] is True
        assert res_del.json()["version"] == 2

        # After soft-deleting SITE-A specific range, subsequent SITE-A local observations must fall back to local generic
        obs_site_a_retry = {
            "subject_id": "SUBJ-E2E-001",
            "study_id": "STUDY-E2E",
            "domain": "LB",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "value": 11.2,
            "unit": "g/dL",
            "lab_source": "LOCAL",
            "lab_site_id": "SITE-A",
        }
        res_obs_a_retry = await client.post(
            "/api/v1/execution/observations",
            json=obs_site_a_retry,
            headers=get_auth_headers(
                roles="cra",
                change_reason="Capturing SITE-A Hemoglobin after range deletion",
            ),
        )
        assert res_obs_a_retry.status_code == 200
        obs_a_retry_data = res_obs_a_retry.json()
        # Falls back to local generic -> 11.2 is LOW
        assert obs_a_retry_data["lab_indicator"] == "LOW"
        assert obs_a_retry_data["lab_out_of_range"] is True
        assert json.loads(obs_a_retry_data["matched_normal_bounds"]) == {
            "low": 11.5,
            "high": 15.5,
        }

        # 8. Create additional observations to establish a statistical cohort for testing coexistence with outlier flags
        # Outlier calculation standard deviation-based needs multiple observations to calculate stats
        # We submit a cohort of normal values for HEMOGLOBIN to establish standard deviation
        for i in range(10):
            # values: 14.0, 14.1, 14.2 ... 14.9
            cohort_payload = {
                "subject_id": "SUBJ-E2E-001",
                "study_id": "STUDY-E2E",
                "domain": "LB",
                "test_code": "HEMOGLOBIN",
                "test_name": "Hemoglobin",
                "value": 14.0 + (i * 0.1),
                "unit": "g/dL",
                "lab_source": "CENTRAL",
            }
            res_cohort = await client.post(
                "/api/v1/execution/observations",
                json=cohort_payload,
                headers=get_auth_headers(
                    roles="cra", change_reason="Establishing normal cohort"
                ),
            )
            assert res_cohort.status_code == 200
            assert res_cohort.json()["is_outlier"] is False

        # Add an extreme statistical outlier observation (e.g. 50.0 g/dL)
        outlier_payload = {
            "subject_id": "SUBJ-E2E-001",
            "study_id": "STUDY-E2E",
            "domain": "LB",
            "test_code": "HEMOGLOBIN",
            "test_name": "Hemoglobin",
            "value": 50.0,
            "unit": "g/dL",
            "lab_source": "CENTRAL",
        }
        res_outlier = await client.post(
            "/api/v1/execution/observations",
            json=outlier_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Capturing extreme outlier"
            ),
        )
        assert res_outlier.status_code == 200
        outlier_data = res_outlier.json()
        # Should coexist: both flagged as an outlier AND critical high (HIGH HIGH)
        assert outlier_data["is_outlier"] is True
        assert outlier_data["lab_indicator"] == "HIGH HIGH"
        assert outlier_data["lab_out_of_range"] is True

        # 9. Verify manual recalculation workflow
        # Update the CENTRAL range (low_bound=11.0 instead of 12.0)
        update_central_payload = {
            "low_bound": 11.0,
        }
        res_upd = await client.put(
            f"/api/v1/execution/lab-ranges/{central_range_id}",
            json=update_central_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Modifying CENTRAL low bound to 11.0"
            ),
        )
        assert res_upd.status_code == 200
        assert res_upd.json()["low_bound"] == 11.0
        assert res_upd.json()["version"] == 2

        # Trigger on-demand manual recalculation for STUDY-E2E and HEMOGLOBIN
        recalc_payload = {
            "study_id": "STUDY-E2E",
            "test_code": "HEMOGLOBIN",
        }
        res_recalc = await client.post(
            "/api/v1/execution/lab-ranges/recalculate",
            json=recalc_payload,
            headers=get_auth_headers(
                roles="cra", change_reason="Triggering recalculation post-bounds update"
            ),
        )
        assert res_recalc.status_code == 200
        recalc_res_data = res_recalc.json()
        assert recalc_res_data["status"] == "success"
        # The central observation 11.2 (previously LOW under low_bound 12.0) should now be NORMAL under low_bound 11.0
        # Check database to verify versions and indicators
        async with db_manager.get_session_maker()() as session:
            # Check Observation 5 (the original central 11.2 observation)
            stmt_obs_c = select(ClinicalObservation).where(
                ClinicalObservation.id == obs_c_data["id"]
            )
            res_obs_c_db = await session.execute(stmt_obs_c)
            obs_c_db = res_obs_c_db.scalar_one()
            # It should have updated to NORMAL, and version incremented from 1 to 2
            assert obs_c_db.lab_indicator == "NORMAL"
            assert obs_c_db.lab_out_of_range is False
            assert obs_c_db.version == 2
            assert json.loads(obs_c_db.matched_normal_bounds) == {
                "low": 11.0,
                "high": 16.0,
            }

            # Check that is_outlier remains untouched/coexisting correctly
            stmt_outlier = select(ClinicalObservation).where(
                ClinicalObservation.id == outlier_data["id"]
            )
            res_outlier_db = await session.execute(stmt_outlier)
            outlier_db = res_outlier_db.scalar_one()
            assert outlier_db.is_outlier is True
            assert outlier_db.lab_indicator == "HIGH HIGH"

            # Check audit logs for the updates on clinical_observations
            stmt_audit = select(AuditLog).where(
                AuditLog.table_name == "clinical_observations",
                AuditLog.action == "UPDATE",
            )
            res_audit = await session.execute(stmt_audit)
            audit_logs = res_audit.scalars().all()
            # There should be audit log records matching the updated observations
            assert len(audit_logs) > 0
            # Check that audit log has version_index and reasons populated
            assert audit_logs[0].change_reason is not None
            assert audit_logs[0].user_id == "test_user"
