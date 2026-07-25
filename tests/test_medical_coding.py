import asyncio
import io
import os
import time
import zipfile
from typing import AsyncGenerator

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select, text
from sqlalchemy.exc import IntegrityError

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalCodingAssignment,
    DictionaryImportJob,
    MedDRAHierarchy,
    MedDRATerm,
    WHODrugRecord,
)
from apps.execution.main import app as exec_app
from apps.execution.trial_lock import TrialLockManager
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db() -> AsyncGenerator[None, None]:
    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()

    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        ),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    TrialLockManager.reset()


@pytest.mark.asyncio
async def test_meddra_term_unique_constraint() -> None:
    """Verify that unique constraints prevent duplicate terminology records for identical version, code, and level."""
    # First insert
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            term1 = MedDRATerm(
                dictionary_version="26.0",
                code="10019211",
                term_name="Headache",
                level="LLT",
            )
            session.add(term1)

    # Second insert with identical version, code, and level should fail
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            term2 = MedDRATerm(
                dictionary_version="26.0",
                code="10019211",
                term_name="Cephalea",
                level="LLT",
            )
            session.add(term2)
            with pytest.raises(IntegrityError):
                await session.commit()


@pytest.mark.asyncio
async def test_whodrug_record_unique_constraint() -> None:
    """Verify that unique constraints prevent duplicate WHODrug record inserts for identical version and drug_code."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            drug1 = WHODrugRecord(
                dictionary_version="2024-03",
                drug_code="00010101001",
                preferred_name="ASPIRIN",
                drug_name="ASPIRIN TABLET",
            )
            session.add(drug1)

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            drug2 = WHODrugRecord(
                dictionary_version="2024-03",
                drug_code="00010101001",
                preferred_name="ASPIRIN PAIN RELIEF",
                drug_name="ASPIRIN FORTE",
            )
            session.add(drug2)
            with pytest.raises(IntegrityError):
                await session.commit()


@pytest.mark.asyncio
async def test_lookup_and_indexes() -> None:
    """Assert that lookup-oriented index queries function correctly on terminology tables."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            term1 = MedDRATerm(
                dictionary_version="26.0",
                code="10019205",
                term_name="Nervous system disorders",
                level="SOC",
            )
            session.add(term1)

            hierarchy1 = MedDRAHierarchy(
                dictionary_version="26.0",
                llt_code="10019211",
                pt_code="10019211",
                hlt_code="10019231",
                hlgt_code="10029214",
                soc_code="10029205",
                primary_soc_flag="Y",
            )
            session.add(hierarchy1)

    async with db_manager.get_session_maker()() as session:
        # Test term query
        term_stmt = select(MedDRATerm).where(
            MedDRATerm.dictionary_version == "26.0",
            MedDRATerm.code == "10019205",
            MedDRATerm.level == "SOC",
        )
        term_res = await session.execute(term_stmt)
        queried_term = term_res.scalar_one_or_none()
        assert queried_term is not None
        assert queried_term.term_name == "Nervous system disorders"

        # Test hierarchy query
        hier_stmt = select(MedDRAHierarchy).where(
            MedDRAHierarchy.dictionary_version == "26.0",
            MedDRAHierarchy.pt_code == "10019211",
        )
        hier_res = await session.execute(hier_stmt)
        queried_hier = hier_res.scalar_one_or_none()
        assert queried_hier is not None
        assert queried_hier.soc_code == "10029205"


