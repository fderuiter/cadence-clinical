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
async def test_authored_cross_form_rule_lifecycle() -> None:
    """Test Task 4: Ingestion and lifecycle evaluation of published authored cross_form_check rules."""
    headers = get_v2_auth_headers(
        user_id="dm_user_100",
        roles="Data Manager",
        change_reason="Publish and test authored cross-form check rules",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Publish the study-level authored cross_form_check rule
        study_id = "STUDY-AUTH-001"
        published_payload = {
            "study_id": study_id,
            "payload": {
                "version": "1.2",
                "cross_form_check": [
                    {
                        "id": "AUTH_RULE_VSDPB_VSSBP",
                        "type": "cross_form_check",
                        "query_message": "Custom Authored Check failed: diastolic cannot exceed systolic BP.",
                        "condition": {
                            "type": "comparison",
                            "operator": ">",
                            "operands": [
                                {
                                    "type": "field_ref",
                                    "field_ref": {"field_id": "VSDPB"},
                                },
                                {
                                    "type": "field_ref",
                                    "field_ref": {"field_id": "VSSBP"},
                                },
                            ],
                        },
                    }
                ],
            },
        }
        pub_resp = await client.post(
            "/events/study-published", json=published_payload, headers=headers
        )
        assert pub_resp.status_code == 200

        # Wait for background task of process_translation to complete/settle
        await asyncio.sleep(0.2)

        # 2. Setup subject and visit
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-AUTH-1", "study_id": study_id},
            headers=headers,
        )

        v_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-AUTH-1",
                "visit_name": "BASELINE",
                "study_id": study_id,
            },
            headers=headers,
        )
        visit_id = v_resp.json()["id"]

        # 3. Post observations that VIOLATE the check (VSDPB = 120, VSSBP = 110)
        # Post VSSBP = 110 first
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AUTH-1",
                "study_id": study_id,
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "VSSBP",
                "test_name": "Systolic Blood Pressure",
                "value": 110.0,
                "unit": "mmHg",
            },
            headers=headers,
        )
        # Post VSDPB = 120 (violates VSDPB > VSSBP)
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AUTH-1",
                "study_id": study_id,
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "VSDPB",
                "test_name": "Diastolic Blood Pressure",
                "value": 120.0,
                "unit": "mmHg",
            },
            headers=headers,
        )

        # Wait for background async check
        await asyncio.sleep(0.2)

        # 4. Assert exactly one ClinicalQuery opens with the authored rule_id and SYSTEM origin
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        queries = queries_resp.json()
        rule_queries = [q for q in queries if q["rule_id"] == "AUTH_RULE_VSDPB_VSSBP"]
        assert len(rule_queries) == 1
        query = rule_queries[0]
        assert query["status"] == "OPEN"
        assert query["origin"] == "SYSTEM"
        assert "diastolic cannot exceed systolic BP" in query["message"]

        # Check audit trail of the query for Part 11 compliant change reason propagation
        assert len(query["history"]) == 1
        assert query["history"][0]["user_id"] == "dm_user_100"
        assert (
            query["history"][0]["change_reason"]
            == "Publish and test authored cross-form check rules"
        )

        # 5. Submit corrected observations (VSDPB = 80, VSSBP = 110)
        # Since VSDPB = 80 is not > 110, the check passes, and query must auto-resolve
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AUTH-1",
                "study_id": study_id,
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "VSDPB",
                "test_name": "Diastolic Blood Pressure",
                "value": 80.0,
                "unit": "mmHg",
            },
            headers=headers,
        )

        # Wait for background async check
        await asyncio.sleep(0.2)

        # Verify query is now CLOSED and auto-resolved
        queries_resp2 = await client.get("/api/v1/execution/queries", headers=headers)
        closed_queries = [
            q
            for q in queries_resp2.json()
            if q["rule_id"] == "AUTH_RULE_VSDPB_VSSBP" and q["status"] == "CLOSED"
        ]
        assert len(closed_queries) == 1
        closed_q = closed_queries[0]
        assert closed_q["resolver"] == "SYSTEM"
        assert "Auto-resolved" in closed_q["response"]


