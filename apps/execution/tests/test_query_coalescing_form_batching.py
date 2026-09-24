"""
Unit and integration tests for Query Coalescing and Form-Level Task Batching.

Verifies that form submissions enqueue a single background task,
rule context resolution operates within a strict <= 3 database query budget,
pre-filtering skips non-matching rules in memory,
failing rules create system queries and passing rules auto-close existing queries,
and predecessor visit dependencies pause and resume correctly.
"""

import asyncio
from typing import Any
from unittest.mock import patch

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import event, select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
    PendingPredecessorCheck,
)
from apps.execution.edit_checks import (
    AEConsentTemporalCheckRule,
    BatchEvaluationContext,
    HighSystolicBPCheckRule,
    LabOutOfRangeCheckRule,
    WeightLossCheckRule,
    run_asynchronous_form_edit_checks,
)
from apps.execution.main import app
from apps.execution.tests.test_clinical_queries import get_v2_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> Any:
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_form_completion_enqueues_single_background_task() -> None:
    """Verify submitting a multi-field form enqueues exactly ONE background task."""
    headers = get_v2_auth_headers(
        user_id="dm_001", roles="Data Manager", change_reason="Testing"
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create Subject and Visit
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-BATCH-1", "study_id": "STUDY-BATCH"},
            headers=headers,
        )
        v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-BATCH-1",
                "visit_name": "BASELINE",
                "study_id": "STUDY-BATCH",
            },
            headers=headers,
        )
        visit_id = v_resp.json()["id"]

        # Create Form Submission
        f_resp = await client.post(
            "/api/v1/execution/form-submissions",
            json={
                "study_id": "STUDY-BATCH",
                "site_id": "SITE-001",
                "subject_id": "SUBJ-BATCH-1",
                "visit_id": visit_id,
                "form_id": "VS_FORM",
            },
            headers=headers,
        )
        sub_id = f_resp.json()["id"]

        # Add 5 observations to this form
        for i in range(5):
            await client.post(
                "/api/v1/execution/observations",
                json={
                    "subject_id": "SUBJ-BATCH-1",
                    "study_id": "STUDY-BATCH",
                    "visit_id": visit_id,
                    "domain": "VS",
                    "test_code": f"TEST_{i}",
                    "test_name": f"Test Parameter {i}",
                    "value": float(10 + i),
                    "page_id": "VS_FORM",
                },
                headers=headers,
            )

        # Intercept background_tasks.add_task in complete_form_submission
        with patch("starlette.background.BackgroundTasks.add_task") as mock_add_task:
            comp_resp = await client.post(
                f"/api/v1/execution/form-submissions/{sub_id}/complete",
                headers=headers,
            )
            assert comp_resp.status_code == 200

            # Assert exactly 1 background task was enqueued for form submission
            assert mock_add_task.call_count == 1
            call_func = mock_add_task.call_args[0][0]
            assert call_func == run_asynchronous_form_edit_checks
            assert mock_add_task.call_args[0][2] == sub_id


