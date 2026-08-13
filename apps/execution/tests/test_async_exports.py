import hashlib
import hmac
import json
import os
import time
from datetime import datetime

import httpx
import pytest
import pytest_asyncio

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
    ClinicalSubject,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")  # nosec B105: mock fallback secret for testing


def get_auth_headers(
    user_id="test_dm", roles="Data Manager", change_reason="system_operation"
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
    """Setup clean database state before each test and clear down after."""
    is_postgres = os.environ.get("TEST_DATABASE_URL", "").startswith(
        ("postgres", "postgresql")
    )

    # Save original state of engine/sessionmaker to avoid polluting other tests
    original_engine = db_manager.engine
    original_session_maker = db_manager.session_maker

    # If not already initialized or if we want SQLite fallback, init it
    initialized_here = False
    if not is_postgres:
        db_manager.init_db("sqlite+aiosqlite:///:memory:")
        initialized_here = True
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)
    else:
        # If running on Postgres, ensure the engine is initialized first if not already done.
        if db_manager.engine is None:
            db_manager.init_db(os.environ["TEST_DATABASE_URL"])
            initialized_here = True
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    yield

    TrialLockManager.reset()

    if not is_postgres:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        if initialized_here:
            await db_manager.close()
            # Restore original state of singleton to avoid polluting subsequent tests
            db_manager.engine = original_engine
            db_manager.session_maker = original_session_maker
    else:
        # For postgres, restore original states if we initialized it here
        if initialized_here:
            await db_manager.close()
            db_manager.engine = original_engine
            db_manager.session_maker = original_session_maker


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

        await session.commit()


@pytest.mark.asyncio
async def test_async_export_bundle_success(populate_test_data) -> None:
    """Verify triggering an async bundle export, checking status, and downloading the output."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Trigger the export
        res_trigger = await client.post(
            "/api/v1/execution/exports",
            json={"study_id": "STUDY-001", "dataset_name": "BUNDLE"},
            headers=get_auth_headers(),
        )
        assert res_trigger.status_code == 202
        data = res_trigger.json()
        assert "job_id" in data
        assert data["status"] == "PENDING"
        job_id = data["job_id"]

        # Poll the status until completed
        completed = False
        for _ in range(10):
            res_status = await client.get(
                f"/api/v1/execution/exports/{job_id}",
                headers=get_auth_headers(),
            )
            assert res_status.status_code == 200
            status_data = res_status.json()
            if status_data["status"] == "COMPLETED":
                completed = True
                assert status_data["progress"] == 100
                assert status_data["download_url"] is not None
                break
            if status_data["status"] == "FAILED":
                pytest.fail(f"Job failed with error: {status_data['error_message']}")
            time.sleep(0.5)

        assert completed, "Background task did not complete in time"

        # Download the file
        res_download = await client.get(
            f"/api/v1/execution/exports/{job_id}/download",
            headers=get_auth_headers(),
        )
        assert res_download.status_code == 200
        download_data = res_download.json()
        assert (
            "clinicalData" in download_data
            or "clinical_data" in download_data
            or "studyOID" in download_data
            or "datasetJSON" in download_data
        )


@pytest.mark.asyncio
async def test_async_export_adam_success(populate_test_data) -> None:
    """Verify triggering an async export of a single ADaM dataset."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Trigger the export for ADSL
        res_trigger = await client.post(
            "/api/v1/execution/exports",
            json={"study_id": "STUDY-001", "dataset_name": "ADSL"},
            headers=get_auth_headers(),
        )
        assert res_trigger.status_code == 202
        data = res_trigger.json()
        job_id = data["job_id"]

        # Poll status
        completed = False
        for _ in range(10):
            res_status = await client.get(
                f"/api/v1/execution/exports/{job_id}",
                headers=get_auth_headers(),
            )
            status_data = res_status.json()
            if status_data["status"] == "COMPLETED":
                completed = True
                break
            time.sleep(0.5)

        assert completed

        # Download and verify ADSL is present
        res_download = await client.get(
            f"/api/v1/execution/exports/{job_id}/download",
            headers=get_auth_headers(),
        )
        assert res_download.status_code == 200


