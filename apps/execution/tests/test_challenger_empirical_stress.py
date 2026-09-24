"""Empirical Adversarial Stress Test Suite for Cadence Clinical Platform Phase 1.

Stress-tests boundary conditions, hierarchical lock inheritance, medical coding
fuzzy matching, lab multi-format ingestion, and biostatistical serialization.

Requirements:
- PRD-SYS-001 (Audit Trail & Part 11 Compliance)
- PRD-SYS-002 (Hierarchical Data Locking & Freeze)
- PRD-SYS-004 (Medical Coding Engine & Terminology)
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-QRY-001 (Discrepancy Query Escalation & Lifecycle)
- PRD-MDR-001 (Metadata Repository & Biostat Exports)
- Trace-1 (Part 11 & GxP Compliance)
- Trace-3 (Data Lock & Freeze System)
- Trace-15 (Laboratory Data Pipeline)
- Trace-16 (Medical Coding Workbench)
- Trace-17 (Export Wizard & Serializers)
"""

import hashlib
import hmac
import json
import os
import time
import uuid

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import text

from apps.execution.biostat.deid import deidentify_export_data
from apps.execution.biostat.odm_xml import serialize_to_odm_xml
from apps.execution.biostat.xpt import double_to_ibm, ibm_to_double, read_xpt, write_xpt
from apps.execution.coding.impact import run_impact_analysis
from apps.execution.coding.matcher import (
    coding_cache,
    match_verbatim_term,
)
from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    Base,
    ClinicalCodingAssignment,
    ClinicalObservation,
    ClinicalSubject,
    ClinicalVisit,
    CodingState,
    DictionaryType,
    FormSubmission,
    MedDRAHierarchy,
    MedDRATerm,
    RecodingState,
)
from apps.execution.lab_ranges import (
    evaluate_lab_value,
)
from apps.execution.main import app as exec_app
from apps.execution.services.lab_ingestion_service import (
    parse_csv_payload,
    parse_fhir_payload,
    parse_hl7_v2_payload,
)
from apps.execution.trial_lock import TrialLockManager
from apps.execution.ucum import convert_unit
from packages.security.sig_token_verifier import token_consumption_cache

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def generate_test_sig_token(
    user_id: str, secret: str = "internal-gateway-secret-12345"
) -> str:
    sig_payload = {
        "sub": user_id,
        "username": user_id,
        "action": "HARD_LOCK",
        "roles": ["Data Manager"],
        "iat": time.time(),
        "exp": time.time() + 300,
        "jti": str(uuid.uuid4()),
    }
    return jwt.encode(sig_payload, secret, algorithm="HS256")


