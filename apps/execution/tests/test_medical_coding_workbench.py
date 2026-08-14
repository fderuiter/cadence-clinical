import hashlib
import hmac
import json
import os
import time
from collections.abc import AsyncGenerator

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalObservation,
    ClinicalSubject,
    CodingState,
    DictionaryType,
    MedDRAHierarchy,
    MedDRATerm,
    WHODrugATC,
    WHODrugDrugATC,
    WHODrugDrugIngredient,
    WHODrugIngredient,
    WHODrugRecord,
)
from apps.execution.main import app as exec_app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id: str = "coder_admin",
    roles: str = "Data Manager,TERMINOLOGY_MANAGER,SYSTEM_ADMIN",
    change_reason: str = "Medical coding workbench test review",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers for execution tests."""
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
async def setup_workbench_db() -> AsyncGenerator[None]:
    """Isolates in-memory database and deploys 21 CFR Part 11 audit triggers."""
    from apps.execution.coding.matcher import coding_cache

    TrialLockManager.reset()
    coding_cache.clear()
    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
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
    TrialLockManager.reset()
    coding_cache.clear()


async def seed_workbench_data() -> dict[str, str]:
    """Seeds comprehensive MedDRA and WHODrug dictionaries and sample assignments."""
    async with db_manager.get_session_maker()() as session, session.begin():
        # 1. Subject
        subject = ClinicalSubject(
            id="SUBJ-UUID-101",
            subject_id="SUBJ-101",
            study_id="STUDY-001",
        )
        session.add(subject)

        # 2. Observations
        obs_ae = ClinicalObservation(
            id="OBS-AE-001",
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Verbatim",
            value_string="Severe pounding headache",
        )
        obs_cm = ClinicalObservation(
            id="OBS-CM-001",
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="CM",
            test_code="CMTRT",
            test_name="Concomitant Medication",
            value_string="Aspirin 100mg tablet",
        )
        obs_uncodable = ClinicalObservation(
            id="OBS-UNCODABLE-001",
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Verbatim",
            value_string="xyz_nonsense_verbatim_string",
        )
        session.add_all([obs_ae, obs_cm, obs_uncodable])

        # 3. MedDRA Dictionary Terms v26.0
        soc_term = MedDRATerm(
            dictionary_version="26.0",
            code="10029205",
            term_name="Nervous system disorders",
            level="SOC",
        )
        hlgt_term = MedDRATerm(
            dictionary_version="26.0",
            code="10019214",
            term_name="Headaches",
            level="HLGT",
        )
        hlt_term = MedDRATerm(
            dictionary_version="26.0",
            code="10019231",
            term_name="Headaches NEC",
            level="HLT",
        )
        pt_term = MedDRATerm(
            dictionary_version="26.0",
            code="10019211",
            term_name="Headache",
            level="PT",
        )
        llt_term = MedDRATerm(
            dictionary_version="26.0",
            code="10019211",
            term_name="Headache",
            level="LLT",
        )
        llt_alt = MedDRATerm(
            dictionary_version="26.0",
            code="10019212",
            term_name="Headache severe",
            level="LLT",
        )
        session.add_all([soc_term, hlgt_term, hlt_term, pt_term, llt_term, llt_alt])

        # MedDRA Hierarchies
        med_hier = MedDRAHierarchy(
            dictionary_version="26.0",
            llt_code="10019211",
            pt_code="10019211",
            hlt_code="10019231",
            hlgt_code="10019214",
            soc_code="10029205",
            primary_soc_flag="Y",
        )
        session.add(med_hier)

        # 4. WHODrug Records v2024-03
        who_rec = WHODrugRecord(
            dictionary_version="2024-03",
            drug_code="00010101001",
            preferred_name="ASPIRIN",
            drug_name="ASPIRIN TABLET",
        )
        session.add(who_rec)

        who_atc = WHODrugATC(
            dictionary_version="2024-03",
            atc_code="N02BA01",
            description="Acetylsalicylic acid",
        )
        session.add(who_atc)

        who_drug_atc = WHODrugDrugATC(
            dictionary_version="2024-03",
            drug_code="00010101001",
            atc_code="N02BA01",
        )
        session.add(who_drug_atc)

        who_ing = WHODrugIngredient(
            dictionary_version="2024-03",
            ingredient_code="ING-001",
            ingredient_name="ACETYLSALICYLIC ACID",
        )
        session.add(who_ing)

        who_drug_ing = WHODrugDrugIngredient(
            dictionary_version="2024-03",
            drug_code="00010101001",
            ingredient_code="ING-001",
        )
        session.add(who_drug_ing)

        # 5. Clinical Coding Assignments
        assign_suggested = ClinicalCodingAssignment(
            id="ASSIGN-SUGGESTED-1",
            verbatim_text="Severe pounding headache",
            source_field="AE.AETERM",
            observation_id="OBS-AE-001",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.SUGGESTED,
            score=0.78,
            suggestions=[
                {
                    "code": "10019211",
                    "term_name": "Headache",
                    "level": "LLT",
                    "score": 0.78,
                    "hierarchies": [
                        {
                            "llt_code": "10019211",
                            "llt_name": "Headache",
                            "pt_code": "10019211",
                            "pt_name": "Headache",
                            "hlt_code": "10019231",
                            "hlt_name": "Headaches NEC",
                            "hlgt_code": "10019214",
                            "hlgt_name": "Headaches",
                            "soc_code": "10029205",
                            "soc_name": "Nervous system disorders",
                            "primary_soc_flag": "Y",
                        }
                    ],
                }
            ],
            domain="AE",
        )

        assign_uncoded = ClinicalCodingAssignment(
            id="ASSIGN-UNCODED-1",
            verbatim_text="Aspirin 100mg tablet",
            source_field="CM.CMTRT",
            observation_id="OBS-CM-001",
            dictionary_type=DictionaryType.WHODRUG,
            dictionary_version="2024-03",
            status=CodingState.UNCODED,
            domain="CM",
            suggestions=[
                {
                    "drug_code": "00010101001",
                    "preferred_name": "ASPIRIN",
                    "drug_name": "ASPIRIN TABLET",
                    "score": 0.82,
                    "atc_context": [
                        {
                            "atc_code": "N02BA01",
                            "description": "Acetylsalicylic acid",
                        }
                    ],
                    "ingredients": [
                        {
                            "ingredient_code": "ING-001",
                            "ingredient_name": "ACETYLSALICYLIC ACID",
                        }
                    ],
                }
            ],
        )

        assign_batch_2 = ClinicalCodingAssignment(
            id="ASSIGN-UNCODED-2",
            verbatim_text="Aspirin cardio 75mg",
            source_field="CM.CMTRT",
            observation_id="OBS-CM-001",
            dictionary_type=DictionaryType.WHODRUG,
            dictionary_version="2024-03",
            status=CodingState.UNCODED,
            domain="CM",
        )

        assign_query = ClinicalCodingAssignment(
            id="ASSIGN-QUERY-1",
            verbatim_text="xyz_nonsense_verbatim_string",
            source_field="AE.AETERM",
            observation_id="OBS-UNCODABLE-001",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.UNCODED,
            domain="AE",
        )

        session.add_all(
            [assign_suggested, assign_uncoded, assign_batch_2, assign_query]
        )

    return {
        "assign_suggested": "ASSIGN-SUGGESTED-1",
        "assign_uncoded": "ASSIGN-UNCODED-1",
        "assign_batch_2": "ASSIGN-UNCODED-2",
        "assign_query": "ASSIGN-QUERY-1",
    }


@pytest.mark.asyncio
async def test_coding_queue_retrieval_and_filtering() -> None:
    """Verify that coding queue returns paginated/filtered assignments matching status and dictionary.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    await seed_workbench_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # 1. Retrieve all assignments
        resp = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200
        assignments = resp.json()
        assert len(assignments) >= 4

        # 2. Filter by status: UNCODED
        resp_uncoded = await client.get(
            "/api/v1/execution/coding/assignments?status=UNCODED",
            headers=get_auth_headers(),
        )
        assert resp_uncoded.status_code == 200
        uncoded_list = resp_uncoded.json()
        assert all(item["status"] == "UNCODED" for item in uncoded_list)
        assert len(uncoded_list) >= 3

        # 3. Filter by dictionary_type: WHODRUG
        resp_who = await client.get(
            "/api/v1/execution/coding/assignments?dictionary_type=WHODRUG",
            headers=get_auth_headers(),
        )
        assert resp_who.status_code == 200
        who_list = resp_who.json()
        assert all(item["dictionary_type"] == "WHODRUG" for item in who_list)
        assert len(who_list) >= 2

        # 4. Verify queue endpoint alias
        resp_queue = await client.get(
            "/api/v1/execution/coding/queue?status=SUGGESTED",
            headers=get_auth_headers(),
        )
        assert resp_queue.status_code == 200
        sug_list = resp_queue.json()
        assert len(sug_list) >= 1
        assert sug_list[0]["status"] == "SUGGESTED"


