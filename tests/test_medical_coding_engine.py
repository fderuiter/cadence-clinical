import asyncio
import hashlib
import hmac
import io
import json
import os
import time
import zipfile
from typing import AsyncGenerator
from unittest.mock import patch

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select, text

from apps.execution.coding import (
    CodingCache,
    coding_cache,
    match_verbatim_term,
    normalize_term,
)
from apps.execution.coding.parsers import (
    MedDRAParseError,
    MedDRAParser,
    WHODrugParseError,
    WHODrugParser,
)
from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalSubject,
    CodingState,
    MedDRAHierarchy,
    MedDRATerm,
    WHODrugRecord,
)
from apps.execution.main import app as exec_app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id: str = "coder_bob",
    roles: str = "Data Manager",
    change_reason: str = "Clinical coding review",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers."""
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
async def setup_db() -> AsyncGenerator[None, None]:
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
    coding_cache.clear()
    TrialLockManager.reset()


async def seed_dictionary_data():
    """Seed dictionaries in the database for isolation and testing."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # MedDRA Version 25.0
            session.add(
                MedDRATerm(
                    dictionary_version="25.0",
                    code="10019211",
                    term_name="Headache",
                    level="LLT",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="25.0",
                    llt_code="10019211",
                    pt_code="10019211",
                    hlt_code="10019231",
                    hlgt_code="10029214",
                    soc_code="10029205",
                    primary_soc_flag="Y",
                )
            )

            # MedDRA Version 26.0 (isolation check)
            session.add(
                MedDRATerm(
                    dictionary_version="26.0",
                    code="10019211",
                    term_name="Severe Headache",
                    level="LLT",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="26.0",
                    llt_code="10019211",
                    pt_code="10019211",
                    hlt_code="10019231",
                    hlgt_code="10029214",
                    soc_code="10029205",
                    primary_soc_flag="Y",
                )
            )

            # WHODrug Version 2023-03
            session.add(
                WHODrugRecord(
                    dictionary_version="2023-03",
                    drug_code="00010101001",
                    preferred_name="ASPIRIN",
                    drug_name="ASPIRIN TABLET v23",
                )
            )

            # WHODrug Version 2024-03
            session.add(
                WHODrugRecord(
                    dictionary_version="2024-03",
                    drug_code="00010101001",
                    preferred_name="ASPIRIN",
                    drug_name="ASPIRIN TABLET v24",
                )
            )

            # Clinical Subject
            session.add(
                ClinicalSubject(
                    id="SUBJ-UUID-1",
                    subject_id="SUBJ-001",
                    study_id="STUDY-001",
                )
            )


# =========================================================================
# 1. PARSER FIXTURES
# =========================================================================


def test_parser_fixtures() -> None:
    """Verify parsing a valid llt.asc and DD.txt stream, and that parse errors are appropriately caught."""
    # MedDRA Parser
    med_lines = ["10019211$Headache$10019211$$$$$Y$\n"]
    med_parser = MedDRAParser(dictionary_version="26.0")
    records = list(med_parser.parse(med_lines, file_type="llt", file_name="llt.asc"))
    assert len(records) == 2
    assert records[0]["type"] == "term"
    assert records[0]["data"]["term_name"] == "Headache"

    # MedDRA Parser failure
    bad_med_lines = ["123$BadCode$10019211$$$$$Y$\n"]
    with pytest.raises(MedDRAParseError) as exc_info:
        list(med_parser.parse(bad_med_lines, file_type="llt", file_name="llt.asc"))
    assert exc_info.value.line_num == 1
    assert "llt_code must be an 8-digit numeric string" in exc_info.value.message

    # WHODrug Parser
    who_lines = ["00010101001ASPIRIN                        ASPIRIN TABLET\n"]
    who_parser = WHODrugParser(dictionary_version="2024-03")
    who_records = list(
        who_parser.parse(who_lines, file_type="drugs", file_name="DD.txt")
    )
    assert len(who_records) == 1
    assert who_records[0]["type"] == "drug_record"
    assert who_records[0]["data"]["preferred_name"] == "ASPIRIN"

    # WHODrug Parser failure
    bad_who_lines = ["           ASPIRIN                        ASPIRIN TABLET\n"]
    with pytest.raises(WHODrugParseError) as who_exc:
        list(who_parser.parse(bad_who_lines, file_type="drugs", file_name="DD.txt"))
    assert who_exc.value.line_num == 1
    assert "drug_code must not be empty" in who_exc.value.message


