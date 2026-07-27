import asyncio
from typing import AsyncGenerator

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
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
async def test_same_record_failure_outlier_and_auto_close() -> None:
    """Test that a same-record failure opens exactly one query, is not duplicated, and auto-closes on correction."""
    headers = get_v2_auth_headers(
        user_id="dm_user_001",
        roles="Data Manager",
        change_reason="Submit clinical vital signs data",
    )

    # 1. Setup a subject and visit
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create Subject
        sub_resp = await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-OUT", "study_id": "STUDY-EDIT"},
            headers=headers,
        )
        assert sub_resp.status_code == 200

        # Create Visit
        visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-OUT",
                "visit_name": "BASELINE",
                "study_id": "STUDY-EDIT",
            },
            headers=headers,
        )
        assert visit_resp.status_code == 200
        visit_id = visit_resp.json()["id"]

        # Create normal values first to establish standard deviation for outliers
        # We need enough elements so standard deviation allows the outlier to trigger (>3 std dev)
        for i in range(25):
            await client.post(
                "/api/v1/execution/observations",
                json={
                    "subject_id": f"SUBJ-NORM-{i}",
                    "study_id": "STUDY-EDIT",
                    "visit_id": visit_id,
                    "domain": "VS",
                    "test_code": "TEMP",
                    "test_name": "Body Temperature",
                    "value": 37.0,
                    "unit": "Cel",
                },
                headers=headers,
            )

        # 2. Submit an outlier observation (e.g. TEMP = 45.0, mean is 37.0, std dev is 0.0 or small, so 45.0 is an outlier)
        out_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-OUT",
                "study_id": "STUDY-EDIT",
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "TEMP",
                "test_name": "Body Temperature",
                "value": 45.0,
                "unit": "Cel",
            },
            headers=headers,
        )
        assert out_resp.status_code == 200
        assert out_resp.json()["is_outlier"] is True

        # Verify that exactly one query was opened
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        queries = queries_resp.json()
        outlier_queries = [q for q in queries if q["rule_id"] == "OUTLIER_CHECK"]
        assert len(outlier_queries) == 1
        query = outlier_queries[0]
        assert query["status"] == "OPEN"
        assert query["origin"] == "SYSTEM"
        assert query["created_by"] == "SYSTEM"

        # 3. Attempt duplicate submit with same value: should not open another query
        dup_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-OUT",
                "study_id": "STUDY-EDIT",
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "TEMP",
                "test_name": "Body Temperature",
                "value": 45.0,
                "unit": "Cel",
            },
            headers=headers,
        )
        assert dup_resp.status_code == 200

        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        assert (
            len(
                [
                    q
                    for q in queries_resp.json()
                    if q["rule_id"] == "OUTLIER_CHECK" and q["status"] == "OPEN"
                ]
            )
            == 1
        )

        # 4. Submit normal/corrected value (TEMP = 37.0): should automatically resolve and close query
        corr_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-OUT",
                "study_id": "STUDY-EDIT",
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "TEMP",
                "test_name": "Body Temperature",
                "value": 37.0,
                "unit": "Cel",
            },
            headers=headers,
        )
        assert corr_resp.status_code == 200
        assert corr_resp.json()["is_outlier"] is False

        # Verify the query is now CLOSED with system details
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        closed_queries = [
            q
            for q in queries_resp.json()
            if q["rule_id"] == "OUTLIER_CHECK" and q["status"] == "CLOSED"
        ]
        assert len(closed_queries) == 1
        closed_query = closed_queries[0]
        assert closed_query["resolver"] == "SYSTEM"
        assert closed_query["resolved_at"] is not None
        assert "Auto-resolved" in closed_query["response"]