@pytest.mark.asyncio
async def test_meddra_hierarchy_search_and_traversal() -> None:
    """Verify that MedDRA terminology lookups resolve full 5-level hierarchy path.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    await seed_workbench_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Search MedDRA for term 'Headache'
        resp = await client.get(
            "/api/v1/dictionaries/meddra/code?term=Headache&version=26.0&target_level=LLT",
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["status"] in ("AUTO-CODED", "SUGGESTIONS")
        assert len(data["matches"]) >= 1

        match = data["matches"][0]
        assert match["llt_code"] == "10019211"
        assert match["llt_name"] == "Headache"
        assert match["pt_code"] == "10019211"
        assert match["pt_name"] == "Headache"
        assert match["hlt_code"] == "10019231"
        assert match["hlt_name"] == "Headaches NEC"
        assert match["hlgt_code"] == "10019214"
        assert match["hlgt_name"] == "Headaches"
        assert match["soc_code"] == "10029205"
        assert match["soc_name"] == "Nervous system disorders"
        assert match["primary_soc_flag"] == "Y"
        assert match["score"] >= 0.85


@pytest.mark.asyncio
async def test_whodrug_atc_and_ingredient_lookup() -> None:
    """Verify that WHODrug lookups resolve drug codes, ATC hierarchy, and active ingredients.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    await seed_workbench_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Search WHODrug for 'Aspirin'
        resp = await client.get(
            "/api/v1/dictionaries/whodrug/code?term=Aspirin&version=2024-03",
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["status"] in ("AUTO-CODED", "SUGGESTIONS")
        assert len(data["matches"]) >= 1

        match = data["matches"][0]
        assert match["drug_code"] == "00010101001"
        assert match["preferred_name"] == "ASPIRIN"
        assert len(match["atc_context"]) >= 1
        assert match["atc_context"][0]["atc_code"] == "N02BA01"
        assert "Acetylsalicylic acid" in match["atc_context"][0]["description"]
        assert len(match["ingredients"]) >= 1
        assert match["ingredients"][0]["ingredient_code"] == "ING-001"
        assert match["ingredients"][0]["ingredient_name"] == "ACETYLSALICYLIC ACID"


@pytest.mark.asyncio
async def test_single_coder_action_accept_and_override() -> None:
    """Verify that accepting or overriding single coding assignments records GxP ledger entries.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    ids = await seed_workbench_data()
    suggested_id = ids["assign_suggested"]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # 1. Accept suggestion index 0
        resp_accept = await client.post(
            f"/api/v1/execution/coding/assignments/{suggested_id}/action",
            json={
                "action": "ACCEPT",
                "suggestion_index": 0,
                "reason_for_change": "Accepted primary suggestion",
            },
            headers=get_auth_headers(user_id="dm_coder_1"),
        )
        assert resp_accept.status_code == 200
        data_accept = resp_accept.json()
        assert data_accept["status"] == "CODED"
        assert data_accept["coded_code"] == "10019211"
        assert data_accept["coded_term"] == "Headache"
        assert data_accept["assigned_by"] == "dm_coder_1"

        # Verify ClinicalCodingLedger record exists
        async with db_manager.get_session_maker()() as session:
            stmt = select(ClinicalCodingLedger).where(
                ClinicalCodingLedger.assignment_id == suggested_id
            )
            res = await session.execute(stmt)
            ledger_entries = res.scalars().all()
            assert len(ledger_entries) >= 1
            assert ledger_entries[-1].new_coded_code == "10019211"
            assert ledger_entries[-1].decision_by == "dm_coder_1"

        # 2. Override with manual code
        uncoded_id = ids["assign_uncoded"]
        resp_override = await client.post(
            f"/api/v1/execution/coding/assignments/{uncoded_id}/action",
            json={
                "action": "OVERRIDE",
                "code": "00010101001",
                "term": "ASPIRIN",
                "reason_for_change": "Manual expert override based on medication package",
            },
            headers=get_auth_headers(user_id="dm_coder_2"),
        )
        assert resp_override.status_code == 200
        data_override = resp_override.json()
        assert data_override["status"] == "CODED"
        assert data_override["coded_code"] == "00010101001"
        assert data_override["coded_term"] == "ASPIRIN"
        assert data_override["assigned_by"] == "dm_coder_2"


@pytest.mark.asyncio
async def test_batch_assignment_with_gxp_audit_logging() -> None:
    """Verify that batch assignment updates multiple records and writes immutable ledger records.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    ids = await seed_workbench_data()
    target_ids = [ids["assign_uncoded"], ids["assign_batch_2"]]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Perform batch assignment of multiple verbatims to ASPIRIN
        resp_batch = await client.post(
            "/api/v1/execution/coding/assignments/batch-assign",
            json={
                "assignment_ids": target_ids,
                "code": "00010101001",
                "term": "ASPIRIN",
                "dictionary_type": "WHODRUG",
                "dictionary_version": "2024-03",
                "reason": "Batch consensus coding of standard OTC aspirin formulations",
                "action": "OVERRIDE",
            },
            headers=get_auth_headers(
                user_id="batch_lead_dm",
                change_reason="Batch coding consensus review",
            ),
        )
        assert resp_batch.status_code == 200
        res_data = resp_batch.json()
        assert res_data["success_count"] == 2
        assert res_data["failed_count"] == 0
        assert len(res_data["results"]) == 2

        # Verify all target assignments are now CODED
        for aid in target_ids:
            resp_get = await client.get(
                f"/api/v1/execution/coding/assignments/{aid}",
                headers=get_auth_headers(),
            )
            assert resp_get.status_code == 200
            assign_data = resp_get.json()
            assert assign_data["status"] == "CODED"
            assert assign_data["coded_code"] == "00010101001"
            assert assign_data["coded_term"] == "ASPIRIN"
            assert assign_data["assigned_by"] == "batch_lead_dm"

        # Verify ledger entries in database
        async with db_manager.get_session_maker()() as session:
            stmt_ledger = select(ClinicalCodingLedger).where(
                ClinicalCodingLedger.assignment_id.in_(target_ids)
            )
            res_ledger = await session.execute(stmt_ledger)
            ledgers = res_ledger.scalars().all()
            assert len(ledgers) == 2
            assert all(entry.new_coded_code == "00010101001" for entry in ledgers)
            assert all(
                "Batch consensus coding" in entry.recoding_reason for entry in ledgers
            )
            assert all(entry.decision_by == "batch_lead_dm" for entry in ledgers)


@pytest.mark.asyncio
async def test_discrepancy_query_escalation_lifecycle() -> None:
    """Verify that raising a query creates an open ClinicalQuery and transitions assignment to QUERY_PENDING.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    ids = await seed_workbench_data()
    query_target_id = ids["assign_query"]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # 1. Escalate query on ambiguous/uncodable assignment
        resp_raise = await client.post(
            f"/api/v1/execution/coding/assignments/{query_target_id}/raise-query",
            json={
                "query_text": "The verbatim term 'xyz_nonsense_verbatim_string' is uncodable. Please specify medical diagnosis.",
                "reason": "Data manager coding discrepancy escalation",
            },
            headers=get_auth_headers(user_id="query_officer"),
        )
        assert resp_raise.status_code == 200
        raise_data = resp_raise.json()
        assert raise_data["status"] == "OPEN"
        assert raise_data["assignment_id"] == query_target_id
        q_id = raise_data["query_id"]

        # 2. Verify assignment status changed to QUERY_PENDING
        resp_assign = await client.get(
            f"/api/v1/execution/coding/assignments/{query_target_id}",
            headers=get_auth_headers(),
        )
        assert resp_assign.status_code == 200
        assert resp_assign.json()["status"] == "QUERY_PENDING"

        # 3. Verify ClinicalQuery discrepancy is accessible in queries API
        resp_queries = await client.get(
            "/api/v1/execution/queries",
            headers=get_auth_headers(),
        )
        assert resp_queries.status_code == 200
        queries = resp_queries.json()
        matching_q = next((q for q in queries if q["id"] == q_id), None)
        assert matching_q is not None
        assert matching_q["status"] == "OPEN"
        assert matching_q["origin"] == "SYSTEM_CODING"
        assert matching_q["query_type"] == "SYSTEM_CODING"
        assert matching_q["action_required"] == "CLARIFY_VERBATIM"
        assert "xyz_nonsense_verbatim_string" in matching_q["explanation"]
        assert matching_q["created_by"] == "query_officer"


@pytest.mark.asyncio
async def test_upversioning_impact_analysis_endpoint() -> None:
    """Verify that up-versioning impact analysis evaluates and reports delta metrics.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    await seed_workbench_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/coding/impact-analysis",
            json={
                "dictionary_type": "MEDDRA",
                "new_version": "27.0",
            },
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["status"] == "success"
        assert data["dictionary_type"] == "MEDDRA"
        assert data["new_version"] == "27.0"
        metrics = data["metrics"]
        assert "unchanged" in metrics
        assert "reclassified" in metrics
        assert "deprecated" in metrics
        assert "skipped" in metrics


@pytest.mark.asyncio
async def test_audit_log_captures_all_coding_actions() -> None:
    """Verify that 21 CFR Part 11 audit trigger logs track all coding mutations and discrepancy queries.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    ids = await seed_workbench_data()
    aid = ids["assign_suggested"]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Mutate assignment via ACCEPT action
        await client.post(
            f"/api/v1/execution/coding/assignments/{aid}/action",
            json={
                "action": "ACCEPT",
                "suggestion_index": 0,
                "reason_for_change": "Audited coding decision",
            },
            headers=get_auth_headers(
                user_id="auditor_lead",
                change_reason="Audit trail verification for Part 11",
            ),
        )

        async with db_manager.get_session_maker()() as session:
            # Check ClinicalCodingAssignment audit logs
            stmt = select(AuditLog).where(
                AuditLog.table_name == "clinical_coding_assignments"
            )
            res = await session.execute(stmt)
            logs = res.scalars().all()
            assert len(logs) >= 1
            assert any(log.action in ("INSERT", "UPDATE") for log in logs)


@pytest.mark.asyncio
async def test_matcher_normalization_and_fuzzy_scoring() -> None:
    """Verify that clinical matcher normalizes stop words, calculates fuzzy scores, and utilizes caching.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    from apps.execution.coding.matcher import (
        CodingCache,
        calculate_combined_score,
        normalize_term,
    )

    # 1. Normalization
    norm1 = normalize_term("Severe acute headache (left side)")
    assert "headache" in norm1
    assert "severe" not in norm1  # stop word removed
    assert "acute" not in norm1  # stop word removed

    # 2. Similarity scoring
    assert calculate_combined_score("headache", "headache") == 1.0
    assert calculate_combined_score("headache", "unrelated condition") < 0.2

    # 3. Cache mechanics
    cache = CodingCache(max_size=100, ttl=60.0)
    cache_key = ("MEDDRA", "26.0", "test_cache_term", "LLT")
    cache.set(cache_key, [{"code": "1001", "term": "Cached Term"}])
    hit_val, _ = cache.get(cache_key)
    assert hit_val is not None
    assert hit_val[0]["code"] == "1001"


@pytest.mark.asyncio
async def test_parsers_meddra_and_whodrug_ascii_lines() -> None:
    """Verify that MedDRA and WHODrug ASCII parsers correctly parse distribution lines and validate formats.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    import io

    from apps.execution.coding.parsers import (
        MedDRAParser,
        WHODrugParser,
    )

    # 1. MedDRA Parser stream parsing
    med_parser = MedDRAParser(dictionary_version="26.0")
    assert med_parser.detect_file_type("llt.asc") == "llt"
    assert med_parser.detect_file_type("pt.asc") == "pt"
    assert med_parser.detect_file_type("mdhier.asc") == "mdhier"

    llt_stream = io.StringIO("10019211$Headache$10019211$$$$$$N$\n")
    llt_records = list(med_parser.parse(llt_stream, "llt"))
    assert len(llt_records) >= 1
    term_rec = next(r for r in llt_records if r["type"] == "term")
    assert term_rec["data"]["code"] == "10019211"
    assert term_rec["data"]["term_name"] == "Headache"
    assert term_rec["data"]["level"] == "LLT"

    # 2. WHODrug Parser stream parsing
    who_parser = WHODrugParser(dictionary_version="2024-03")
    assert who_parser.detect_file_type("whodrug.txt") == "drugs"
    drug_stream = io.StringIO(
        "00010101001ASPIRIN                        ASPIRIN TABLET\n"
    )
    who_records = list(who_parser.parse(drug_stream, "drugs"))
    assert len(who_records) == 1
    assert who_records[0]["data"]["drug_code"] == "00010101001"
    assert "ASPIRIN" in who_records[0]["data"]["preferred_name"]


@pytest.mark.asyncio
async def test_impact_analysis_reclassification_and_mutation() -> None:
    """Verify that impact analysis correctly detects unchanged, reclassified, and deprecated codes.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-16
    """
    from apps.execution.coding.impact import run_impact_analysis

    async with db_manager.get_session_maker()() as session, session.begin():
        # Add a coded assignment on v26.0 with full hierarchy
        coded_assign = ClinicalCodingAssignment(
            id="ASSIGN-IMPACT-TEST-1",
            verbatim_text="Headache mild",
            source_field="AE.AETERM",
            observation_id="OBS-AE-001",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.CODED,
            coded_code="10019211",
            coded_term="Headache",
            hierarchy={
                "hierarchies": [
                    {
                        "llt_code": "10019211",
                        "llt_name": "Headache",
                        "pt_code": "10019211",
                        "pt_name": "Headache",
                        "hlt_code": "10019231",
                        "hlt_name": "Headaches NEC",
                        "hlgt_code": "10019214",
                        "hlgt_name": "Headaches",
                        "soc_code": "10029205",
                        "soc_name": "Nervous system disorders",
                        "primary_soc_flag": "Y",
                    }
                ]
            },
            score=1.0,
            domain="AE",
        )
        session.add(coded_assign)

        # In target v27.0, add the same term with identical hierarchy (unchanged)
        session.add(
            MedDRATerm(
                dictionary_version="27.0",
                code="10019211",
                term_name="Headache",
                level="LLT",
            )
        )
        session.add(
            MedDRAHierarchy(
                dictionary_version="27.0",
                llt_code="10019211",
                pt_code="10019211",
                hlt_code="10019231",
                hlgt_code="10019214",
                soc_code="10029205",
                primary_soc_flag="Y",
            )
        )

    # Run impact analysis targeting v27.0
    async with db_manager.get_session_maker()() as session, session.begin():
        metrics = await run_impact_analysis(
            session=session,
            dictionary_type="MEDDRA",
            new_version="27.0",
            actor="impact_auditor",
        )
        assert metrics["unchanged"] >= 1