@pytest.mark.asyncio
async def test_audit_trigger_logging_on_coding_workflow() -> None:
    """Verify that mutations on clinical coding models write audit trail records correctly."""
    # 1. INSERT audit log test
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            assignment = ClinicalCodingAssignment(
                verbatim_text="headache symptom",
                source_field="AE.AETERM",
                observation_id="obs_123",
                dictionary_type="MEDDRA",
                dictionary_version="26.0",
                coded_code="10019211",
                coded_term="Headache",
                status="CODED",
            )
            session.add(assignment)

    # Verify INSERT audit log exists
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            res = await session.execute(
                select(AuditLog).where(
                    AuditLog.table_name == "clinical_coding_assignments"
                )
            )
            logs = res.scalars().all()
            insert_logs = [log for log in logs if log.action == "INSERT"]
            assert len(insert_logs) >= 1
            assert any(
                lg.new_values["verbatim_text"] == "headache symptom"
                for lg in insert_logs
            )
            assert any(lg.new_values["coded_code"] == "10019211" for lg in insert_logs)

    # 2. UPDATE audit log test
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Query and update status
            stmt = select(ClinicalCodingAssignment).where(
                ClinicalCodingAssignment.observation_id == "obs_123"
            )
            res = await session.execute(stmt)
            obj = res.scalar_one()
            obj.status = "RECODING_REQUIRED"

    # Verify UPDATE audit log is recorded
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            res = await session.execute(
                select(AuditLog).where(
                    AuditLog.table_name == "clinical_coding_assignments"
                )
            )
            logs = res.scalars().all()
            update_logs = [log for log in logs if log.action == "UPDATE"]
            assert len(update_logs) >= 1
            assert any(lg.old_values["status"] == "CODED" for lg in update_logs)
            assert any(
                lg.new_values["status"] == "RECODING_REQUIRED" for lg in update_logs
            )

    # 3. Prevent hard delete, but allow soft delete
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Hard delete should raise exception from trigger/session handler
            with pytest.raises(
                Exception, match="Hard deletions are strictly forbidden"
            ):
                await session.execute(
                    text(
                        "DELETE FROM clinical_coding_assignments WHERE observation_id = 'obs_123';"
                    )
                )

    # Soft delete instead
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(ClinicalCodingAssignment).where(
                ClinicalCodingAssignment.observation_id == "obs_123"
            )
            res = await session.execute(stmt)
            obj = res.scalar_one()
            obj.is_deleted = True

    # Verify soft delete maps to 'DELETE' action in AuditLog
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            res = await session.execute(
                select(AuditLog).where(
                    AuditLog.table_name == "clinical_coding_assignments"
                )
            )
            logs = res.scalars().all()
            delete_logs = [log for log in logs if log.action == "DELETE"]
            assert len(delete_logs) >= 1
            assert any(lg.old_values["is_deleted"] == 0 for lg in delete_logs)
            assert any(lg.new_values["is_deleted"] == 1 for lg in delete_logs)