@pytest.mark.asyncio
async def test_cross_form_temporal_consistency_and_context_propagation() -> None:
    """Test that cross-form temporal evaluations execute in the background and propagate session + audit context."""
    headers = get_v2_auth_headers(
        user_id="dm_user_99",
        roles="Data Manager",
        change_reason="Register Subject Screening Dates",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create Subject
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-CF", "study_id": "STUDY-CF"},
            headers=headers,
        )

        # Create Visit
        v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-CF",
                "visit_name": "SCREENING",
                "study_id": "STUDY-CF",
            },
            headers=headers,
        )
        visit_id = v_resp.json()["id"]

        # 1. Informed Consent Date (e.g. 2026-08-01)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-CF",
                "study_id": "STUDY-CF",
                "visit_id": visit_id,
                "domain": "DS",
                "test_code": "DSSTDTC",
                "test_name": "Informed Consent Date",
                "value_string": "2026-08-01",
            },
            headers=headers,
        )

        # 2. Adverse Event Onset Date (e.g. 2026-07-15, which is BEFORE informed consent!)
        ae_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-CF",
                "study_id": "STUDY-CF",
                "visit_id": visit_id,
                "domain": "AE",
                "test_code": "AESTDTC",
                "test_name": "AE Onset Date",
                "value_string": "2026-07-15",
            },
            headers=headers,
        )
        assert ae_resp.status_code == 200

        # Wait briefly for background task to complete (FastAPI ASGITransport executes it on request completion, but let's be safe)
        await asyncio.sleep(0.1)

        # Verify system query was opened for AE_CONSENT_TEMPORAL_CHECK
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        queries = queries_resp.json()
        cf_queries = [q for q in queries if q["rule_id"] == "AE_CONSENT_TEMPORAL_CHECK"]
        assert len(cf_queries) == 1
        query = cf_queries[0]
        assert query["status"] == "OPEN"
        assert "onset date cannot be before informed consent" in query["message"]

        # Check audit trail of the query to ensure context was propagated correctly
        assert len(query["history"]) == 1
        assert query["history"][0]["user_id"] == "dm_user_99"
        assert (
            query["history"][0]["change_reason"] == "Register Subject Screening Dates"
        )


@pytest.mark.asyncio
async def test_deferred_predecessor_checks() -> None:
    """Test that missing predecessor data is deferred (as Pending-Predecessor check) and reevaluated upon completion."""
    headers = get_v2_auth_headers(
        user_id="dm_user_001",
        roles="Data Manager",
        change_reason="Weight entry",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create Subject
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-LONG", "study_id": "STUDY-LONG"},
            headers=headers,
        )

        # Create Visits: BASELINE and SCREENING (predecessor)
        base_visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-LONG",
                "visit_name": "BASELINE",
                "study_id": "STUDY-LONG",
            },
            headers=headers,
        )
        base_visit_id = base_visit_resp.json()["id"]

        screen_visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-LONG",
                "visit_name": "SCREENING",
                "study_id": "STUDY-LONG",
            },
            headers=headers,
        )
        screen_visit_id = screen_visit_resp.json()["id"]

        # 1. Submit weight at BASELINE. SCREENING weight is missing, so weight comparison is deferred!
        base_weight_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-LONG",
                "study_id": "STUDY-LONG",
                "visit_id": base_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 60.0,
                "unit": "kg",
            },
            headers=headers,
        )
        assert base_weight_resp.status_code == 200

        # Wait briefly
        await asyncio.sleep(0.1)

        # Verify no query is opened yet since predecessor weight is missing
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        assert (
            len([q for q in queries_resp.json() if q["rule_id"] == "WEIGHT_LOSS_CHECK"])
            == 0
        )

        # Verify that a PendingPredecessorCheck was created in database
        async with db_manager.get_session_maker()() as session:
            stmt = select(PendingPredecessorCheck).where(
                PendingPredecessorCheck.subject_id == "SUBJ-LONG",
                PendingPredecessorCheck.rule_id == "WEIGHT_LOSS_CHECK",
                PendingPredecessorCheck.is_deleted.is_(False),
            )
            res = await session.execute(stmt)
            pending_list = res.scalars().all()
            assert len(pending_list) == 1
            assert pending_list[0].predecessor_visit_name == "SCREENING"

        # 2. Submit weight at SCREENING = 100.0.
        # Predecessor is now completed. Since 60.0 is < 80% of 100.0 (greater than 20% weight loss!),
        # re-evaluation of BASELINE weight should fail and open a query!
        screen_weight_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-LONG",
                "study_id": "STUDY-LONG",
                "visit_id": screen_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 100.0,
                "unit": "kg",
            },
            headers=headers,
        )
        assert screen_weight_resp.status_code == 200

        # Wait briefly for background resolution
        await asyncio.sleep(0.1)

        # Verify query was opened
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

        # Verify the PendingPredecessorCheck was soft-deleted (is_deleted = True)
        async with db_manager.get_session_maker()() as session:
            stmt = select(PendingPredecessorCheck).where(
                PendingPredecessorCheck.subject_id == "SUBJ-LONG",
                PendingPredecessorCheck.rule_id == "WEIGHT_LOSS_CHECK",
            )
            res = await session.execute(stmt)
            all_pending = res.scalars().all()
            # It should be soft deleted
            assert len([p for p in all_pending if not p.is_deleted]) == 0
            assert len([p for p in all_pending if p.is_deleted]) == 1


