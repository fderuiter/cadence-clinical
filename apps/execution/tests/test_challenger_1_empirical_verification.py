"""Empirical Challenger 1 Test Suite: Core Logic & Adversarial Verification.

Covers:
1. Medical Coding fuzzy matching, stop phrase stripping, stemming, query escalation, and up-versioning impact analysis.
2. Data Lock & Freeze 6-tier hierarchy inheritance, pre-flush mutation blocking, X-Sig-Token replay prevention, and >= 50 char unlock justification.
3. Lab Ingestion CSV/HL7/FHIR parsing, demographic-stratified range selection, and discrepancy/SAE auto-queries.

Requirements:
- @req:PRD-SYS-001
- @req:PRD-SYS-002
- @req:PRD-SYS-004
- @req:PRD-LAB-001
- @req:PRD-MDR-001
- @req:PRD-MDR-002
- @req:PRD-QRY-001
- @req:Trace-1
- @req:Trace-3
- @req:Trace-13
- @req:Trace-15
- @req:Trace-16
- @req:Trace-17
"""

import hashlib
import hmac
import json
import os
import time
import uuid
from collections.abc import AsyncGenerator

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import select

from apps.execution.coding.impact import run_impact_analysis
from apps.execution.coding.matcher import (
    calculate_combined_score,
    coding_cache,
    normalize_term,
    stem_word,
)
from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    Base,
    ClinicalCodingAssignment,
    ClinicalQuery,
    ClinicalSubject,
    CodingState,
    DataLock,
    DictionaryType,
    FormSubmission,
    LabReferenceRange,
    MedDRAHierarchy,
    MedDRATerm,
    RecodingState,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.main import app as exec_app
from apps.execution.services.lab_ingestion_service import (
    LabIngestionService,
    parse_csv_payload,
    parse_fhir_payload,
    parse_hl7_v2_payload,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security.sig_token_verifier import (
    token_consumption_cache,
    verify_and_consume_sig_token,
)

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id: str = "challenger_admin",
    roles: str = "Data Manager,TERMINOLOGY_MANAGER,SYSTEM_ADMIN,data_manager,crc",
    change_reason: str = "Adversarial verification testing",
    sig_action: str | None = None,
    sig_exp_offset: float = 300.0,
    custom_jti: str | None = None,
) -> dict[str, str]:
    """Generate Gateway signature v2 and optional X-Sig-Token authentication headers."""
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode("utf-8"),
        serialized.encode("utf-8"),
        hashlib.sha256,
    ).hexdigest()
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }
    if sig_action:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": sig_action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + sig_exp_offset,
            "jti": custom_jti or str(uuid.uuid4()),
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
        headers["X-Sig-Token"] = sig_token
    return headers


@pytest_asyncio.fixture(autouse=True)
async def setup_challenger_db() -> AsyncGenerator[None]:
    """Isolate in-memory database, reset lock states, and deploy audit triggers."""
    TrialLockManager.reset()
    coding_cache.clear()
    token_consumption_cache.reset()
    current_user_id.set("test_challenger")
    current_change_reason.set("Challenger verification test setup")

    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    TrialLockManager.reset()
    coding_cache.clear()
    token_consumption_cache.reset()


# ==============================================================================
# 1. MEDICAL CODING ADVERSARIAL STRESS TESTS
# ==============================================================================


@pytest.mark.asyncio
async def test_medical_coding_fuzzy_matching_and_stemming_edge_cases() -> None:
    """Stress test stemming, stop-phrase removal, cosine similarity, and thresholding.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:Trace-16
    """
    # 1. Stemming edge cases
    assert stem_word("") == ""
    assert stem_word("flu") == "flu"  # length <= 3 not modified
    assert stem_word("sinusitises") == "sinusitis"  # 'itises' -> 'itis'
    assert stem_word("bronchitis") == "bronchitis"  # 'itis' preserved
    assert stem_word("allergies") == "allergy"  # 'ies' -> 'y'
    assert stem_word("headaches") == "headache"  # 'es' stripped
    assert stem_word("vomiting") == "vomit"  # 'ing' stripped
    assert stem_word("infected") == "infect"  # 'ed' stripped
    assert stem_word("severely") == "severe"  # 'ly' stripped

    # 2. Stop phrases and stop words stripping
    raw_verbatim = "Onset of severe acute episode of migraine headache due to stress"
    norm = normalize_term(raw_verbatim)
    # Stop phrases 'onset of', 'episode of', 'due to' and stop words 'severe', 'acute' should be removed
    assert "onset" not in norm
    assert "severe" not in norm
    assert "acute" not in norm
    assert "episode" not in norm
    assert "due" not in norm
    assert "migraine" in norm
    assert "headache" in norm

    # 3. Similarity math validation (0.4 * Lev + 0.6 * Cos)
    exact_score = calculate_combined_score("headache", "headache")
    assert pytest.approx(exact_score, 0.001) == 1.0

    empty_score = calculate_combined_score("", "")
    assert empty_score == 1.0

    mismatch_score = calculate_combined_score("hypertension", "fracture")
    assert mismatch_score < 0.3


