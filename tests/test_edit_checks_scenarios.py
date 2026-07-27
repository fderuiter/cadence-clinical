import asyncio
from typing import AsyncGenerator

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalObservation,
    PendingPredecessorCheck,
)
from apps.execution.main import app
from tests.test_clinical_queries import get_v2_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> AsyncGenerator[None, None]:
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_scenario_skip_logic_and_cascading_nullification() -> None:
    """
    Scenario verifying dynamic skip logic and cascading dependent nullification.
    @req:PRD-EDC-003
    @req:PRD-EDC-004
    """
    headers = get_v2_auth_headers(
        user_id="dm_user_01",
        roles="Data Manager",
        change_reason="Submit Pregnancy and Due Date data",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Setup a subject and visit
        sub_resp = await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-PREG", "study_id": "STUDY-PREG"},
            headers=headers,
        )
        assert sub_resp.status_code == 200

        visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-PREG",
                "visit_name": "BASELINE",
                "study_id": "STUDY-PREG",
            },
            headers=headers,
        )
        assert visit_resp.status_code == 200
        visit_id = visit_resp.json()["id"]

        # Submit DUE_DATE child field first (as if PREG_STATUS was previously YES)
        child_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-PREG",
                "study_id": "STUDY-PREG",
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "DUE_DATE",
                "test_name": "Pregnancy Due Date",
                "value_string": "2026-12-01",
            },
            headers=headers,
        )
        assert child_resp.status_code == 200
        due_date_id = child_resp.json()["id"]

        # 2. Submit parent PREG_STATUS = "NO" (causes child to become irrelevant)
        parent_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-PREG",
                "study_id": "STUDY-PREG",
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "PREG_STATUS",
                "test_name": "Pregnancy Status",
                "value_string": "NO",
            },
            headers=headers,
        )
        assert parent_resp.status_code == 200

        # Wait briefly for any background processing
        await asyncio.sleep(0.1)

        # 3. Verify that child field DUE_DATE is completely nullified in the database
        async with db_manager.get_session_maker()() as session:
            stmt = select(ClinicalObservation).where(
                ClinicalObservation.id == due_date_id
            )
            res = await session.execute(stmt)
            updated_child = res.scalars().first()
            assert updated_child is not None
            assert updated_child.value is None
            assert updated_child.value_string is None

            # 4. Verify that the audit log for the child field change records the exact system reason
            stmt_audit = select(AuditLog).where(
                AuditLog.table_name == "clinical_observations",
                AuditLog.record_id == due_date_id,
                AuditLog.action == "UPDATE",
            )
            res_audit = await session.execute(stmt_audit)
            audits = res_audit.scalars().all()
            assert len(audits) >= 1

            # Find the audit with the system-generated reason
            purge_audit = next(
                (
                    a
                    for a in audits
                    if a.change_reason
                    == "System-initiated purge of inactive child variable due to parent value mutation"
                ),
                None,
            )
            assert purge_audit is not None
            assert purge_audit.old_values.get("value_string") == "2026-12-01"
            assert purge_audit.new_values.get("value_string") is None


@pytest.mark.asyncio
async def test_scenario_cross_form_edit_checks_and_auto_resolve() -> None:
    """
    Scenario verifying cross-form edit check execution and query auto-resolution with change reason tracking.
    @req:PRD-QRY-003
    """
    headers = get_v2_auth_headers(
        user_id="dm_user_02",
        roles="Data Manager",
        change_reason="Register Subject Screening Dates",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create Subject
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-CF-S", "study_id": "STUDY-CF-S"},
            headers=headers,
        )

        # Create Visit
        v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-CF-S",
                "visit_name": "SCREENING",
                "study_id": "STUDY-CF-S",
            },
            headers=headers,
        )
        visit_id = v_resp.json()["id"]

        # 1. Informed Consent Date
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-CF-S",
                "study_id": "STUDY-CF-S",
                "visit_id": visit_id,
                "domain": "DS",
                "test_code": "DSSTDTC",
                "test_name": "Informed Consent Date",
                "value_string": "2026-08-01",
            },
            headers=headers,
        )

        # 2. Adverse Event Onset Date (before informed consent!)
        ae_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-CF-S",
                "study_id": "STUDY-CF-S",
                "visit_id": visit_id,
                "domain": "AE",
                "test_code": "AESTDTC",
                "test_name": "AE Onset Date",
                "value_string": "2026-07-15",
            },
            headers=headers,
        )
        assert ae_resp.status_code == 200

        # Wait briefly for background task
        await asyncio.sleep(0.1)

        # Verify system query was opened
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        queries = queries_resp.json()
        cf_queries = [q for q in queries if q["rule_id"] == "AE_CONSENT_TEMPORAL_CHECK"]
        assert len(cf_queries) == 1
        query_id = cf_queries[0]["id"]
        assert cf_queries[0]["status"] == "OPEN"

        # 3. Submit corrected AE onset date (after informed consent)
        correct_headers = get_v2_auth_headers(
            user_id="dm_user_02",
            roles="Data Manager",
            change_reason="Correct AE onset date",
        )
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-CF-S",
                "study_id": "STUDY-CF-S",
                "visit_id": visit_id,
                "domain": "AE",
                "test_code": "AESTDTC",
                "test_name": "AE Onset Date",
                "value_string": "2026-08-05",
            },
            headers=correct_headers,
        )

        # Wait briefly for background task
        await asyncio.sleep(0.1)

        # Verify query is auto-resolved/closed with SYSTEM details
        get_q_resp = await client.get(
            f"/api/v1/execution/queries/{query_id}", headers=headers
        )
        query = get_q_resp.json()
        assert query["status"] == "CLOSED"
        assert query["resolver"] == "SYSTEM"
        assert "Auto-resolved" in query["response"]

        # Verify that the audit log for the query change records 'Edit Check Auto-Resolution'
        async with db_manager.get_session_maker()() as session:
            stmt_audit = select(AuditLog).where(
                AuditLog.table_name == "clinical_queries",
                AuditLog.record_id == query_id,
                AuditLog.action == "UPDATE",
            )
            res_audit = await session.execute(stmt_audit)
            audits = res_audit.scalars().all()
            assert len(audits) >= 1
            auto_res_audit = next(
                (a for a in audits if a.change_reason == "Edit Check Auto-Resolution"),
                None,
            )
            assert auto_res_audit is not None
            assert auto_res_audit.old_values.get("status") == "OPEN"
            assert auto_res_audit.new_values.get("status") == "CLOSED"