@pytest.mark.asyncio
async def test_authored_longitudinal_predecessor_handling() -> None:
    """Test Task 4: Ingestion and evaluation of published longitudinal authored rule using PendingPredecessorCheck."""
    headers = get_v2_auth_headers(
        user_id="dm_user_200",
        roles="Data Manager",
        change_reason="Publish and test authored predecessor longitudinal check rules",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Publish the study-level longitudinal authored cross_form_check rule
        study_id = "STUDY-AUTH-LONG"
        published_payload = {
            "study_id": study_id,
            "payload": {
                "version": "1.3",
                "cross_form_check": [
                    {
                        "id": "AUTH_LONG_RULE",
                        "type": "cross_form_check",
                        "query_message": "Authored rule failed: diastolic increased by over 50 compared to predecessor.",
                        "condition": {
                            "type": "comparison",
                            "operator": ">",
                            "operands": [
                                {
                                    "type": "field_ref",
                                    "field_ref": {"field_id": "VSDPB"},
                                },
                                {
                                    "type": "comparison",
                                    "operator": "+",
                                    "operands": [
                                        {
                                            "type": "field_ref",
                                            "field_ref": {
                                                "field_id": "VSDPB",
                                                "visit_relative": "previous",
                                            },
                                        },
                                        {"type": "constant", "value": 50.0},
                                    ],
                                },
                            ],
                        },
                    }
                ],
            },
        }
        pub_resp = await client.post(
            "/events/study-published", json=published_payload, headers=headers
        )
        assert pub_resp.status_code == 200

        # Wait for background task of process_translation to complete/settle
        await asyncio.sleep(0.2)

        # 2. Setup subject and visits
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": "SUBJ-AUTH-LONG-1", "study_id": study_id},
            headers=headers,
        )

        base_visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-AUTH-LONG-1",
                "visit_name": "BASELINE",
                "study_id": study_id,
            },
            headers=headers,
        )
        base_visit_id = base_visit_resp.json()["id"]

        screen_visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": "SUBJ-AUTH-LONG-1",
                "visit_name": "SCREENING",
                "study_id": study_id,
            },
            headers=headers,
        )
        screen_visit_id = screen_visit_resp.json()["id"]

        # 3. Post observation at BASELINE. SCREENING weight is missing, so weight comparison is deferred!
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AUTH-LONG-1",
                "study_id": study_id,
                "visit_id": base_visit_id,
                "domain": "VS",
                "test_code": "VSDPB",
                "test_name": "Diastolic Blood Pressure",
                "value": 110.0,
                "unit": "mmHg",
            },
            headers=headers,
        )

        # Wait briefly
        await asyncio.sleep(0.2)

        # Verify no query is opened yet since predecessor (SCREENING) VSDPB is missing
        queries_resp = await client.get("/api/v1/execution/queries", headers=headers)
        assert (
            len([q for q in queries_resp.json() if q["rule_id"] == "AUTH_LONG_RULE"])
            == 0
        )

        # Verify that a PendingPredecessorCheck was created in database
        async with db_manager.get_session_maker()() as session:
            stmt = select(PendingPredecessorCheck).where(
                PendingPredecessorCheck.subject_id == "SUBJ-AUTH-LONG-1",
                PendingPredecessorCheck.rule_id == "AUTH_LONG_RULE",
                PendingPredecessorCheck.is_deleted.is_(False),
            )
            res = await session.execute(stmt)
            pending_list = res.scalars().all()
            assert len(pending_list) == 1
            assert pending_list[0].predecessor_visit_name == "SCREENING"

        # 4. Submit observation at SCREENING = 50.0.
        # Predecessor is now completed. 110.0 is > 50.0 + 50.0 (100.0), so check fails and query opens on BASELINE!
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-AUTH-LONG-1",
                "study_id": study_id,
                "visit_id": screen_visit_id,
                "domain": "VS",
                "test_code": "VSDPB",
                "test_name": "Diastolic Blood Pressure",
                "value": 50.0,
                "unit": "mmHg",
            },
            headers=headers,
        )

        # Wait briefly for background resolution
        await asyncio.sleep(0.2)

        # Verify query was opened on BASELINE visit's VSDPB
        queries_resp2 = await client.get("/api/v1/execution/queries", headers=headers)
        long_queries = [
            q for q in queries_resp2.json() if q["rule_id"] == "AUTH_LONG_RULE"
        ]
        assert len(long_queries) == 1
        assert long_queries[0]["status"] == "OPEN"
        assert (
            "diastolic increased by over 50 compared to predecessor"
            in long_queries[0]["message"]
        )

        # Verify the PendingPredecessorCheck was soft-deleted (is_deleted = True)
        async with db_manager.get_session_maker()() as session:
            stmt = select(PendingPredecessorCheck).where(
                PendingPredecessorCheck.subject_id == "SUBJ-AUTH-LONG-1",
                PendingPredecessorCheck.rule_id == "AUTH_LONG_RULE",
            )
            res = await session.execute(stmt)
            all_pending = res.scalars().all()
            assert len([p for p in all_pending if not p.is_deleted]) == 0
            assert len([p for p in all_pending if p.is_deleted]) == 1