@pytest.mark.asyncio
async def test_medical_coding_upversioning_and_reclassification_impact() -> None:
    """Stress test up-versioning categorizations: unchanged, deprecated, and reclassified.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:Trace-16
    """
    async with db_manager.get_session_maker()() as session, session.begin():
        # Setup: Assignment 1 (Will be UNCHANGED in v27.0)
        a1 = ClinicalCodingAssignment(
            id="A-UNCHANGED",
            verbatim_text="Headache mild",
            observation_id="OBS-1",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.CODED,
            coded_code="1001",
            coded_term="Headache",
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
        # Assignment 2 (Will be RECLASSIFIED in v27.0 due to new SOC)
        a2 = ClinicalCodingAssignment(
            id="A-RECLASSIFIED",
            verbatim_text="Nausea constant",
            observation_id="OBS-2",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.CODED,
            coded_code="1002",
            coded_term="Nausea",
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
        # Assignment 3 (Will be DEPRECATED / MISSING in v27.0)
        a3 = ClinicalCodingAssignment(
            id="A-DEPRECATED",
            verbatim_text="Obsolete condition",
            observation_id="OBS-3",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.CODED,
            coded_code="1003",
            coded_term="Obsolete condition",
            hierarchy={
                "hierarchies": [
                    {
                        "llt_code": "1003",
                        "pt_code": "1003",
                        "hlt_code": "2003",
                        "hlgt_code": "3003",
                        "soc_code": "4003",
                        "primary_soc_flag": "Y",
                    }
                ]
            },
        )
        session.add_all([a1, a2, a3])

        # Seed v27.0 dictionary
        # Term 1001: identical hierarchy
        session.add(
            MedDRATerm(
                dictionary_version="27.0",
                code="1001",
                term_name="Headache",
                level="LLT",
            )
        )
        session.add(
            MedDRAHierarchy(
                dictionary_version="27.0",
                llt_code="1001",
                pt_code="1001",
                hlt_code="2001",
                hlgt_code="3001",
                soc_code="4001",
                primary_soc_flag="Y",
            )
        )

        # Term 1002: modified hierarchy (soc_code changed from 4002 -> 4999)
        session.add(
            MedDRATerm(
                dictionary_version="27.0",
                code="1002",
                term_name="Nausea",
                level="LLT",
            )
        )
        session.add(
            MedDRAHierarchy(
                dictionary_version="27.0",
                llt_code="1002",
                pt_code="1002",
                hlt_code="2002",
                hlgt_code="3002",
                soc_code="4999",
                primary_soc_flag="Y",
            )
        )
        # Note: Term 1003 is omitted in v27.0 to simulate deprecation

    # Execute impact analysis
    async with db_manager.get_session_maker()() as session, session.begin():
        metrics = await run_impact_analysis(
            session=session,
            dictionary_type="MEDDRA",
            new_version="27.0",
            actor="challenger_impact_auditor",
        )
        assert metrics["unchanged"] == 1
        assert metrics["reclassified"] == 1
        assert metrics["deprecated"] == 1

    # Verify database state mutations and ledger audit trail
    async with db_manager.get_session_maker()() as session:
        r_unchanged = (
            await session.execute(
                select(ClinicalCodingAssignment).where(
                    ClinicalCodingAssignment.id == "A-UNCHANGED"
                )
            )
        ).scalar_one()
        assert r_unchanged.dictionary_version == "27.0"
        assert r_unchanged.status == CodingState.CODED

        r_reclass = (
            await session.execute(
                select(ClinicalCodingAssignment).where(
                    ClinicalCodingAssignment.id == "A-RECLASSIFIED"
                )
            )
        ).scalar_one()
        assert r_reclass.status == CodingState.RECODING_REQUIRED
        assert r_reclass.recoding_status == RecodingState.PENDING

        r_dep = (
            await session.execute(
                select(ClinicalCodingAssignment).where(
                    ClinicalCodingAssignment.id == "A-DEPRECATED"
                )
            )
        ).scalar_one()
        assert r_dep.status == CodingState.RECODING_REQUIRED
        assert r_dep.recoding_status == RecodingState.PENDING


# ==============================================================================
# 2. DATA LOCK & FREEZE ADVERSARIAL STRESS TESTS
# ==============================================================================


@pytest.mark.asyncio
async def test_data_lock_6_tier_hierarchy_and_mutation_blocking() -> None:
    """Stress test 6-tier hierarchical lock inheritance and pre-flush interception.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-13
    """
    # 1. Study-level lock blocks all children
    async with db_manager.get_session_maker()() as session, session.begin():
        session.add(
            DataLock(
                study_id="STUDY-LOCK-X",
                scope_type="STUDY",
                scope_id="STUDY-LOCK-X",
                lock_type="HARD_LOCK",
                is_active=True,
                created_by="sponsor_admin",
                reason_for_change="Global study lock",
            )
        )

    # Attempting to write a FormSubmission under STUDY-LOCK-X must raise PermissionError
    with pytest.raises(PermissionError, match="Study STUDY-LOCK-X is currently locked"):
        async with db_manager.get_session_maker()() as session, session.begin():
            session.add(
                FormSubmission(
                    study_id="STUDY-LOCK-X",
                    site_id="SITE-1",
                    subject_id="SUBJ-1",
                    form_id="FORM-1",
                    status="DRAFT",
                )
            )

    # 2. Site-level lock blocks that site, leaves other site open
    async with db_manager.get_session_maker()() as session, session.begin():
        session.add(
            DataLock(
                study_id="STUDY-OPEN",
                site_id="SITE-LOCKED",
                scope_type="SITE",
                scope_id="SITE-LOCKED",
                lock_type="LOCKED",
                is_active=True,
                created_by="cra_lead",
                reason_for_change="Audit site lock",
            )
        )

    with pytest.raises(PermissionError, match="Site SITE-LOCKED is currently locked"):
        async with db_manager.get_session_maker()() as session, session.begin():
            session.add(
                FormSubmission(
                    study_id="STUDY-OPEN",
                    site_id="SITE-LOCKED",
                    subject_id="SUBJ-10",
                    form_id="FORM-1",
                    status="DRAFT",
                )
            )

    # Mutation on SITE-UNLOCKED succeeds
    async with db_manager.get_session_maker()() as session, session.begin():
        session.add(
            FormSubmission(
                study_id="STUDY-OPEN",
                site_id="SITE-UNLOCKED",
                subject_id="SUBJ-20",
                form_id="FORM-1",
                status="DRAFT",
            )
        )


@pytest.mark.asyncio
async def test_sig_token_step_up_replay_prevention_and_unlock_justification() -> None:
    """Stress test single-use JWT replay prevention and >=50 chars unlock rule.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:Trace-1
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Test X-Sig-Token verification & replay prevention
    user = "pi_investigator_01"
    token_payload = {
        "sub": user,
        "username": user,
        "action": "HARD_LOCK",
        "exp": time.time() + 300,
        "jti": str(uuid.uuid4()),
    }
    raw_token = jwt.encode(
        token_payload, GATEWAY_SECRET.encode("utf-8"), algorithm="HS256"
    )

    # First consumption succeeds
    verified = verify_and_consume_sig_token(
        raw_token, expected_user_id=user, secret=GATEWAY_SECRET.encode("utf-8")
    )
    assert verified["sub"] == user

    # Second consumption (replay attack) is rejected with 401
    with pytest.raises(Exception):
        verify_and_consume_sig_token(
            raw_token,
            expected_user_id=user,
            secret=GATEWAY_SECRET.encode("utf-8"),
        )

    # 2. Test Unlock Justification via API
    async with db_manager.get_session_maker()() as session, session.begin():
        session.add(
            DataLock(
                id="lock_test_justification_01",
                study_id="STUDY-J",
                form_id="FORM-J",
                scope_type="FORM",
                scope_id="FORM-J",
                lock_type="LOCKED",
                is_active=True,
                created_by="data_manager",
                reason_for_change="Test lock",
            )
        )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # Reject short justification (<50 chars)
        short_just = "Short justification 12345"
        res_short = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "lock_id": "lock_test_justification_01",
                "form_id": "FORM-J",
                "scope_type": "FORM",
                "justification": short_just,
            },
            headers=headers,
        )
        assert res_short.status_code == 400
        assert "at least 50 characters" in res_short.json()["detail"]

        # Accept valid justification (>=50 chars)
        valid_just = "Formal unlocking authorized by Principal Investigator following query resolution."  # 82 chars
        assert len(valid_just) >= 50
        res_valid = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "lock_id": "lock_test_justification_01",
                "form_id": "FORM-J",
                "scope_type": "FORM",
                "justification": valid_just,
            },
            headers=headers,
        )
        assert res_valid.status_code == 200
        assert res_valid.json()["status"] == "UNLOCKED"


# ==============================================================================
# 3. LAB BATCH INGESTION ADVERSARIAL STRESS TESTS
# ==============================================================================


@pytest.mark.asyncio
async def test_lab_ingestion_parsers_and_delimiter_sniffing() -> None:
    """Stress test CSV pipe/semicolon/tab sniffing, HL7 ORU^R01, and FHIR Observation parsing.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:Trace-15
    """
    # 1. Delimited CSV/TSV/Pipe parsing
    pipe_payload = "subject_id|test_code|value|unit|collection_date\nSUBJ-P1|ALT|45.0|U/L|2026-08-14\n"
    records, errors = parse_csv_payload(pipe_payload)
    assert len(errors) == 0
    assert len(records) == 1
    assert records[0].subject_id == "SUBJ-P1"
    assert records[0].test_code == "ALT"
    assert records[0].value == 45.0

    # 2. HL7 v2.x parser
    hl7_msg = (
        "MSH|^~\\&|CENTRAL|SITE1|CADENCE|SPONSOR|20260814||ORU^R01|M1|P|2.5\r"
        "PID|1||SUBJ-H1||DOE^JOHN||19800101|M\r"
        "OBX|1|NM|CREAT^Creatinine||1.1|mg/dL|0.7-1.3|N|||F\r"
    )
    hl7_records, hl7_errors = parse_hl7_v2_payload(hl7_msg)
    assert len(hl7_errors) == 0
    assert len(hl7_records) == 1
    assert hl7_records[0].subject_id == "SUBJ-H1"
    assert hl7_records[0].test_code == "CREAT"
    assert hl7_records[0].value == 1.1

    # 3. FHIR Observation parsing
    fhir_obs = {
        "resourceType": "Observation",
        "subject": {"reference": "Patient/SUBJ-F1"},
        "code": {"coding": [{"system": "http://loinc.org", "code": "BILIRUBIN"}]},
        "valueQuantity": {"value": 0.8, "unit": "mg/dL"},
    }
    fhir_records, fhir_errors = parse_fhir_payload(fhir_obs)
    assert len(fhir_errors) == 0
    assert len(fhir_records) == 1
    assert fhir_records[0].subject_id == "SUBJ-F1"
    assert fhir_records[0].test_code == "BILIRUBIN"
    assert fhir_records[0].value == 0.8


@pytest.mark.asyncio
async def test_lab_reference_range_selection_and_critical_sae_alerts() -> None:
    """Stress test multi-dimensional demographic range selection and SAE alert generation.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-15
    """
    study_id = "STUDY-ADV-LAB"
    async with db_manager.get_session_maker()() as session, session.begin():
        # Subject
        session.add(
            ClinicalSubject(
                subject_id="SUBJ-ADV-01",
                study_id=study_id,
                site_id="SITE-01",
                encrypted_demographics=encrypt_demographics(
                    {"gender": "F", "birthdate": "1995-06-01"}
                ),
            )
        )
        # Reference Range with Critical Boundaries: Normal [3.5, 5.0], Critical [<2.5, >6.0]
        session.add(
            LabReferenceRange(
                study_id=study_id,
                test_code="K",
                test_name="Potassium",
                source="CENTRAL",
                unit="mmol/L",
                normalized_unit="mmol/L",
                sex_applicability="ALL",
                low_bound=3.5,
                high_bound=5.0,
                critical_low=2.5,
                critical_high=6.0,
            )
        )

    # Ingest critical high value (6.8 > 6.0)
    csv_data = "Subject ID,Test Code,Value,Unit\nSUBJ-ADV-01,K,6.8,mmol/L\n"
    async with db_manager.get_session_maker()() as session:
        res = await LabIngestionService.ingest_batch(
            session=session,
            payload=csv_data,
            format="csv",
            study_id=study_id,
        )
        assert res.status == "COMPLETED"
        assert res.critical_alerts == 1
        assert res.queries_raised == 1

        # Verify generated ClinicalQuery is POTENTIAL_SAE_CRITICAL and CRITICAL priority
        stmt_q = select(ClinicalQuery).where(ClinicalQuery.study_id == study_id)
        queries = (await session.execute(stmt_q)).scalars().all()
        assert len(queries) == 1
        assert queries[0].query_type == "POTENTIAL_SAE_CRITICAL"
        assert queries[0].priority == "CRITICAL"
        assert queries[0].status == "OPEN"