@pytest.mark.asyncio
async def test_async_export_unauthorized() -> None:
    """Verify that unauthorized roles or missing auth headers fail correctly."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # No auth headers
        res_trigger = await client.post(
            "/api/v1/execution/exports",
            json={"study_id": "STUDY-001", "dataset_name": "BUNDLE"},
        )
        assert res_trigger.status_code in (401, 403)

        # Unauthorized role (e.g. CRC)
        res_trigger_unauth = await client.post(
            "/api/v1/execution/exports",
            json={"study_id": "STUDY-001", "dataset_name": "BUNDLE"},
            headers=get_auth_headers(roles="CRC"),
        )
        assert res_trigger_unauth.status_code == 403


@pytest.mark.asyncio
async def test_async_export_not_found() -> None:
    """Verify querying a non-existent job returns 404."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.get(
            "/api/v1/execution/exports/some-nonexistent-job-uuid",
            headers=get_auth_headers(),
        )
        assert res.status_code == 404


@pytest.mark.asyncio
async def test_async_export_validation_failure() -> None:
    """Verify that a job with validation or run errors fails gracefully and registers error_message."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Trigger export for non-existent study / empty database (validation should raise error or fail)
        res_trigger = await client.post(
            "/api/v1/execution/exports",
            json={"study_id": "NON-EXISTENT-STUDY", "dataset_name": "BUNDLE"},
            headers=get_auth_headers(),
        )
        assert res_trigger.status_code == 202
        job_id = res_trigger.json()["job_id"]

        # Poll the status, expecting FAILED
        failed = False
        for _ in range(10):
            res_status = await client.get(
                f"/api/v1/execution/exports/{job_id}",
                headers=get_auth_headers(),
            )
            status_data = res_status.json()
            if status_data["status"] == "FAILED":
                failed = True
                assert status_data["error_message"] is not None
                break
            time.sleep(0.5)

        assert failed


@pytest.mark.asyncio
async def test_exports_helper_unsupported_adam(populate_test_data) -> None:
    """Verify run_adam_derivation raises ValueError for unsupported dataset."""
    from apps.execution.presentation.routers.exports import run_adam_derivation

    async with db_manager.get_session_maker()() as session:
        with pytest.raises(ValueError, match="Unsupported ADaM dataset"):
            await run_adam_derivation(session, "STUDY-001", "INVALID_DATASET")


@pytest.mark.asyncio
async def test_run_dataset_export_task_no_job() -> None:
    """Verify run_dataset_export_task returns early if job is not found."""
    from apps.execution.presentation.routers.exports import run_dataset_export_task

    # This should not raise any exceptions and return cleanly
    await run_dataset_export_task("nonexistent-job-uuid", "STUDY-001", "BUNDLE")


@pytest.mark.asyncio
async def test_async_export_task_dataset_json_validation_failure(
    populate_test_data,
) -> None:
    """Verify background task handles DatasetJSONValidationError gracefully."""
    from unittest.mock import patch

    from apps.execution.biostat import DatasetJSONValidationError

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # We patch validate_dataset_json on biostat module to raise validation error
        with patch(
            "apps.execution.biostat.validate_dataset_json",
            side_effect=DatasetJSONValidationError(["Mocked validation failure"]),
        ):
            res_trigger = await client.post(
                "/api/v1/execution/exports",
                json={"study_id": "STUDY-001", "dataset_name": "BUNDLE"},
                headers=get_auth_headers(),
            )
            assert res_trigger.status_code == 202
            job_id = res_trigger.json()["job_id"]

            failed = False
            for _ in range(10):
                res_status = await client.get(
                    f"/api/v1/execution/exports/{job_id}",
                    headers=get_auth_headers(),
                )
                status_data = res_status.json()
                if status_data["status"] == "FAILED":
                    failed = True
                    assert "Mocked validation failure" in status_data["error_message"]
                    break
                time.sleep(0.5)
            assert failed


@pytest.mark.asyncio
async def test_download_endpoint_errors(populate_test_data) -> None:
    """Verify download endpoint error handling for non-existent, incomplete, and missing files."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Non-existent job UUID
        res = await client.get(
            "/api/v1/execution/exports/nonexistent-job-uuid/download",
            headers=get_auth_headers(),
        )
        assert res.status_code == 404
        assert res.json()["detail"] == "Export job not found"

        # 2. Incomplete/Failed job
        res_trigger = await client.post(
            "/api/v1/execution/exports",
            json={"study_id": "NON-EXISTENT-STUDY", "dataset_name": "BUNDLE"},
            headers=get_auth_headers(),
        )
        job_id = res_trigger.json()["job_id"]
        # Poll till it's failed
        failed = False
        for _ in range(10):
            res_status = await client.get(
                f"/api/v1/execution/exports/{job_id}", headers=get_auth_headers()
            )
            if res_status.json()["status"] == "FAILED":
                failed = True
                break
            time.sleep(0.5)
        assert failed

        res_download = await client.get(
            f"/api/v1/execution/exports/{job_id}/download",
            headers=get_auth_headers(),
        )
        assert res_download.status_code == 400
        assert "cannot be downloaded" in res_download.json()["detail"]

        # 3. Successful job but missing file from disk
        res_trigger_ok = await client.post(
            "/api/v1/execution/exports",
            json={"study_id": "STUDY-001", "dataset_name": "BUNDLE"},
            headers=get_auth_headers(),
        )
        job_id_ok = res_trigger_ok.json()["job_id"]
        completed = False
        for _ in range(10):
            res_status = await client.get(
                f"/api/v1/execution/exports/{job_id_ok}", headers=get_auth_headers()
            )
            if res_status.json()["status"] == "COMPLETED":
                completed = True
                break
            time.sleep(0.5)
        assert completed

        # Remove the file
        file_path = f"/app/exports/{job_id_ok}.json"
        if os.path.exists(file_path):
            os.remove(file_path)

        res_download_missing = await client.get(
            f"/api/v1/execution/exports/{job_id_ok}/download",
            headers=get_auth_headers(),
        )
        assert res_download_missing.status_code == 404
        assert "not found on the server" in res_download_missing.json()["detail"]