@pytest.mark.asyncio
async def test_dictionary_import_job_lifecycle() -> None:
    """Verify that import job lifecycle can be persisted, tracked, and audited."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            job = DictionaryImportJob(
                dictionary_type="WHODRUG",
                dictionary_version="2024-03",
                status="PENDING",
            )
            session.add(job)

    # Update job state
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(DictionaryImportJob).where(
                DictionaryImportJob.dictionary_version == "2024-03"
            )
            res = await session.execute(stmt)
            job_obj = res.scalar_one()
            job_obj.status = "COMPLETED"
            job_obj.progress_percentage = 100
            job_obj.records_imported = 45000

    # Assert persistence and audit capture
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(DictionaryImportJob).where(
                DictionaryImportJob.dictionary_version == "2024-03"
            )
            res = await session.execute(stmt)
            job_obj = res.scalar_one()
            assert job_obj.status == "COMPLETED"
            assert job_obj.records_imported == 45000

            res_logs = await session.execute(
                select(AuditLog).where(AuditLog.table_name == "dictionary_import_jobs")
            )
            logs = res_logs.scalars().all()
            assert len(logs) > 0
            # Ensure audit is tracking changes on dictionary_import_jobs
            assert any(lg.action == "INSERT" for lg in logs)
            assert any(lg.action == "UPDATE" for lg in logs)


# ==================================================
# NEW: Audited Dictionary Import Integration Tests
# ==================================================


def get_import_auth_headers(
    roles: str = "TERMINOLOGY_MANAGER",
    change_reason: str = "Importing standard dictionary",
) -> dict:
    """Helper to generate valid gateway V2 signed headers for dictionary import tests."""
    timestamp = str(time.time())
    user_id = "test_terminologist"
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    return headers


@pytest.mark.asyncio
async def test_meddra_import_happy_path() -> None:
    """Verify that an authorized user can successfully import a valid MedDRA zip distribution."""
    import httpx
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=exec_app), base_url="http://test") as client:
        # 1. Create a valid MedDRA zip archive in memory
        zip_buffer = io.BytesIO()
        with zipfile.ZipFile(zip_buffer, "a", zipfile.ZIP_DEFLATED, False) as zip_file:
            zip_file.writestr("llt.asc", "10019211$Headache$10019211$$$$$Y$\n")
            zip_file.writestr("pt.asc", "10019211$Headache$10019211$$$$$\n")
        zip_buffer.seek(0)

        # 2. Upload zip archive using TERMINOLOGY_MANAGER role
        resp = await client.post(
            "/api/v1/dictionaries/import",
            data={
                "dictionary_type": "MEDDRA",
                "version": "26.0",
                "parse_multilingual": "true",
            },
            files={"files": ("meddra_26_0.zip", zip_buffer, "application/zip")},
            headers=get_import_auth_headers("TERMINOLOGY_MANAGER"),
        )
        assert resp.status_code == 202
        job_info = resp.json()
        assert job_info["job_id"] is not None
        assert job_info["status"] == "PENDING"

        # 3. Poll GET /api/v1/dictionaries/jobs/{job_id} until completed
        job_id = job_info["job_id"]
        completed = False
        for _ in range(50):
            status_resp = await client.get(
                f"/api/v1/dictionaries/jobs/{job_id}",
                headers=get_import_auth_headers("TERMINOLOGY_MANAGER"),
            )
            assert status_resp.status_code == 200
            status_info = status_resp.json()
            if status_info["status"] == "COMPLETED":
                completed = True
                assert status_info["records_imported"] > 0
                assert status_info["errors_encountered"] == 0
                break
            elif status_info["status"] == "FAILED":
                pytest.fail(f"Job failed unexpectedly: {status_info}")
            await asyncio.sleep(0.1)

        assert completed, "Dictionary import job did not complete in time."

        # 4. Assert records were persisted in the database
        async with db_manager.get_session_maker()() as session:
            terms_res = await session.execute(select(MedDRATerm))
            terms = terms_res.scalars().all()
            assert len(terms) >= 2
            assert any(t.code == "10019211" and t.level == "LLT" for t in terms)
            assert any(t.code == "10019211" and t.level == "PT" for t in terms)


@pytest.mark.asyncio
async def test_whodrug_import_happy_path() -> None:
    """Verify that an authorized user can successfully import a valid WHODrug zip distribution."""
    import httpx
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=exec_app), base_url="http://test") as client:
        # 1. Create a valid WHODrug zip archive in memory
        zip_buffer = io.BytesIO()
        with zipfile.ZipFile(zip_buffer, "a", zipfile.ZIP_DEFLATED, False) as zip_file:
            zip_file.writestr(
                "DD.txt", "00010101001ASPIRIN                        ASPIRIN TABLET\n"
            )
            zip_file.writestr("ING.txt", "0000000001ACETYLSALICYLIC ACID\n")
        zip_buffer.seek(0)

        # 2. Upload zip archive using SYSTEM_ADMIN role
        resp = await client.post(
            "/api/v1/dictionaries/import",
            data={
                "dictionary_type": "WHODRUG",
                "version": "2024-03",
                "parse_multilingual": "false",
            },
            files={"files": ("whodrug_2024_03.zip", zip_buffer, "application/zip")},
            headers=get_import_auth_headers("SYSTEM_ADMIN"),
        )
        assert resp.status_code == 202
        job_info = resp.json()
        job_id = job_info["job_id"]

        # 3. Poll job status
        completed = False
        for _ in range(50):
            status_resp = await client.get(
                f"/api/v1/dictionaries/jobs/{job_id}",
                headers=get_import_auth_headers("SYSTEM_ADMIN"),
            )
            assert status_resp.status_code == 200
            status_info = status_resp.json()
            if status_info["status"] == "COMPLETED":
                completed = True
                assert status_info["records_imported"] == 2
                assert status_info["errors_encountered"] == 0
                break
            elif status_info["status"] == "FAILED":
                pytest.fail(f"Job failed unexpectedly: {status_info}")
            await asyncio.sleep(0.1)

        assert completed

        # 4. Assert records are persisted
        async with db_manager.get_session_maker()() as session:
            records_res = await session.execute(select(WHODrugRecord))
            records = records_res.scalars().all()
            assert len(records) == 1
            assert records[0].drug_code == "00010101001"
            assert records[0].preferred_name == "ASPIRIN"


@pytest.mark.asyncio
async def test_import_unauthorized_roles_forbidden() -> None:
    """Verify that unauthorized roles (like Data Manager or CRA) cannot import dictionaries."""
    import httpx
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=exec_app), base_url="http://test") as client:
        zip_buffer = io.BytesIO()
        with zipfile.ZipFile(zip_buffer, "a", zipfile.ZIP_DEFLATED, False) as zip_file:
            zip_file.writestr("llt.asc", "10019211$Headache$10019211$$$$$Y$\n")
        zip_buffer.seek(0)

        # Test with unauthorized "Data Manager" role
        resp = await client.post(
            "/api/v1/dictionaries/import",
            data={"dictionary_type": "MEDDRA", "version": "26.0"},
            files={"files": ("meddra.zip", zip_buffer, "application/zip")},
            headers=get_import_auth_headers("Data Manager"),
        )
        assert resp.status_code == 403
        assert "not authorized" in resp.json()["detail"]


@pytest.mark.asyncio
async def test_import_invalid_layout_rejected() -> None:
    """Verify that archives with invalid file layout are rejected synchronously."""
    import httpx
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=exec_app), base_url="http://test") as client:
        # Empty zip
        zip_buffer = io.BytesIO()
        with zipfile.ZipFile(zip_buffer, "a", zipfile.ZIP_DEFLATED, False):
            pass
        zip_buffer.seek(0)

        resp = await client.post(
            "/api/v1/dictionaries/import",
            data={"dictionary_type": "MEDDRA", "version": "26.0"},
            files={"files": ("invalid_layout.zip", zip_buffer, "application/zip")},
            headers=get_import_auth_headers("TERMINOLOGY_MANAGER"),
        )
        assert resp.status_code == 400
        assert "Invalid MedDRA archive layout" in resp.json()["detail"]


@pytest.mark.asyncio
async def test_import_unsupported_dictionary_rejected() -> None:
    """Verify that unsupported dictionary types are rejected synchronously."""
    import httpx
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=exec_app), base_url="http://test") as client:
        zip_buffer = io.BytesIO()
        with zipfile.ZipFile(zip_buffer, "a", zipfile.ZIP_DEFLATED, False) as zip_file:
            zip_file.writestr("test.txt", "some content")
        zip_buffer.seek(0)

        resp = await client.post(
            "/api/v1/dictionaries/import",
            data={"dictionary_type": "LOINC", "version": "2.74"},
            files={"files": ("loinc.zip", zip_buffer, "application/zip")},
            headers=get_import_auth_headers("TERMINOLOGY_MANAGER"),
        )
        assert resp.status_code == 400
        assert "Import not supported for dictionary type" in resp.json()["detail"]


@pytest.mark.asyncio
async def test_import_failure_rollback_and_failed_state() -> None:
    """Verify that parsing/persistence failure rolls back any records and marks job as FAILED."""
    import httpx
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=exec_app), base_url="http://test") as client:
        # Create zip containing invalid data (non-8-digit llt_code)
        zip_buffer = io.BytesIO()
        with zipfile.ZipFile(zip_buffer, "a", zipfile.ZIP_DEFLATED, False) as zip_file:
            # First term valid, second term invalid
            zip_file.writestr(
                "llt.asc",
                "10019211$Headache$10019211$$$$$Y$\n123$InvalidMigraine$10019211$$$$$Y$\n",
            )
        zip_buffer.seek(0)

        resp = await client.post(
            "/api/v1/dictionaries/import",
            data={"dictionary_type": "MEDDRA", "version": "26.0"},
            files={"files": ("bad_meddra.zip", zip_buffer, "application/zip")},
            headers=get_import_auth_headers("TERMINOLOGY_MANAGER"),
        )
        assert resp.status_code == 202
        job_id = resp.json()["job_id"]

        # Wait for job to fail
        failed = False
        for _ in range(50):
            status_resp = await client.get(
                f"/api/v1/dictionaries/jobs/{job_id}",
                headers=get_import_auth_headers("TERMINOLOGY_MANAGER"),
            )
            assert status_resp.status_code == 200
            status_info = status_resp.json()
            if status_info["status"] == "FAILED":
                failed = True
                assert status_info["records_imported"] == 0
                assert status_info["errors_encountered"] == 1
                break
            elif status_info["status"] == "COMPLETED":
                pytest.fail("Job completed but was expected to fail.")
            await asyncio.sleep(0.1)

        assert failed

        # Verify database: NO MedDRATerms should have been imported due to rollback!
        async with db_manager.get_session_maker()() as session:
            terms_res = await session.execute(select(MedDRATerm))
            terms = terms_res.scalars().all()
            assert len(terms) == 0, (
                f"Expected 0 terms imported due to transaction rollback, got {len(terms)}."
            )