@pytest.mark.asyncio
async def test_context_resolution_query_budget() -> None:
    """Verify rule context resolution executes no more than 3 database queries per form execution."""
    headers = get_v2_auth_headers(
        user_id="dm_001", roles="Data Manager", change_reason="Testing"
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create Subject and Visit
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-BUDGET-1", "study_id": "STUDY-BUDGET"},
            headers=headers,
        )
        v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-BUDGET-1",
                "visit_name": "BASELINE",
                "study_id": "STUDY-BUDGET",
            },
            headers=headers,
        )
        visit_id = v_resp.json()["id"]

        # Create Form Submission
        f_resp = await client.post(
            "/api/v1/execution/form-submissions",
            json={
                "study_id": "STUDY-BUDGET",
                "site_id": "SITE-001",
                "subject_id": "SUBJ-BUDGET-1",
                "visit_id": visit_id,
                "form_id": "VS_FORM",
            },
            headers=headers,
        )
        assert f_resp.status_code in (200, 201)

        # Add 10 observations to form
        for i in range(10):
            await client.post(
                "/api/v1/execution/observations",
                json={
                    "subject_id": "SUBJ-BUDGET-1",
                    "study_id": "STUDY-BUDGET",
                    "visit_id": visit_id,
                    "domain": "VS",
                    "test_code": f"PARAM_{i}",
                    "test_name": f"Parameter {i}",
                    "value": 100.0 + i,
                    "page_id": "VS_FORM",
                },
                headers=headers,
            )

        # 2. Execute BatchEvaluationContext.load directly and measure query count
        query_count = 0

        def count_queries(conn, cursor, statement, parameters, context, executemany):
            nonlocal query_count
            query_count += 1

        engine = db_manager.engine.sync_engine
        event.listen(engine, "before_cursor_execute", count_queries)

        try:
            async with db_manager.get_session_maker()() as session:
                ctx = await BatchEvaluationContext.load(
                    session, "SUBJ-BUDGET-1", "STUDY-BUDGET"
                )

                # Query count for loading context MUST be <= 3 (1 for visits, 1 for form submissions, 1 for observations)
                assert query_count <= 3
                assert len(ctx.visits) >= 1
                assert len(ctx.submissions) >= 1
                assert len(ctx.observations) >= 10
        finally:
            event.remove(engine, "before_cursor_execute", count_queries)


@pytest.mark.asyncio
async def test_in_memory_prefiltering_rules() -> None:
    """Verify rule preconditions skip non-matching observations before running detailed evaluation."""
    obs_bp = ClinicalObservation(
        id="obs_1",
        subject_id="S1",
        study_id="ST1",
        domain="VS",
        test_code="SYSBP",
        value=120.0,
    )
    obs_weight = ClinicalObservation(
        id="obs_2",
        subject_id="S1",
        study_id="ST1",
        domain="VS",
        test_code="WEIGHT",
        value=70.0,
    )
    obs_lab = ClinicalObservation(
        id="obs_3",
        subject_id="S1",
        study_id="ST1",
        domain="LB",
        test_code="WBC",
        value=5.0,
        lab_out_of_range=True,
    )

    sysbp_rule = HighSystolicBPCheckRule()
    weight_rule = WeightLossCheckRule()
    lab_rule = LabOutOfRangeCheckRule()
    ae_rule = AEConsentTemporalCheckRule()

    # SYSBP rule applies to SYSBP, not WEIGHT
    assert sysbp_rule.applies_to_observation(obs_bp) is True
    assert sysbp_rule.applies_to_observation(obs_weight) is False

    # Weight loss rule applies to WEIGHT, not SYSBP
    assert weight_rule.applies_to_observation(obs_weight) is True
    assert weight_rule.applies_to_observation(obs_bp) is False

    # Lab rule applies to LB domain, not VS
    assert lab_rule.applies_to_observation(obs_lab) is True
    assert lab_rule.applies_to_observation(obs_bp) is False

    # AE rule applies to AE/DS, not VS SYSBP
    assert ae_rule.applies_to_observation(obs_bp) is False