@pytest.mark.asyncio
async def test_dynamic_ast_edit_check_auto_resolve() -> None:
    # @req:PRD-QRY-003
    # @req:PRD-EDC-003
    """Test dynamic AST-based cross-form check rule and automatic clinical query auto-resolution with audit trail change reason."""
    headers = get_v2_auth_headers(
        user_id="dm_user_001",
        roles="Data Manager",
        change_reason="Setup dynamic rules and observations",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Publish a study with dynamic AST rule
        # Rule: AEOnset before Consent is a failure.
        # Condition: AEOnset < INFORMED_CONSENT
        # In AST:
        # {
        #   "type": "comparison", "operator": "<",
        #   "operands": [
        #     {"type": "field_ref", "field_ref": {"field_id": "AESTDTC"}},
        #     {"type": "field_ref", "field_ref": {"field_id": "DSSTDTC"}}
        #   ]
        # }
        rule_payload = {
            "id": "AST_AE_CONSENT_CHECK",
            "type": "cross_form_check",
            "condition": {
                "type": "comparison",
                "operator": "<",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "AESTDTC"}},
                    {"type": "field_ref", "field_ref": {"field_id": "DSSTDTC"}},
                ],
            },
            "query_message": "Dynamic AST Check: AEonset date cannot be before Informed Consent.",
        }

        pub_resp = await client.post(
            "/events/study-published",
            json={
                "study_id": "DYNAMIC-AST-STUDY",
                "payload": {
                    "protocol": {
                        "items": [],
                        "rules": [rule_payload],
                    }
                },
            },
            headers=headers,
        )
        assert pub_resp.status_code == 200

        # Create Subject
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-AST-1", "study_id": "DYNAMIC-AST-STUDY"},
            headers=headers,
        )

        # Create Visit
        v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-AST-1",
                "visit_name": "SCREENING",
                "study_id": "DYNAMIC-AST-STUDY",
            },
            headers=headers,
        )
        visit_id = v_resp.json()["id"]

        # Create Informed Consent Date = 2026-08-01
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AST-1",
                "study_id": "DYNAMIC-AST-STUDY",
                "visit_id": visit_id,
                "domain": "DS",
                "test_code": "DSSTDTC",
                "test_name": "Consent Date",
                "value_string": "2026-08-01",
            },
            headers=headers,
        )

        # Create AE Onset Date = 2026-07-20 (BEFORE Consent!)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AST-1",
                "study_id": "DYNAMIC-AST-STUDY",
                "visit_id": visit_id,
                "domain": "AE",
                "test_code": "AESTDTC",
                "test_name": "AE Date",
                "value_string": "2026-07-20",
            },
            headers=headers,
        )

        # Wait briefly for background evaluation
        await asyncio.sleep(0.1)

        # Verify dynamic query was raised
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        queries = queries_resp.json()
        ast_queries = [q for q in queries if q["rule_id"] == "AST_AE_CONSENT_CHECK"]
        assert len(ast_queries) == 1
        query = ast_queries[0]
        assert query["status"] == "OPEN"
        assert "AEonset date cannot be before Informed Consent" in query["message"]

        # 2. Correct AE Onset Date = 2026-08-05 (AFTER Consent!)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AST-1",
                "study_id": "DYNAMIC-AST-STUDY",
                "visit_id": visit_id,
                "domain": "AE",
                "test_code": "AESTDTC",
                "test_name": "AE Date",
                "value_string": "2026-08-05",
            },
            headers=headers,
        )

        # Wait briefly for background recheck and resolution
        await asyncio.sleep(0.1)

        # Verify query was automatically CLOSED with change reason "Edit Check Auto-Resolution"
        queries_resp2 = await client.get("/api/v1/execution/queries", headers=headers)
        queries2 = queries_resp2.json()
        resolved_queries = [q for q in queries2 if q["rule_id"] == "AST_AE_CONSENT_CHECK"]
        assert len(resolved_queries) == 1
        q_resolved = resolved_queries[0]
        assert q_resolved["status"] == "CLOSED"
        assert q_resolved["resolver"] == "SYSTEM"
        assert "Auto-resolved" in q_resolved["response"]

        # Verify change reason "Edit Check Auto-Resolution" in history
        history = q_resolved["history"]
        # Find the update history item
        update_items = [h for h in history if h["action"] == "UPDATE" and h["new_values"].get("status") == "CLOSED"]
        assert len(update_items) == 1
        assert update_items[0]["change_reason"] == "Edit Check Auto-Resolution"
        assert update_items[0]["old_values"]["status"] == "OPEN"
        assert update_items[0]["new_values"]["status"] == "CLOSED"