# =========================================================================
# 2. IMPORT AUTHORIZATION AND JOB STATUS
# =========================================================================


@pytest.mark.asyncio
async def test_import_auth_and_job_status() -> None:
    """Verify role-gating for imports and polling job status functionality."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Create a mock zip
        zip_buffer = io.BytesIO()
        with zipfile.ZipFile(zip_buffer, "a", zipfile.ZIP_DEFLATED, False) as zip_file:
            zip_file.writestr("llt.asc", "10019211$Headache$10019211$$$$$Y$\n")
            zip_file.writestr("pt.asc", "10019211$Headache$10019211$$$$$\n")
        zip_buffer.seek(0)

        # 1. Unauthorized role (CRA) -> Forbidden 403
        unauth_headers = get_auth_headers(
            roles="CRA", change_reason="Unpermitted import"
        )
        resp_fail = await client.post(
            "/api/v1/dictionaries/import",
            data={"dictionary_type": "MEDDRA", "version": "26.0"},
            files={"files": ("meddra.zip", zip_buffer, "application/zip")},
            headers=unauth_headers,
        )
        assert resp_fail.status_code == 403

        # Reset buffer
        zip_buffer.seek(0)

        # 2. Authorized role (TERMINOLOGY_MANAGER) -> Accepted 202
        auth_headers = get_auth_headers(
            roles="TERMINOLOGY_MANAGER", change_reason="Permitted import"
        )
        resp_ok = await client.post(
            "/api/v1/dictionaries/import",
            data={"dictionary_type": "MEDDRA", "version": "26.0"},
            files={"files": ("meddra.zip", zip_buffer, "application/zip")},
            headers=auth_headers,
        )
        assert resp_ok.status_code == 202
        job_info = resp_ok.json()
        assert job_info["job_id"] is not None
        assert job_info["status"] == "PENDING"

        # 3. Poll GET /api/v1/dictionaries/jobs/{job_id}
        job_id = job_info["job_id"]
        completed = False
        for _ in range(50):
            status_resp = await client.get(
                f"/api/v1/dictionaries/jobs/{job_id}",
                headers=auth_headers,
            )
            assert status_resp.status_code == 200
            status_info = status_resp.json()
            if status_info["status"] == "COMPLETED":
                completed = True
                assert status_info["records_imported"] > 0
                break
            elif status_info["status"] == "FAILED":
                pytest.fail(f"Job failed: {status_info}")
            await asyncio.sleep(0.05)

        assert completed


# =========================================================================
# 3. MATCHER NORMALIZATION AND SCORING THRESHOLDS
# =========================================================================


def test_matcher_normalization_and_scoring_thresholds() -> None:
    """Verify normalization and token scoring rules."""
    # Normalization checks
    assert normalize_term("Mild headache") == "headache"
    assert normalize_term("Severe recurring pain, chronic") == "pain"

    from apps.execution.coding.matcher import calculate_combined_score

    # "cough symptom" vs "cough" (moderate match)
    score = calculate_combined_score("cough symptom", "cough")
    assert 0.50 <= score <= 0.65


# =========================================================================
# 4. CACHE BEHAVIOR
# =========================================================================


@pytest.mark.asyncio
async def test_cache_behavior_and_degradation() -> None:
    """Verify cache setting, hits, misses, TTL, and stale-on-error graceful degradation."""
    # Instantiate short TTL cache
    cache = CodingCache(ttl=0.05)
    key = ("MEDDRA", "26.0", "migraine", "LLT")
    val = {"status": "AUTO-CODED", "match": "Migraine"}

    # Initially empty
    hit, expired = cache.get(key)
    assert hit is None
    assert expired is None

    # Set and immediate hit
    cache.set(key, val)
    hit, expired = cache.get(key)
    assert hit == val
    assert expired is None

    # Expire after TTL sleep
    await asyncio.sleep(0.06)
    hit, expired = cache.get(key)
    assert hit is None
    assert expired == val  # Returns expired as stale fallback candidate

    # Test stale-on-error fallback on matcher
    async with db_manager.get_session_maker()() as session:
        # Mock database failure during matching
        with patch("apps.execution.coding.matcher.coding_cache", cache):
            with patch(
                "apps.execution.coding.matcher._match_meddra",
                side_effect=Exception("Database connection timed out"),
            ):
                # When DB fails, it falls back to the stale/expired cache value gracefully
                res = await match_verbatim_term(
                    session, "migraine", "MEDDRA", "26.0", target_level="LLT"
                )
                assert res == val


# =========================================================================
# 5. MedDRA AND WHODrug LOOKUP ENDPOINTS
# =========================================================================


@pytest.mark.asyncio
async def test_lookups_endpoints() -> None:
    """Verify that lookup GET endpoints work and return validation failures for bad inputs."""
    await seed_dictionary_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")

        # 1. Successful MedDRA lookup
        resp = await client.get(
            "/api/v1/dictionaries/meddra/code",
            params={"term": "headache", "version": "25.0", "target_level": "LLT"},
            headers=headers,
        )
        assert resp.status_code == 200
        assert resp.json()["status"] == "AUTO-CODED"

        # 2. Blank term MedDRA -> 400
        resp_bad1 = await client.get(
            "/api/v1/dictionaries/meddra/code",
            params={"term": "   ", "version": "25.0"},
            headers=headers,
        )
        assert resp_bad1.status_code == 400
        assert "Term must be a non-empty string" in resp_bad1.json()["detail"]

        # 3. Successful WHODrug lookup
        resp_who = await client.get(
            "/api/v1/dictionaries/whodrug/code",
            params={"term": "aspirin", "version": "2024-03"},
            headers=headers,
        )
        assert resp_who.status_code == 200
        assert resp_who.json()["status"] == "AUTO-CODED"


# =========================================================================
# 6. CODING TRANSITIONS
# =========================================================================


@pytest.mark.asyncio
async def test_coding_transitions() -> None:
    """Verify coding assignment status transition properties and roles."""
    await seed_dictionary_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Create suggestion-prone observation
        # "Mild Aspirin Tablet" -> stop words Mild, Tablet removed -> "aspirin" -> WHODrug 2024-03 contains preferred name ASPIRIN and drug name ASPIRIN TABLET.
        resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "CM",
                "test_code": "CMTRT",
                "test_name": "Prior Medication Verbatim",
                "value_string": "Aspirin Pain",  # Not exact -> yields SUGGESTED
            },
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200

        # Fetch assignment
        resp_list = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        assert resp_list.status_code == 200
        assignments = resp_list.json()
        assert len(assignments) == 1
        assign = assignments[0]
        assert assign["status"] == "SUGGESTED"


# =========================================================================
# 7. OVERRIDE REASON VALIDATION
# =========================================================================


@pytest.mark.asyncio
async def test_override_reason_validation() -> None:
    """Verify manual override validations: must have non-empty reason and exist in dictionary."""
    await seed_dictionary_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Post CM observation
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "CM",
                "test_code": "CMTRT",
                "test_name": "Prior Medication Verbatim",
                "value_string": "Aspirin Pain",
            },
            headers=get_auth_headers(),
        )

        resp_list = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        assign_id = resp_list.json()[0]["id"]

        # 1. Missing override code/term or invalid code -> 400
        resp_bad_code = await client.post(
            f"/api/v1/execution/coding/assignments/{assign_id}/action",
            json={
                "action": "OVERRIDE",
                "code": "NONEXISTENT",
                "term": "Fake Aspirin",
                "reason_for_change": "Because I say so",
            },
            headers=get_auth_headers(),
        )
        assert resp_bad_code.status_code == 400

        # 2. Missing reason_for_change -> 422 (or 400 based on validation rules)
        resp_no_reason = await client.post(
            f"/api/v1/execution/coding/assignments/{assign_id}/action",
            json={
                "action": "OVERRIDE",
                "code": "00010101001",
                "term": "ASPIRIN",
                "reason_for_change": "   ",  # blank
            },
            headers=get_auth_headers(),
        )
        assert resp_no_reason.status_code in (400, 422)


# =========================================================================
# 8. UNCODABLE QUERY GENERATION
# =========================================================================


@pytest.mark.asyncio
async def test_uncodable_query_generation_and_pii_isolation() -> None:
    """Verify that posting completely uncodable text raises SYSTEM_CODING clinical query and isolates PII."""
    await seed_dictionary_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Post uncodable AE verbatim
        resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "completely_gibberish_uncodable_text",
            },
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200

        # Verify QUERY_PENDING status
        resp_list = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        assigns = resp_list.json()
        assert any(a["status"] == "QUERY_PENDING" for a in assigns)

        # Retrieve queries
        resp_q = await client.get(
            "/api/v1/execution/queries", headers=get_auth_headers()
        )
        assert resp_q.status_code == 200
        queries = resp_q.json()
        assert len(queries) >= 1
        q = queries[0]
        assert q["status"] == "OPEN"
        assert q["origin"] == "SYSTEM_CODING"
        assert q["query_type"] == "SYSTEM_CODING"
        assert q["action_required"] == "RE-ENTER_VERBATIM"

        # Verify coordinate matching is present and PII is isolated
        assert "AETERM" in q["explanation"]
        assert "completely_gibberish_uncodable_text" in q["explanation"]
        # Patient ID/PII must not leak into query explanation
        assert "SUBJ-001" not in q["explanation"]


# =========================================================================
# 9. UP-VERSIONING LEDGER OUTCOMES
# =========================================================================


@pytest.mark.asyncio
async def test_upversioning_ledger_outcomes() -> None:
    """Verify up-versioning compares assignments against new versions and logs to ClinicalCodingLedger."""
    # Let's seed direct records
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Old MedDRA (25.0)
            session.add(
                MedDRATerm(
                    dictionary_version="25.0",
                    code="M10",
                    term_name="Headache",
                    level="LLT",
                )
            )
            session.add(
                MedDRATerm(
                    dictionary_version="25.0",
                    code="M20",
                    term_name="Nausea",
                    level="LLT",
                )
            )
            session.add(
                MedDRATerm(
                    dictionary_version="25.0",
                    code="M30",
                    term_name="Fatigue",
                    level="LLT",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="25.0",
                    llt_code="M10",
                    pt_code="M10",
                    hlt_code="H10",
                    hlgt_code="HG10",
                    soc_code="S10",
                    primary_soc_flag="Y",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="25.0",
                    llt_code="M20",
                    pt_code="M20",
                    hlt_code="H20",
                    hlgt_code="HG20",
                    soc_code="S20",
                    primary_soc_flag="Y",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="25.0",
                    llt_code="M30",
                    pt_code="M30",
                    hlt_code="H30",
                    hlgt_code="HG30",
                    soc_code="S30",
                    primary_soc_flag="Y",
                )
            )

            # New MedDRA (26.0)
            # M10 is unchanged
            session.add(
                MedDRATerm(
                    dictionary_version="26.0",
                    code="M10",
                    term_name="Headache",
                    level="LLT",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="26.0",
                    llt_code="M10",
                    pt_code="M10",
                    hlt_code="H10",
                    hlgt_code="HG10",
                    soc_code="S10",
                    primary_soc_flag="Y",
                )
            )
            # M20 is reclassified (different hlt_code)
            session.add(
                MedDRATerm(
                    dictionary_version="26.0",
                    code="M20",
                    term_name="Nausea",
                    level="LLT",
                )
            )
            session.add(
                MedDRAHierarchy(
                    dictionary_version="26.0",
                    llt_code="M20",
                    pt_code="M20",
                    hlt_code="H20_NEW",
                    hlgt_code="HG20",
                    soc_code="S20",
                    primary_soc_flag="Y",
                )
            )
            # M30 is deprecated (doesn't exist in 26.0)

            session.add(
                ClinicalSubject(
                    id="SUBJ-UUID-UP",
                    subject_id="SUBJ-UP",
                    study_id="STUDY-001",
                )
            )

            # Assignments (25.0)
            session.add(
                ClinicalCodingAssignment(
                    id="A-M10",
                    verbatim_text="Headache",
                    source_field="AE.AETERM",
                    dictionary_type="MEDDRA",
                    dictionary_version="25.0",
                    coded_code="M10",
                    coded_term="Headache",
                    status=CodingState.CODED,
                    hierarchy={
                        "hierarchies": [
                            {
                                "llt_code": "M10",
                                "pt_code": "M10",
                                "hlt_code": "H10",
                                "hlgt_code": "HG10",
                                "soc_code": "S10",
                                "primary_soc_flag": "Y",
                            }
                        ]
                    },
                )
            )
            session.add(
                ClinicalCodingAssignment(
                    id="A-M20",
                    verbatim_text="Nausea",
                    source_field="AE.AETERM",
                    dictionary_type="MEDDRA",
                    dictionary_version="25.0",
                    coded_code="M20",
                    coded_term="Nausea",
                    status=CodingState.CODED,
                    hierarchy={
                        "hierarchies": [
                            {
                                "llt_code": "M20",
                                "pt_code": "M20",
                                "hlt_code": "H20",
                                "hlgt_code": "HG20",
                                "soc_code": "S20",
                                "primary_soc_flag": "Y",
                            }
                        ]
                    },
                )
            )
            session.add(
                ClinicalCodingAssignment(
                    id="A-M30",
                    verbatim_text="Fatigue",
                    source_field="AE.AETERM",
                    dictionary_type="MEDDRA",
                    dictionary_version="25.0",
                    coded_code="M30",
                    coded_term="Fatigue",
                    status=CodingState.CODED,
                    hierarchy={
                        "hierarchies": [
                            {
                                "llt_code": "M30",
                                "pt_code": "M30",
                                "hlt_code": "H30",
                                "hlgt_code": "HG30",
                                "soc_code": "S30",
                                "primary_soc_flag": "Y",
                            }
                        ]
                    },
                )
            )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Run MedDRA Impact Analysis
        resp_meddra = await client.post(
            "/api/v1/execution/coding/impact-analysis",
            json={"dictionary_type": "MEDDRA", "new_version": "26.0"},
            headers=get_auth_headers(),
        )
        assert resp_meddra.status_code == 200
        data_m = resp_meddra.json()
        assert data_m["status"] == "success"
        metrics_m = data_m["metrics"]
        assert metrics_m["unchanged"] == 1  # M10
        assert metrics_m["reclassified"] == 1  # M20
        assert metrics_m["deprecated"] == 1  # M30

        # Assert ledger outcomes exist
        async with db_manager.get_session_maker()() as session:
            stmt_ledger = select(ClinicalCodingLedger).where(
                ClinicalCodingLedger.new_dictionary_version == "26.0"
            )
            res_ledger = await session.execute(stmt_ledger)
            ledgers = list(res_ledger.scalars().all())
            assert len(ledgers) == 3


# =========================================================================
# 10. DICTIONARY VERSION ISOLATION
# =========================================================================


@pytest.mark.asyncio
async def test_dictionary_version_isolation() -> None:
    """Verify that lookup and assignments are strictly isolated by version."""
    await seed_dictionary_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # 1. Look up Headache in version 25.0
        resp25 = await client.get(
            "/api/v1/dictionaries/meddra/code",
            params={"term": "headache", "version": "25.0", "target_level": "LLT"},
            headers=headers,
        )
        assert resp25.status_code == 200
        assert resp25.json()["matches"][0]["llt_name"] == "Headache"

        # 2. Look up Headache in version 26.0 (returns Severe Headache)
        resp26 = await client.get(
            "/api/v1/dictionaries/meddra/code",
            params={"term": "headache", "version": "26.0", "target_level": "LLT"},
            headers=headers,
        )
        assert resp26.status_code == 200
        assert resp26.json()["matches"][0]["llt_name"] == "Severe Headache"


# =========================================================================
# 11. AUDIT-RELEVANT WORKFLOWS
# =========================================================================


@pytest.mark.asyncio
async def test_audit_relevant_workflows() -> None:
    """Verify that coding assignment mutations write to the system AuditLog correctly."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            assignment = ClinicalCodingAssignment(
                verbatim_text="headache symptom",
                source_field="AE.AETERM",
                observation_id="obs_999",
                dictionary_type="MEDDRA",
                dictionary_version="26.0",
                coded_code="10019211",
                coded_term="Headache",
                status="CODED",
            )
            session.add(assignment)

    # Confirm audit log records INSERT
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            res = await session.execute(
                select(AuditLog).where(
                    AuditLog.table_name == "clinical_coding_assignments"
                )
            )
            logs = res.scalars().all()
            assert len(logs) >= 1
            assert any(lg.action == "INSERT" for lg in logs)