@pytest.mark.asyncio
async def test_auto_query_generation_and_auto_close_on_form_completion() -> None:
    """Verify form-level evaluation automatically raises queries for failing rules and closes queries when checks pass."""
    headers = get_v2_auth_headers(
        user_id="dm_001", roles="Data Manager", change_reason="Testing"
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create Subject and Visit
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-AUTO-1", "study_id": "STUDY-AUTO"},
            headers=headers,
        )
        v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-AUTO-1",
                "visit_name": "BASELINE",
                "study_id": "STUDY-AUTO",
            },
            headers=headers,
        )
        visit_id = v_resp.json()["id"]

        # 1. Create Informed Consent date
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AUTO-1",
                "study_id": "STUDY-AUTO",
                "visit_id": visit_id,
                "domain": "DS",
                "test_code": "DSSTDTC",
                "test_name": "Consent Date",
                "value_string": "2026-08-01",
                "page_id": "DS_FORM",
            },
            headers=headers,
        )

        # 2. Create AE with onset BEFORE consent (2026-07-01 vs 2026-08-01) -> Failing rule!
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AUTO-1",
                "study_id": "STUDY-AUTO",
                "visit_id": visit_id,
                "domain": "AE",
                "test_code": "AESTDTC",
                "test_name": "AE Onset Date",
                "value_string": "2026-07-01",
                "page_id": "AE_FORM",
            },
            headers=headers,
        )

        # Complete AE Form
        f_resp = await client.post(
            "/api/v1/execution/form-submissions",
            json={
                "study_id": "STUDY-AUTO",
                "site_id": "SITE-001",
                "subject_id": "SUBJ-AUTO-1",
                "visit_id": visit_id,
                "form_id": "AE_FORM",
            },
            headers=headers,
        )
        sub_id = f_resp.json()["id"]

        await client.post(
            f"/api/v1/execution/form-submissions/{sub_id}/complete",
            headers=headers,
        )

        await asyncio.sleep(0.1)

        # Verify query was opened for AE_CONSENT_TEMPORAL_CHECK
        q_resp = await client.get("/api/v1/execution/queries", headers=headers)
        queries = q_resp.json()
        ae_queries = [q for q in queries if q["rule_id"] == "AE_CONSENT_TEMPORAL_CHECK"]
        assert len(ae_queries) >= 1
        assert all(q["status"] == "OPEN" for q in ae_queries)

        # 3. Submit corrected AE onset date (2026-08-10, AFTER consent!)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AUTO-1",
                "study_id": "STUDY-AUTO",
                "visit_id": visit_id,
                "domain": "AE",
                "test_code": "AESTDTC",
                "test_name": "AE Onset Date",
                "value_string": "2026-08-10",
                "page_id": "AE_FORM",
            },
            headers=headers,
        )

        # Create new form submission for the update or complete again
        f_resp2 = await client.post(
            "/api/v1/execution/form-submissions",
            json={
                "study_id": "STUDY-AUTO",
                "site_id": "SITE-001",
                "subject_id": "SUBJ-AUTO-1",
                "visit_id": visit_id,
                "form_id": "AE_FORM",
            },
            headers=headers,
        )
        sub_id2 = f_resp2.json()["id"]

        await client.post(
            f"/api/v1/execution/form-submissions/{sub_id2}/complete",
            headers=headers,
        )

        await asyncio.sleep(0.1)

        # Verify open queries for AE_CONSENT_TEMPORAL_CHECK are now CLOSED and auto-resolved
        q_resp2 = await client.get("/api/v1/execution/queries", headers=headers)
        queries2 = q_resp2.json()
        closed_queries = [
            q
            for q in queries2
            if q["rule_id"] == "AE_CONSENT_TEMPORAL_CHECK" and q["status"] == "CLOSED"
        ]
        open_queries = [
            q
            for q in queries2
            if q["rule_id"] == "AE_CONSENT_TEMPORAL_CHECK" and q["status"] == "OPEN"
        ]
        assert len(closed_queries) >= 1
        assert len(open_queries) == 0
        assert closed_queries[0]["resolver"] == "SYSTEM"
        assert "Auto-resolved" in closed_queries[0]["response"]