@pytest.mark.asyncio
async def test_dynamic_ast_edit_check_pause_resume_on_form_complete() -> None:
    # @req:PRD-QRY-004
    # @req:PRD-EDC-004
    """Test longitudinal AST-based weight loss rule: pause when predecessor is Draft, and resume upon form completion."""
    headers = get_v2_auth_headers(
        user_id="dm_user_001",
        roles="Data Manager",
        change_reason="Vitals data submission",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Publish study with weight loss AST check rule
        # Condition: current WEIGHT < 0.8 * previous WEIGHT
        # In AST:
        # {
        #   "type": "comparison", "operator": "<",
        #   "operands": [
        #     {"type": "field_ref", "field_ref": {"field_id": "WEIGHT"}},
        #     {
        #       "type": "comparison", "operator": "*",
        #       "operands": [
        #         {"type": "constant", "value": 0.8},
        #         {"type": "field_ref", "field_ref": {"field_id": "WEIGHT", "visit_relative": "previous"}}
        #       ]
        #     }
        #   ]
        # }
        rule_payload = {
            "id": "AST_WEIGHT_LOSS_CHECK",
            "type": "cross_form_check",
            "condition": {
                "type": "comparison",
                "operator": "<",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "WEIGHT"}},
                    {
                        "type": "comparison",
                        "operator": "*",
                        "operands": [
                            {"type": "constant", "value": 0.8},
                            {"type": "field_ref", "field_ref": {"field_id": "WEIGHT", "visit_relative": "previous"}},
                        ],
                    },
                ],
            },
            "query_message": "Dynamic AST: Weight loss is critically high (>20%) compared to previous visit.",
        }

        pub_resp = await client.post(
            "/events/study-published",
            json={
                "study_id": "AST-STUDY-LONG",
                "payload": {
                    "study": {
                        "rules": [rule_payload],
                    }
                },
            },
            headers=headers,
        )
        assert pub_resp.status_code == 200

        # Create Subject
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-LONG-AST", "study_id": "AST-STUDY-LONG"},
            headers=headers,
        )

        # Create SCREENING and BASELINE visits
        screen_visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-LONG-AST",
                "visit_name": "SCREENING",
                "study_id": "AST-STUDY-LONG",
            },
            headers=headers,
        )
        screen_visit_id = screen_visit_resp.json()["id"]

        base_visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-LONG-AST",
                "visit_name": "BASELINE",
                "study_id": "AST-STUDY-LONG",
            },
            headers=headers,
        )
        base_visit_id = base_visit_resp.json()["id"]

        # Create DRAFT Form Submission for SCREENING predecessor
        sub_resp = await client.post(
            "/api/v1/execution/form-submissions",
            json={
                "study_id": "AST-STUDY-LONG",
                "site_id": "SITE-01",
                "subject_id": "SUBJ-LONG-AST",
                "visit_id": screen_visit_id,
                "form_id": "VS_FORM",
            },
            headers=headers,
        )
        sub_id = sub_resp.json()["id"]
        assert sub_resp.json()["status"] == "DRAFT"

        # Submit Weight = 60.0 at BASELINE (predecessor SCREENING is Draft)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-LONG-AST",
                "study_id": "AST-STUDY-LONG",
                "visit_id": base_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 60.0,
                "unit": "kg",
            },
            headers=headers,
        )

        # Wait briefly
        await asyncio.sleep(0.1)

        # No query opened because predecessor is still Draft
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        assert len([q for q in queries_resp.json() if q["rule_id"] == "AST_WEIGHT_LOSS_CHECK"]) == 0

        # Verify PendingPredecessorCheck is saved
        async with db_manager.get_session_maker()() as session:
            stmt = select(PendingPredecessorCheck).where(
                PendingPredecessorCheck.subject_id == "SUBJ-LONG-AST",
                PendingPredecessorCheck.rule_id == "AST_WEIGHT_LOSS_CHECK",
                PendingPredecessorCheck.is_deleted.is_(False),
            )
            res = await session.execute(stmt)
            assert len(res.scalars().all()) == 1

        # Submit Predecessor Weight = 100.0 at SCREENING
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-LONG-AST",
                "study_id": "AST-STUDY-LONG",
                "visit_id": screen_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 100.0,
                "unit": "kg",
            },
            headers=headers,
        )

        # Wait briefly
        await asyncio.sleep(0.1)

        # Still no query opened because predecessor is still Draft
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        assert len([q for q in queries_resp.json() if q["rule_id"] == "AST_WEIGHT_LOSS_CHECK"]) == 0

        # Complete Form Submission for SCREENING predecessor!
        comp_resp = await client.post(
            f"/api/v1/execution/form-submissions/{sub_id}/complete",
            headers=headers,
        )
        assert comp_resp.status_code == 200

        # Wait briefly for resume task
        await asyncio.sleep(0.1)

        # Verify query is now opened (60.0 < 80% of 100.0)
        queries_resp2 = await client.get("/api/v1/execution/queries", headers=headers)
        ast_queries = [q for q in queries_resp2.json() if q["rule_id"] == "AST_WEIGHT_LOSS_CHECK"]
        assert len(ast_queries) == 1
        assert ast_queries[0]["status"] == "OPEN"
        assert "Weight loss is critically high" in ast_queries[0]["message"]

        # 2. Re-submit BASELINE Weight = 95.0 (Passing!)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-LONG-AST",
                "study_id": "AST-STUDY-LONG",
                "visit_id": base_visit_id,
                "domain": "VS",
                "test_code": "WEIGHT",
                "test_name": "Weight",
                "value": 95.0,
                "unit": "kg",
            },
            headers=headers,
        )

        # Wait briefly for auto-resolution
        await asyncio.sleep(0.1)

        # Verify query is auto-resolved/CLOSED
        queries_resp3 = await client.get("/api/v1/execution/queries", headers=headers)
        closed_queries = [q for q in queries_resp3.json() if q["rule_id"] == "AST_WEIGHT_LOSS_CHECK" and q["status"] == "CLOSED"]
        assert len(closed_queries) == 1
        assert closed_queries[0]["resolver"] == "SYSTEM"
        assert "Edit Check Auto-Resolution" in closed_queries[0]["history"][-1]["change_reason"]