@pytest.mark.asyncio
async def test_scenario_longitudinal_predecessor_draft_and_complete() -> None:
    """
    Scenario verifying longitudinal checks with Draft / missing predecessors entering
    Pending-Predecessor queue, and resuming/evaluating correctly on Form completion.
    @req:PRD-QRY-004
    """
    headers = get_v2_auth_headers(
        user_id="dm_user_03",
        roles="Data Manager",
        change_reason="Longitudinal evaluation setup",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create Subject
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-LONG-SC", "study_id": "STUDY-LONG-SC"},
            headers=headers,
        )

        # Create Visits: BASELINE and SCREENING (predecessor)
        base_visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-LONG-SC",
                "visit_name": "BASELINE",
                "study_id": "STUDY-LONG-SC",
            },
            headers=headers,
        )
        base_visit_id = base_visit_resp.json()["id"]

        screen_visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-LONG-SC",
                "visit_name": "SCREENING",
                "study_id": "STUDY-LONG-SC",
            },
            headers=headers,
        )
        screen_visit_id = screen_visit_resp.json()["id"]

        # Create FormSubmissions for SCREENING (predecessor) as DRAFT
        screen_sub_resp = await client.post(
            "/api/v1/execution/form-submissions",
            json={
                "study_id": "STUDY-LONG-SC",
                "site_id": "SITE-A",
                "subject_id": "SUBJ-LONG-SC",
                "visit_id": screen_visit_id,
                "form_id": "VS_FORM",
            },
            headers=headers,
        )
        assert screen_sub_resp.status_code == 201
        screen_submission_id = screen_sub_resp.json()["id"]

        # Submit weight at SCREENING
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-LONG-SC",
                "study_id": "STUDY-LONG-SC",
                "visit_id": screen_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 100.0,
                "unit": "kg",
            },
            headers=headers,
        )

        # 1. Submit weight at BASELINE. Predecessor visit's FormSubmission is DRAFT, so check is deferred!
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-LONG-SC",
                "study_id": "STUDY-LONG-SC",
                "visit_id": base_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 60.0,
                "unit": "kg",
            },
            headers=headers,
        )

        # Wait briefly for background task
        await asyncio.sleep(0.1)

        # Verify no query is opened yet since predecessor is DRAFT
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        assert (
            len([q for q in queries_resp.json() if q["rule_id"] == "WEIGHT_LOSS_CHECK"])
            == 0
        )

        # Verify PendingPredecessorCheck is registered in DB
        async with db_manager.get_session_maker()() as session:
            stmt = select(PendingPredecessorCheck).where(
                PendingPredecessorCheck.subject_id == "SUBJ-LONG-SC",
                PendingPredecessorCheck.rule_id == "WEIGHT_LOSS_CHECK",
                PendingPredecessorCheck.is_deleted.is_(False),
            )
            res = await session.execute(stmt)
            pending_list = res.scalars().all()
            assert len(pending_list) == 1
            assert pending_list[0].predecessor_visit_name == "SCREENING"

        # 2. Transition SCREENING (predecessor) FormSubmission to COMPLETED
        complete_resp = await client.post(
            f"/api/v1/execution/form-submissions/{screen_submission_id}/complete",
            headers=headers,
        )
        assert complete_resp.status_code == 200

        # Wait briefly for background resume task
        await asyncio.sleep(0.1)

        # Verify that the deferred check was automatically resumed and query opened!
        queries_resp2 = await client.get("/api/v1/execution/queries", headers=headers)
        long_queries = [
            q for q in queries_resp2.json() if q["rule_id"] == "WEIGHT_LOSS_CHECK"
        ]
        assert len(long_queries) == 1
        assert long_queries[0]["status"] == "OPEN"
        assert (
            "greater than 20% compared to predecessor visit"
            in long_queries[0]["message"]
        )