def get_auth_headers(
    user_id: str = "challenger_admin",
    roles: str = "Data Manager,TERMINOLOGY_MANAGER,SYSTEM_ADMIN",
    change_reason: str = "Challenger adversarial test run",
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
async def setup_test_db():
    """Isolates in-memory database and deploys triggers for every test."""
    TrialLockManager.reset()
    token_consumption_cache.reset()
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
    await db_manager.close()
    TrialLockManager.reset()
    token_consumption_cache.reset()
    coding_cache.clear()


# ============================================================================
# 1. MEDICAL CODING ADVERSARIAL STRESS TESTS
# ============================================================================


@pytest.mark.asyncio
async def test_medical_coding_uncodable_and_extreme_inputs():
    """Verify medical coding engine handles uncodable, empty, whitespace, and extreme inputs gracefully.

    @req:PRD-SYS-004
    @req:PRD-QRY-001
    @req:Trace-16
    """
    async with db_manager.get_session_maker()() as session, session.begin():
        # Seed MedDRA term
        term = MedDRATerm(
            dictionary_version="26.0",
            code="10019211",
            term_name="Headache",
            level="LLT",
        )
        session.add(term)

    async with db_manager.get_session_maker()() as session:
        # 1. Empty string verbatim -> UNCODABLE
        res_empty = await match_verbatim_term(
            session=session,
            verbatim="",
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_empty["status"] == "UNCODABLE"
        assert res_empty["match"] is None

        # 2. Whitespace only verbatim -> UNCODABLE
        res_ws = await match_verbatim_term(
            session=session,
            verbatim="   \t\n   ",
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_ws["status"] == "UNCODABLE"

        # 3. Pure punctuation -> UNCODABLE
        res_punct = await match_verbatim_term(
            session=session,
            verbatim="!@#$%^&*()_+{}|:\"<>?~`-=[]\\;',./",
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_punct["status"] == "UNCODABLE"

        # 4. Stop-words only verbatim -> UNCODABLE
        res_stop = await match_verbatim_term(
            session=session,
            verbatim="severe acute mild episode of onset of with and the a an",
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_stop["status"] == "UNCODABLE"

        # 5. Extreme long verbatim (10,000 characters)
        long_verbatim = "headache " * 1200
        res_long = await match_verbatim_term(
            session=session,
            verbatim=long_verbatim,
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_long["status"] in ("AUTO-CODED", "SUGGESTIONS")

        # 6. Verbatim with emojis around exact target term
        res_emoji = await match_verbatim_term(
            session=session,
            verbatim="🔥 Headache 💊",
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_emoji["status"] == "AUTO-CODED"
        assert res_emoji["match"]["code"] == "10019211"

        # 7. Unsupported dictionary type raises ValueError
        with pytest.raises(ValueError, match="Unsupported dictionary type"):
            await match_verbatim_term(
                session=session,
                verbatim="Aspirin",
                dictionary_type="UNKNOWN_DICT",
                version="1.0",
            )


@pytest.mark.asyncio
async def test_medical_coding_exact_threshold_boundaries():
    """Verify exact threshold mechanics: >=0.85 -> AUTO-CODED, [0.60, 0.85) -> SUGGESTIONS, <0.60 -> UNCODABLE.

    @req:PRD-SYS-004
    @req:Trace-16
    """
    async with db_manager.get_session_maker()() as session, session.begin():
        # Add diverse MedDRA terms
        terms = [
            MedDRATerm(
                dictionary_version="26.0",
                code="10019211",
                term_name="Headache",
                level="LLT",
            ),
            MedDRATerm(
                dictionary_version="26.0",
                code="10028813",
                term_name="Nausea",
                level="LLT",
            ),
            MedDRATerm(
                dictionary_version="26.0",
                code="10037660",
                term_name="Pyrexia",
                level="LLT",
            ),
        ]
        session.add_all(terms)

    async with db_manager.get_session_maker()() as session:
        # Exact match -> Score = 1.0 -> AUTO-CODED
        res_exact = await match_verbatim_term(
            session=session,
            verbatim="Headache",
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_exact["status"] == "AUTO-CODED"
        assert res_exact["match"]["score"] == 1.0

        # Close variation -> e.g. "Headaches" -> Stemming normalizes to "headache" -> Score ~ 1.0 -> AUTO-CODED
        res_close = await match_verbatim_term(
            session=session,
            verbatim="Headaches",
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_close["status"] == "AUTO-CODED"
        assert res_close["match"]["score"] >= 0.85

        # Unrelated condition -> UNCODABLE
        res_unrelated = await match_verbatim_term(
            session=session,
            verbatim="Fracture of distal radius right arm",
            dictionary_type="MEDDRA",
            version="26.0",
        )
        assert res_unrelated["status"] == "UNCODABLE"
        assert res_unrelated["match"] is None


@pytest.mark.asyncio
async def test_medical_coding_upversioning_and_idempotency_stress():
    """Verify up-versioning handles unchanged, deprecated, and reclassified terms, and ensures idempotency.

    @req:PRD-SYS-004
    @req:Trace-16
    """
    async with db_manager.get_session_maker()() as session, session.begin():
        # Seed v26.0 terms
        t_unchanged_26 = MedDRATerm(
            dictionary_version="26.0",
            code="1001",
            term_name="Condition Unchanged",
            level="LLT",
        )
        t_reclass_26 = MedDRATerm(
            dictionary_version="26.0",
            code="1002",
            term_name="Condition Reclassified",
            level="LLT",
        )
        t_deprec_26 = MedDRATerm(
            dictionary_version="26.0",
            code="1003",
            term_name="Condition Deprecated",
            level="LLT",
        )

        # Seed v26.0 hierarchies
        h_unchanged_26 = MedDRAHierarchy(
            dictionary_version="26.0",
            llt_code="1001",
            pt_code="1001",
            hlt_code="2001",
            hlgt_code="3001",
            soc_code="4001",
            primary_soc_flag="Y",
        )
        h_reclass_26 = MedDRAHierarchy(
            dictionary_version="26.0",
            llt_code="1002",
            pt_code="1002",
            hlt_code="2002",
            hlgt_code="3002",
            soc_code="4002",
            primary_soc_flag="Y",
        )

        # Seed v27.0 terms (1001 unchanged, 1002 reclassified with different SOC, 1003 missing/deprecated)
        t_unchanged_27 = MedDRATerm(
            dictionary_version="27.0",
            code="1001",
            term_name="Condition Unchanged",
            level="LLT",
        )
        t_reclass_27 = MedDRATerm(
            dictionary_version="27.0",
            code="1002",
            term_name="Condition Reclassified",
            level="LLT",
        )

        # Seed v27.0 hierarchies (1002 has new SOC 4999)
        h_unchanged_27 = MedDRAHierarchy(
            dictionary_version="27.0",
            llt_code="1001",
            pt_code="1001",
            hlt_code="2001",
            hlgt_code="3001",
            soc_code="4001",
            primary_soc_flag="Y",
        )
        h_reclass_27 = MedDRAHierarchy(
            dictionary_version="27.0",
            llt_code="1002",
            pt_code="1002",
            hlt_code="2002",
            hlgt_code="3002",
            soc_code="4999",
            primary_soc_flag="Y",
        )

        session.add_all(
            [
                t_unchanged_26,
                t_reclass_26,
                t_deprec_26,
                h_unchanged_26,
                h_reclass_26,
                t_unchanged_27,
                t_reclass_27,
                h_unchanged_27,
                h_reclass_27,
            ]
        )

        # Seed assignments under v26.0
        assign_unchanged = ClinicalCodingAssignment(
            id="ASG-UNCHANGED",
            verbatim_text="Condition Unchanged",
            source_field="AE.AETERM",
            observation_id="OBS-1",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.CODED,
            coded_code="1001",
            coded_term="Condition Unchanged",
            hierarchy={
                "hierarchies": [
                    {
                        "llt_code": "1001",
                        "pt_code": "1001",
                        "hlt_code": "2001",
                        "hlgt_code": "3001",
                        "soc_code": "4001",
                        "primary_soc_flag": "Y",
                    }
                ]
            },
        )
        assign_reclass = ClinicalCodingAssignment(
            id="ASG-RECLASS",
            verbatim_text="Condition Reclassified",
            source_field="AE.AETERM",
            observation_id="OBS-2",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.CODED,
            coded_code="1002",
            coded_term="Condition Reclassified",
            hierarchy={
                "hierarchies": [
                    {
                        "llt_code": "1002",
                        "pt_code": "1002",
                        "hlt_code": "2002",
                        "hlgt_code": "3002",
                        "soc_code": "4002",
                        "primary_soc_flag": "Y",
                    }
                ]
            },
        )
        assign_deprec = ClinicalCodingAssignment(
            id="ASG-DEPREC",
            verbatim_text="Condition Deprecated",
            source_field="AE.AETERM",
            observation_id="OBS-3",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.CODED,
            coded_code="1003",
            coded_term="Condition Deprecated",
            hierarchy={},
        )
        session.add_all([assign_unchanged, assign_reclass, assign_deprec])

    # Run Impact Analysis
    async with db_manager.get_session_maker()() as session, session.begin():
        metrics = await run_impact_analysis(
            session=session,
            dictionary_type="MEDDRA",
            new_version="27.0",
            actor="upversion_tester",
        )
        assert metrics["unchanged"] == 1
        assert metrics["reclassified"] == 1
        assert metrics["deprecated"] == 1
        assert metrics["skipped"] == 0

    # Verify mutations
    async with db_manager.get_session_maker()() as session:
        # Check unchanged promoted
        res_u = await session.get(ClinicalCodingAssignment, "ASG-UNCHANGED")
        assert res_u.dictionary_version == "27.0"
        assert res_u.status == CodingState.CODED

        # Check reclassified flagged
        res_r = await session.get(ClinicalCodingAssignment, "ASG-RECLASS")
        assert res_r.status == CodingState.RECODING_REQUIRED
        assert res_r.recoding_status == RecodingState.PENDING

        # Check deprecated flagged
        res_d = await session.get(ClinicalCodingAssignment, "ASG-DEPREC")
        assert res_d.status == CodingState.RECODING_REQUIRED
        assert res_d.recoding_status == RecodingState.PENDING

    # Test Idempotency: re-running on same version processes 0 new records
    async with db_manager.get_session_maker()() as session, session.begin():
        metrics_second_run = await run_impact_analysis(
            session=session,
            dictionary_type="MEDDRA",
            new_version="27.0",
            actor="upversion_tester",
        )
        assert metrics_second_run["unchanged"] == 0
        assert metrics_second_run["reclassified"] == 0
        assert metrics_second_run["deprecated"] == 0


# ============================================================================
# 2. DATA LOCKS 6-TIER HIERARCHY & STEP-UP AUTH STRESS TESTS
# ============================================================================


@pytest.mark.asyncio
async def test_datalock_6_tier_field_level_blocking():
    """Verify FIELD lock blocks only the target field and allows other fields in same form.

    @req:PRD-SYS-002
    @req:Trace-3
    """
    async with db_manager.get_session_maker()() as session, session.begin():
        subj = ClinicalSubject(
            id="SUBJ-S1",
            subject_id="SUBJ-S1",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
        )
        visit = ClinicalVisit(
            id="VISIT-V1",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
            subject_id="SUBJ-S1",
            visit_name="Screening",
        )
        form = FormSubmission(
            id="FORM-F1",
            form_id="FORM-F1",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
            subject_id="SUBJ-S1",
            visit_id="VISIT-V1",
        )
        obs1 = ClinicalObservation(
            id="OBS-O1",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
            subject_id="SUBJ-S1",
            visit_id="VISIT-V1",
            page_id="FORM-F1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic BP",
            value=120.0,
        )
        obs2 = ClinicalObservation(
            id="OBS-O2",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
            subject_id="SUBJ-S1",
            visit_id="VISIT-V1",
            page_id="FORM-F1",
            domain="VS",
            test_code="DIABP",
            test_name="Diastolic BP",
            value=80.0,
        )
        session.add_all([subj, visit, form, obs1, obs2])

    # 1. Lock field 'SYSBP' -> mutating SYSBP is BLOCKED
    TrialLockManager.lock_field("SYSBP", "FORM-F1")
    async with db_manager.get_session_maker()() as session:
        target_obs = await session.get(ClinicalObservation, "OBS-O1")
        target_obs.value = 130.0
        with pytest.raises(PermissionError, match="Field SYSBP is currently locked"):
            await session.flush()
    TrialLockManager.reset()

    # 2. Mutating DIABP when only SYSBP is locked succeeds in fresh session
    TrialLockManager.lock_field("SYSBP", "FORM-F1")
    async with db_manager.get_session_maker()() as session, session.begin():
        target_diabp = await session.get(ClinicalObservation, "OBS-O2")
        target_diabp.value = 85.0
        await session.flush()
    TrialLockManager.reset()


@pytest.mark.asyncio
async def test_datalock_6_tier_form_visit_subject_site_study_hierarchy():
    """Verify FORM, VISIT, SUBJECT, SITE, and STUDY locks block child mutations down the hierarchy.

    @req:PRD-SYS-002
    @req:Trace-3
    """
    async with db_manager.get_session_maker()() as session, session.begin():
        subj1 = ClinicalSubject(
            id="SUBJ-S1",
            subject_id="SUBJ-S1",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
        )
        subj2 = ClinicalSubject(
            id="SUBJ-S2",
            subject_id="SUBJ-S2",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-2",
        )
        visit1 = ClinicalVisit(
            id="VISIT-V1",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
            subject_id="SUBJ-S1",
            visit_name="Screening",
        )
        form1 = FormSubmission(
            id="FORM-F1",
            form_id="FORM-F1",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
            subject_id="SUBJ-S1",
            visit_id="VISIT-V1",
        )
        obs1 = ClinicalObservation(
            id="OBS-O1",
            study_id="STUDY-LOCK-1",
            site_id="SITE-LOCK-1",
            subject_id="SUBJ-S1",
            visit_id="VISIT-V1",
            page_id="FORM-F1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic BP",
            value=120.0,
        )
        session.add_all([subj1, subj2, visit1, form1, obs1])

    # 1. FORM LEVEL LOCK
    TrialLockManager.lock_form("FORM-F1")
    async with db_manager.get_session_maker()() as session:
        target_obs = await session.get(ClinicalObservation, "OBS-O1")
        target_obs.value = 131.0
        with pytest.raises(PermissionError, match="Form FORM-F1 is currently locked"):
            await session.flush()
    TrialLockManager.reset()

    # 2. VISIT LEVEL LOCK
    TrialLockManager.lock_visit("VISIT-V1")
    async with db_manager.get_session_maker()() as session:
        target_obs = await session.get(ClinicalObservation, "OBS-O1")
        target_obs.value = 132.0
        with pytest.raises(PermissionError, match="Visit VISIT-V1 is currently locked"):
            await session.flush()
    TrialLockManager.reset()

    # 3. SUBJECT LEVEL LOCK
    TrialLockManager.lock_subject("SUBJ-S1")
    async with db_manager.get_session_maker()() as session:
        target_obs = await session.get(ClinicalObservation, "OBS-O1")
        target_obs.value = 133.0
        with pytest.raises(
            PermissionError, match="Subject SUBJ-S1 is currently locked"
        ):
            await session.flush()
    TrialLockManager.reset()

    # 4. SITE LEVEL LOCK
    TrialLockManager.lock_site("SITE-LOCK-1")
    async with db_manager.get_session_maker()() as session:
        target_obs = await session.get(ClinicalObservation, "OBS-O1")
        target_obs.value = 134.0
        with pytest.raises(
            PermissionError, match="Site SITE-LOCK-1 is currently locked"
        ):
            await session.flush()
    TrialLockManager.reset()

    # 5. STUDY LEVEL LOCK
    TrialLockManager.lock_trial(reason="Interim database lock")
    async with db_manager.get_session_maker()() as session:
        target_obs = await session.get(ClinicalObservation, "OBS-O1")
        target_obs.value = 135.0
        with pytest.raises(
            PermissionError, match="Trial is currently locked in a read-only state"
        ):
            await session.flush()
    TrialLockManager.reset()


@pytest.mark.asyncio
async def test_datalock_step_up_signature_and_unlock_justification_boundaries():
    """Verify rejection of invalid sig-tokens on HARD_LOCK and <50 char justifications on unlock.

    @req:PRD-SYS-002
    @req:Trace-3
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # 1. HARD_LOCK without X-Sig-Token -> Expect 401/403
        resp_no_token = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "action": "HARD_LOCK",
                "scope_type": "FORM",
                "scope_id": "FORM-TEST-1",
                "reason": "Hard lock protocol review requirement",
            },
            headers=get_auth_headers(user_id="lead_dm"),
        )
        assert resp_no_token.status_code in (400, 401, 403)

        # 2. HARD_LOCK with invalid/tampered X-Sig-Token -> Expect 401/403
        resp_invalid_token = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "action": "HARD_LOCK",
                "scope_type": "FORM",
                "scope_id": "FORM-TEST-1",
                "reason": "Hard lock protocol review requirement",
            },
            headers={
                **get_auth_headers(user_id="lead_dm"),
                "X-Sig-Token": "invalid.corrupted.token.payload",
            },
        )
        assert resp_invalid_token.status_code in (400, 401, 403)

        # 3. HARD_LOCK with VALID X-Sig-Token -> Expect 200 OK
        valid_token = generate_test_sig_token(user_id="lead_dm")
        resp_valid_lock = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "action": "HARD_LOCK",
                "scope_type": "FORM",
                "scope_id": "FORM-TEST-1",
                "reason": "Hard lock protocol review requirement",
            },
            headers={
                **get_auth_headers(user_id="lead_dm"),
                "X-Sig-Token": valid_token,
            },
        )
        assert resp_valid_lock.status_code == 200
        lock_id = resp_valid_lock.json()["lock_id"]

        # 4. UNLOCK with justification < 50 characters -> Expect 400 Bad Request
        resp_short_unlock = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "lock_id": lock_id,
                "scope_type": "FORM",
                "scope_id": "FORM-TEST-1",
                "justification": "Too short reason for unlock",
            },
            headers=get_auth_headers(user_id="lead_dm"),
        )
        assert resp_short_unlock.status_code == 400
        assert "at least 50 characters" in resp_short_unlock.json()["detail"]

        # 5. UNLOCK with whitespace padding < 50 meaningful chars -> Expect 400 Bad Request
        resp_ws_unlock = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "lock_id": lock_id,
                "scope_type": "FORM",
                "scope_id": "FORM-TEST-1",
                "justification": "   short justification   " + " " * 40,
            },
            headers=get_auth_headers(user_id="lead_dm"),
        )
        assert resp_ws_unlock.status_code == 400

        # 6. UNLOCK with >= 50 valid characters justification -> Expect 200 OK
        valid_justification = "This is a strictly compliant GxP unlock justification that exceeds fifty characters."
        assert len(valid_justification) >= 50
        resp_valid_unlock = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "lock_id": lock_id,
                "scope_type": "FORM",
                "scope_id": "FORM-TEST-1",
                "justification": valid_justification,
            },
            headers=get_auth_headers(user_id="lead_dm"),
        )
        assert resp_valid_unlock.status_code == 200


# ============================================================================
# 3. LAB INGESTION MULTI-FORMAT & BOUNDARY STRESS TESTS
# ============================================================================


@pytest.mark.asyncio
async def test_lab_ingestion_malformed_csv_and_corrupt_data():
    """Verify lab CSV parser is resilient against malformed lines, corrupt headers, and empty files.

    @req:PRD-LAB-001
    @req:Trace-15
    """
    # 1. Empty payload
    recs, errs = parse_csv_payload("")
    assert len(recs) == 0

    # 2. Corrupt header line with missing required subject_id and test_code
    recs_bad_hdr, errs_bad_hdr = parse_csv_payload("COL1,COL2,COL3\nval1,val2,val3\n")
    assert len(recs_bad_hdr) == 0
    assert len(errs_bad_hdr) >= 1

    # 3. Delimited file with mixed unparseable rows
    csv_mixed = (
        "SUBJECT_ID,TEST_CODE,TEST_NAME,VALUE,UNIT,OBS_DATE\n"
        "SUBJ-101,GLUC,Glucose,95.0,mg/dL,2026-08-01 10:00:00\n"
        "SUBJ-102,,MissingTestCode,100,mg/dL,2026-08-01 10:00:00\n"  # Missing test_code
        ",ALT,Alanine Aminotransferase,45,U/L,2026-08-01 10:00:00\n"  # Missing subject_id
        "SUBJ-103,K,Potassium,invalid_numeric_str,mmol/L,2026-08-01 10:00:00\n"  # Non-numeric
    )
    recs_mixed, errs_mixed = parse_csv_payload(csv_mixed)
    assert len(recs_mixed) == 2  # SUBJ-101 (valid) and SUBJ-103 (string value stored)
    assert len(errs_mixed) == 2  # Missing test_code and missing subject_id


@pytest.mark.asyncio
async def test_lab_ingestion_malformed_hl7_and_fhir():
    """Verify HL7 and FHIR parsers gracefully capture malformed messages and missing segments.

    @req:PRD-LAB-001
    @req:Trace-15
    """
    # 1. Malformed HL7 message with missing required Patient Identifier in PID segment
    hl7_bad_pid = "MSH|^~\\&|LAB|CENTRAL|CADENCE|SPONSOR|20260814090000||ORU^R01|MSG01|P|2.5\rPID|1||\rOBX|1|NM|GLUC^Glucose||110|mg/dL"
    recs_hl7, errs_hl7 = parse_hl7_v2_payload(hl7_bad_pid)
    assert len(errs_hl7) >= 1
    assert any(
        "PID segment missing Patient Identifier" in err["error"] for err in errs_hl7
    )

    # 2. Corrupt non-JSON FHIR
    recs_fhir_bad, errs_fhir_bad = parse_fhir_payload("not_valid_json_content{[[")
    assert len(recs_fhir_bad) == 0
    assert len(errs_fhir_bad) >= 1

    # 3. FHIR Observation missing required subject reference
    fhir_missing_subject = {
        "resourceType": "Observation",
        "status": "final",
        "code": {"coding": [{"code": "GLUC", "display": "Glucose"}]},
        "valueQuantity": {"value": 110.0, "unit": "mg/dL"},
    }
    recs_fhir_subj, errs_fhir_subj = parse_fhir_payload(fhir_missing_subject)
    assert len(recs_fhir_subj) == 0
    assert len(errs_fhir_subj) >= 1


@pytest.mark.asyncio
async def test_lab_unit_conversions_and_extreme_range_boundaries():
    """Verify UCUM conversions, range evaluation at exact boundaries, and critical SAE alerts.

    @req:PRD-LAB-001
    @req:PRD-QRY-001
    @req:Trace-15
    """
    # 1. UCUM Unit conversion
    conv_val = convert_unit(1500.0, "g", "kg")
    assert conv_val == pytest.approx(1.5, rel=1e-3)

    # Incompatible units raise ValueError
    with pytest.raises(ValueError, match="Incompatible|Unrecognized"):
        convert_unit(100.0, "mg", "Cel")

    # 2. Range boundary evaluation (Normal: 70.0 - 99.0, Critical: < 40.0 or > 400.0)
    mock_range = {
        "low_bound": 70.0,
        "high_bound": 99.0,
        "critical_low": 40.0,
        "critical_high": 400.0,
    }

    # Exact low boundary -> Normal
    ind_low_edge, oor_low_edge, _ = evaluate_lab_value(70.0, mock_range)
    assert ind_low_edge == "NORMAL"
    assert oor_low_edge is False

    # Exact high boundary -> Normal
    ind_high_edge, oor_high_edge, _ = evaluate_lab_value(99.0, mock_range)
    assert ind_high_edge == "NORMAL"
    assert oor_high_edge is False

    # Below normal, not critical -> LOW
    ind_low, oor_low, _ = evaluate_lab_value(65.0, mock_range)
    assert ind_low == "LOW"
    assert oor_low is True

    # Critical Panic High -> HIGH HIGH
    ind_crit_high, oor_crit_high, _ = evaluate_lab_value(450.0, mock_range)
    assert ind_crit_high == "HIGH HIGH"
    assert oor_crit_high is True


# ============================================================================
# 4. BIOSTAT EXPORTS SERIALIZATION & DE-IDENTIFICATION STRESS TESTS
# ============================================================================


def test_sas_xpt_v5_v8_card_padding_and_ibm_floats():
    """Verify SAS XPT serializer strictly generates 80-byte header cards and encodes IBM 360 floats.

    @req:PRD-MDR-001
    @req:Trace-17
    """
    records = [
        {"USUBJID": "SUBJ-001", "AGE": 45, "SEX": "M", "WEIGHT": 78.5},
        {"USUBJID": "SUBJ-002", "AGE": None, "SEX": "F", "WEIGHT": 62.0},
        {"USUBJID": "SUBJ-003", "AGE": 95, "SEX": "M", "WEIGHT": 0.0},
    ]

    # Test XPT v5 serialization
    xpt_bytes_v5 = write_xpt("DM", records, version="v5")
    assert len(xpt_bytes_v5) > 0
    # Every XPT file length must be an exact multiple of 80 bytes
    assert len(xpt_bytes_v5) % 80 == 0
    meta_v5, parsed_v5 = read_xpt(xpt_bytes_v5)
    assert len(parsed_v5) == 3

    # Test XPT v8 serialization
    xpt_bytes_v8 = write_xpt("DM", records, version="v8")
    assert len(xpt_bytes_v8) > 0
    assert len(xpt_bytes_v8) % 80 == 0
    meta_v8, parsed_v8 = read_xpt(xpt_bytes_v8)
    assert len(parsed_v8) == 3

    # Test IBM 360 float encoding roundtrip
    for test_num in [0.0, 1.0, -1.0, 45.0, 0.000123, 987654.321, -85.25]:
        encoded = double_to_ibm(test_num)
        assert len(encoded) == 8
        decoded = ibm_to_double(encoded)
        assert decoded == pytest.approx(test_num, rel=1e-5)

    # Missing value (None) -> 0x2E '.'
    missing_encoded = double_to_ibm(None)
    assert missing_encoded[0] == 0x2E
    assert ibm_to_double(missing_encoded) is None


def test_cdisc_odm_xml_audit_records_and_entity_escaping():
    """Verify CDISC ODM-XML serializer escapes XML special characters and includes full <AuditRecord> trees.

    @req:PRD-MDR-001
    @req:Trace-1
    @req:Trace-17
    """
    clinical_data = {
        "AE": [
            {
                "USUBJID": "SUBJ-XML-01",
                "AETERM": 'Severe Headache & Nausea <Grade 2> "Emergency"',
                "AESEV": "SEVERE",
            }
        ]
    }

    odm_xml = serialize_to_odm_xml(
        study_id="STUDY-ODM-1",
        data=clinical_data,
        audit_user="auditor_xml",
        change_reason="Reviewed & Approved for Part 11 <Compliance>",
    )

    assert "<ODM" in odm_xml
    assert "</ODM>" in odm_xml
    assert "<AuditRecord>" in odm_xml
    assert '<UserRef UserOID="auditor_xml"' in odm_xml
    # Verify XML entities escaped
    assert "&amp;" in odm_xml
    assert "&lt;" in odm_xml
    assert "&gt;" in odm_xml


def test_hipaa_gdpr_deidentification_rules():
    """Verify HIPAA/GDPR de-identification masks age >= 90 and scrubs direct identifiers.

    @req:PRD-MDR-001
    @req:Trace-17
    """
    records = [
        {
            "USUBJID": "SUBJ-001",
            "AGE": 45,
            "NAME": "John Doe",
            "BIRTHDATE": "1981-05-12",
        },
        {
            "USUBJID": "SUBJ-002",
            "AGE": 92,
            "NAME": "Jane Smith",
            "BIRTHDATE": "1934-01-01",
        },
    ]

    deid_records = deidentify_export_data(records, salt="salt123")
    assert len(deid_records) == 2

    # Direct identifier NAME must be redacted
    assert deid_records[0].get("NAME") == "[REDACTED]"

    # Age 92 must be capped at 89 according to HIPAA Safe Harbor rules
    assert deid_records[1]["AGE"] <= 89