@pytest.mark.asyncio
async def test_predecessor_pause_and_resume_on_form_completion() -> None:
    """Verify edit checks pause when predecessor visit is missing and resume upon predecessor form submission."""
    headers = get_v2_auth_headers(
        user_id="dm_001", roles="Data Manager", change_reason="Testing"
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create Subject
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-PRED-1", "study_id": "STUDY-PRED"},
            headers=headers,
        )

        # Create Visits: SCREENING (predecessor) and BASELINE (current)
        screen_v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-PRED-1",
                "visit_name": "SCREENING",
                "study_id": "STUDY-PRED",
            },
            headers=headers,
        )
        screen_visit_id = screen_v_resp.json()["id"]

        base_v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-PRED-1",
                "visit_name": "BASELINE",
                "study_id": "STUDY-PRED",
            },
            headers=headers,
        )
        base_visit_id = base_v_resp.json()["id"]

        # Submit BASELINE weight = 60.0 (SCREENING weight is missing!)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-PRED-1",
                "study_id": "STUDY-PRED",
                "visit_id": base_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 60.0,
                "page_id": "VS_FORM",
            },
            headers=headers,
        )

        # Complete BASELINE form submission
        base_f_resp = await client.post(
            "/api/v1/execution/form-submissions",
            json={
                "study_id": "STUDY-PRED",
                "site_id": "SITE-001",
                "subject_id": "SUBJ-PRED-1",
                "visit_id": base_visit_id,
                "form_id": "VS_FORM",
            },
            headers=headers,
        )
        base_sub_id = base_f_resp.json()["id"]

        await client.post(
            f"/api/v1/execution/form-submissions/{base_sub_id}/complete",
            headers=headers,
        )

        await asyncio.sleep(0.1)

        # Verify no query opened yet, and PendingPredecessorCheck recorded
        q_resp = await client.get("/api/v1/execution/queries", headers=headers)
        assert (
            len([q for q in q_resp.json() if q["rule_id"] == "WEIGHT_LOSS_CHECK"]) == 0
        )

        async with db_manager.get_session_maker()() as session:
            stmt = select(PendingPredecessorCheck).where(
                PendingPredecessorCheck.subject_id == "SUBJ-PRED-1",
                PendingPredecessorCheck.rule_id == "WEIGHT_LOSS_CHECK",
                PendingPredecessorCheck.is_deleted.is_(False),
            )
            res = await session.execute(stmt)
            pending = res.scalars().all()
            assert len(pending) == 1
            assert pending[0].predecessor_visit_name == "SCREENING"

        # Now submit SCREENING weight = 100.0 (60.0 is >20% loss vs 100.0)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-PRED-1",
                "study_id": "STUDY-PRED",
                "visit_id": screen_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 100.0,
                "page_id": "VS_FORM_SCR",
            },
            headers=headers,
        )

        # Complete SCREENING form submission
        scr_f_resp = await client.post(
            "/api/v1/execution/form-submissions",
            json={
                "study_id": "STUDY-PRED",
                "site_id": "SITE-001",
                "subject_id": "SUBJ-PRED-1",
                "visit_id": screen_visit_id,
                "form_id": "VS_FORM_SCR",
            },
            headers=headers,
        )
        scr_sub_id = scr_f_resp.json()["id"]

        await client.post(
            f"/api/v1/execution/form-submissions/{scr_sub_id}/complete",
            headers=headers,
        )

        await asyncio.sleep(0.1)

        # Verify query opened for BASELINE weight loss check
        q_resp2 = await client.get("/api/v1/execution/queries", headers=headers)
        weight_queries = [
            q for q in q_resp2.json() if q["rule_id"] == "WEIGHT_LOSS_CHECK"
        ]
        assert len(weight_queries) == 1
        assert weight_queries[0]["status"] == "OPEN"

        # Verify PendingPredecessorCheck is soft deleted
        async with db_manager.get_session_maker()() as session:
            stmt = select(PendingPredecessorCheck).where(
                PendingPredecessorCheck.subject_id == "SUBJ-PRED-1",
                PendingPredecessorCheck.rule_id == "WEIGHT_LOSS_CHECK",
            )
            res = await session.execute(stmt)
            all_p = res.scalars().all()
            assert len([p for p in all_p if not p.is_deleted]) == 0
            assert len([p for p in all_p if p.is_deleted]) == 1