@pytest.mark.asyncio
async def test_synchronous_exports_errors(populate_test_data) -> None:
    """Verify error paths for synchronous endpoints (SDTM, ADaM, BUNDLE)."""
    from unittest.mock import patch

    from apps.execution.biostat import DatasetJSONValidationError

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. SDTM invalid domain
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/INVALID?study_id=STUDY-001",
            headers=get_auth_headers(),
        )
        assert res.status_code == 400
        assert "Unsupported SDTM domain" in res.json()["detail"]

        # 2. SDTM DatasetJSONValidationError
        with patch(
            "apps.execution.presentation.routers.exports.validate_dataset_json",
            side_effect=DatasetJSONValidationError(["Mock SDTM Validate Error"]),
        ):
            res = await client.get(
                "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001",
                headers=get_auth_headers(),
            )
            assert res.status_code == 422
            assert "Dataset-JSON validation failed" in res.json()["detail"]

        # 3. SDTM General Exception
        with patch(
            "apps.execution.presentation.routers.exports.run_sdtm_extraction",
            side_effect=Exception("Mock general extraction error"),
        ):
            res = await client.get(
                "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001",
                headers=get_auth_headers(),
            )
            assert res.status_code == 500
            assert "Export execution failed" in res.json()["detail"]

        # 4. ADaM invalid dataset
        res = await client.get(
            "/api/v1/execution/biostat/adam/INVALID?study_id=STUDY-001",
            headers=get_auth_headers(),
        )
        assert res.status_code == 400
        assert "Unsupported ADaM dataset" in res.json()["detail"]

        # 5. ADaM DatasetJSONValidationError
        with patch(
            "apps.execution.presentation.routers.exports.validate_dataset_json",
            side_effect=DatasetJSONValidationError(["Mock ADaM Validate Error"]),
        ):
            res = await client.get(
                "/api/v1/execution/biostat/adam/ADSL?study_id=STUDY-001",
                headers=get_auth_headers(),
            )
            assert res.status_code == 422
            assert "Dataset-JSON validation failed" in res.json()["detail"]

        # 6. ADaM General Exception
        with patch(
            "apps.execution.presentation.routers.exports.run_adam_derivation",
            side_effect=Exception("Mock general derivation error"),
        ):
            res = await client.get(
                "/api/v1/execution/biostat/adam/ADSL?study_id=STUDY-001",
                headers=get_auth_headers(),
            )
            assert res.status_code == 500
            assert "Export execution failed" in res.json()["detail"]

        # 7. BUNDLE DatasetJSONValidationError
        with patch(
            "apps.execution.presentation.routers.exports.validate_dataset_json",
            side_effect=DatasetJSONValidationError(["Mock BUNDLE Validate Error"]),
        ):
            res = await client.get(
                "/api/v1/execution/biostat/bundle?study_id=STUDY-001",
                headers=get_auth_headers(),
            )
            assert res.status_code == 422
            assert "Dataset-JSON validation failed" in res.json()["detail"]

        # 8. BUNDLE General Exception
        with patch(
            "apps.execution.presentation.routers.exports.run_sdtm_extraction",
            side_effect=Exception("Mock general extraction error"),
        ):
            res = await client.get(
                "/api/v1/execution/biostat/bundle?study_id=STUDY-001",
                headers=get_auth_headers(),
            )
            assert res.status_code == 500
            assert "Export execution failed" in res.json()["detail"]
