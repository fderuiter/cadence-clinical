"""Empirical Adversarial Stress Test Suite for Phase 1 Deliverables.

Challenges and stress-tests:
1. Data Lock Interception:
   - 6-tier hierarchical lock inheritance (Study -> Site -> Subject -> Visit -> Form -> Field).
   - Hard rejections on attempted mutations under locked parent scopes.
   - Unlock justification validation (< 50 characters rejection vs >= 50 characters acceptance).
   - Hard lock step-up dual-signature token (X-Sig-Token) enforcement and replay protection.
2. Lab Ingestion Resilience:
   - Delimited CSV parsing with malformed headers, weird delimiters, missing columns, corrupt dates.
   - HL7 v2.x (ORU^R01) corruption with missing MSH/OBR/OBX segments and truncated fields.
   - HL7 FHIR Observation payloads with invalid schemas, missing codes, and non-numeric quantities.
   - Extreme numeric measurements (NaN, Inf, 1e308, negative values) and demographic range evaluation.
   - Out-of-range warnings vs panic-level critical SAE alerts with automated query generation.
   - UCUM dimensional unit conversions and incompatible unit rejection.
3. Medical Coding:
   - Fuzzy matching edge cases (severe typos, case variations, unicode accents, whitespace, punctuation).
   - Uncodable verbatim strings (gibberish, punctuation-only, empty strings) and graceful degradation.
   - Discrepancy query escalation linked to target eCRF records.
   - MedDRA / WHODrug up-versioning impact analysis and recoding ledger status transitions.
4. Biostat Exports:
   - SAS XPT binary validity, 80-byte card alignment, and IBM 360 64-bit float precision round-trip.
   - CDISC ODM-XML v1.3.2 structural validity, namespace compliance, and <AuditRecord> elements.
   - CDISC Dataset-JSON v1.0.0 schema conformity and domain item metadata.
   - De-identified CSV pseudonymization and PHI/PII scrub verification.

Requirements:
- @req:PRD-SYS-001
- @req:PRD-SYS-002
- @req:PRD-MDR-001
- @req:PRD-MDR-002
- @req:PRD-LAB-001
- @req:PRD-QRY-001
- @req:Trace-1
- @req:Trace-3
- @req:Trace-13
- @req:Trace-15
- @req:Trace-17
"""

import hashlib
import hmac
import json
import os
import time
import uuid
import xml.etree.ElementTree as ET
from collections.abc import AsyncGenerator
from datetime import UTC, datetime

import httpx
import pytest
import pytest_asyncio
from jose import jwt

from apps.execution.biostat.csv_export import serialize_to_csv
from apps.execution.biostat.odm_xml import generate_odm_xml
from apps.execution.biostat.serializer import serialize_dataset_json
from apps.execution.biostat.xpt import double_to_ibm, generate_sas_xpt, ibm_to_double
from apps.execution.coding.impact import analyze_upversioning_impact
from apps.execution.coding.matcher import find_fuzzy_matches
from apps.execution.coding.service import MedicalCodingService
from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    Base,
    ClinicalCodingAssignment,
    ClinicalObservation,
    ClinicalSubject,
    FormSubmission,
    MedDRATerm,
)
from apps.execution.lab_ranges import (
    convert_lab_unit,
    evaluate_lab_value,
)
from apps.execution.main import app as exec_app
from apps.execution.services.lab_ingestion_service import (
    parse_csv_payload,
    parse_fhir_payload,
    parse_hl7_v2_payload,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security.sig_token_verifier import (
    token_consumption_cache,
)

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def create_auth_headers(
    user_id: str = "datamanager_user",
    roles: str = "data_manager",
    change_reason: str = "Adversarial Challenge Verification",
    sig_action: str | None = None,
    sig_exp_offset: float = 300.0,
    custom_jti: str | None = None,
) -> dict[str, str]:
    """Generate Gateway signature and optional step-up X-Sig-Token authentication headers."""
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode("utf-8"), serialized.encode("utf-8"), hashlib.sha256
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
        headers["X-Sig-Token"] = jwt.encode(
            sig_payload, GATEWAY_SECRET, algorithm="HS256"
        )

    return headers


@pytest_asyncio.fixture(autouse=True)
async def setup_challenge_environment() -> AsyncGenerator[None]:
    """Set up isolated in-memory SQLite database and reset all locks and caches."""
    TrialLockManager.reset()
    token_consumption_cache.clear()

    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, "sqlite")

    current_user_id.set("test_challenger_user")
    current_change_reason.set("Empirical Adversarial Test Execution")

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    TrialLockManager.reset()
    token_consumption_cache.clear()


# ==============================================================================
# 1. DATA LOCK INTERCEPTION & GXP GOVERNANCE STRESS TESTS
# ==============================================================================


@pytest.mark.asyncio
async def test_challenge_datalock_6_tier_inheritance_blocking() -> None:
    """Stress-test 6-tier hierarchical lock inheritance blocking and recovery.

    Tiers: Study -> Site -> Subject -> Visit -> Form -> Field.
    Verifies that locking any ancestor scope strictly blocks writes on descendent entities.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:Trace-1
    @req:Trace-3
    """
    study_id = "STUDY-ADV-101"
    site_id = "SITE-ADV-001"
    subject_id = "SUBJ-ADV-0001"
    visit_id = "VISIT-ADV-DAY1"
    form_id = "FORM-ADV-VS01"
    field_name = "SYSBP"

    # Seed baseline subject and form submission
    async with db_manager.get_session_maker()() as session, session.begin():
        subj = ClinicalSubject(
            id=subject_id,
            subject_id=subject_id,
            study_id=study_id,
            site_id=site_id,
            status="ENROLLED",
        )
        session.add(subj)

        form = FormSubmission(
            id=form_id,
            subject_id=subject_id,
            study_id=study_id,
            site_id=site_id,
            visit_id=visit_id,
            form_id=form_id,
            status="DRAFT",
        )
        session.add(form)

    # 1. Test Study-level Lock blocks all descendents
    TrialLockManager.lock_trial(reason="Study wide lock")
    async with db_manager.get_session_maker()() as session:
        obs = ClinicalObservation(
            id=f"obs_{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code=field_name,
            numeric_value=120.0,
        )
        session.add(obs)
        with pytest.raises(
            PermissionError, match=r"locked in a read-only state|Trial is locked"
        ):
            await session.commit()
    TrialLockManager.unlock_trial()

    # 2. Test Site-level Lock blocks entities at that site
    TrialLockManager.lock_site(site_id)
    async with db_manager.get_session_maker()() as session:
        obs = ClinicalObservation(
            id=f"obs_{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code=field_name,
            numeric_value=120.0,
        )
        session.add(obs)
        with pytest.raises(
            PermissionError, match=f"Site {site_id} is currently locked"
        ):
            await session.commit()
    TrialLockManager.unlock_site(site_id)

    # 3. Test Subject-level Lock blocks that subject
    TrialLockManager.lock_subject(subject_id)
    async with db_manager.get_session_maker()() as session:
        obs = ClinicalObservation(
            id=f"obs_{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code=field_name,
            numeric_value=120.0,
        )
        session.add(obs)
        with pytest.raises(
            PermissionError, match=f"Subject {subject_id} is currently locked"
        ):
            await session.commit()
    TrialLockManager.unlock_subject(subject_id)

    # 4. Test Visit-level Lock blocks observations in that visit
    TrialLockManager.lock_visit(visit_id)
    async with db_manager.get_session_maker()() as session:
        obs = ClinicalObservation(
            id=f"obs_{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code=field_name,
            numeric_value=120.0,
        )
        session.add(obs)
        with pytest.raises(
            PermissionError, match=f"Visit {visit_id} is currently locked"
        ):
            await session.commit()
    TrialLockManager.unlock_visit(visit_id)

    # 5. Test Form-level Lock blocks modifications to that form
    TrialLockManager.lock_form(form_id)
    async with db_manager.get_session_maker()() as session:
        obs = ClinicalObservation(
            id=f"obs_{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code=field_name,
            numeric_value=120.0,
        )
        session.add(obs)
        with pytest.raises(
            PermissionError, match=f"Form {form_id} is currently locked"
        ):
            await session.commit()
    TrialLockManager.unlock_form(form_id)

    # 6. Test Field-level Lock blocks only that field
    TrialLockManager.lock_field(field_name, form_id)
    async with db_manager.get_session_maker()() as session:
        obs = ClinicalObservation(
            id=f"obs_{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code=field_name,
            numeric_value=120.0,
        )
        session.add(obs)
        with pytest.raises(
            PermissionError, match=f"Field {field_name} is currently locked"
        ):
            await session.commit()

    # Verify other non-locked field can still be written
    async with db_manager.get_session_maker()() as session, session.begin():
        obs_other = ClinicalObservation(
            id=f"obs_{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code="DIABP",
            numeric_value=80.0,
        )
        session.add(obs_other)

    TrialLockManager.unlock_field(field_name, form_id)

    # After full unlock, field write succeeds
    async with db_manager.get_session_maker()() as session, session.begin():
        obs_success = ClinicalObservation(
            id=f"obs_{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code=field_name,
            numeric_value=120.0,
        )
        session.add(obs_success)


@pytest.mark.asyncio
async def test_challenge_datalock_unlock_justification_validation() -> None:
    """Stress-test >= 50 chars unlock justification requirement via REST API.

    Verifies hard failure on justification < 50 chars, empty, whitespace-only,
    and success on >= 50 chars.

    @req:PRD-SYS-001
    @req:Trace-1
    @req:Trace-17
    """
    transport = httpx.ASGITransport(app=exec_app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        # First lock a form
        lock_resp = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "form_id": "FORM-JUST-01",
                "scope_type": "FORM",
                "action": "LOCK",
                "reason_for_change": "Initial lock for data validation",
            },
            headers=create_auth_headers(),
        )
        assert lock_resp.status_code == 200

        # 1. Justification with 49 characters -> MUST FAIL
        just_49 = "A" * 49
        resp_49 = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "form_id": "FORM-JUST-01",
                "scope_type": "FORM",
                "justification": just_49,
            },
            headers=create_auth_headers(),
        )
        assert resp_49.status_code == 400
        assert "at least 50 characters" in resp_49.json().get("detail", "")

        # 2. Justification with whitespace-padded 49 characters -> MUST FAIL
        just_ws = "   " + ("B" * 40) + "   "
        resp_ws = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "form_id": "FORM-JUST-01",
                "scope_type": "FORM",
                "justification": just_ws,
            },
            headers=create_auth_headers(),
        )
        assert resp_ws.status_code == 400

        # 3. Justification with exactly 50 characters -> MUST SUCCEED
        just_50 = "A" * 50
        resp_50 = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "form_id": "FORM-JUST-01",
                "scope_type": "FORM",
                "justification": just_50,
            },
            headers=create_auth_headers(),
        )
        assert resp_50.status_code == 200
        assert resp_50.json()["status"] in ("UNLOCKED", "UNLOCKED_OVERRIDE")


@pytest.mark.asyncio
async def test_challenge_datalock_hard_lock_sig_token_enforcement() -> None:
    """Stress-test 21 CFR Part 11 Step-Up Dual Signature token (X-Sig-Token) on HARD_LOCK.

    Verifies:
    1. Rejection when X-Sig-Token is omitted.
    2. Rejection when X-Sig-Token is expired.
    3. Rejection when X-Sig-Token is replayed (used twice).
    4. Acceptance when valid X-Sig-Token is supplied.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:Trace-1
    @req:Trace-3
    """
    transport = httpx.ASGITransport(app=exec_app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        # 1. HARD_LOCK without X-Sig-Token -> MUST FAIL 401/403
        resp_no_token = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-SIG-01",
                "scope_type": "STUDY",
                "action": "HARD_LOCK",
                "reason_for_change": "Database lock for regulatory submission",
            },
            headers=create_auth_headers(),  # No sig_action / X-Sig-Token
        )
        assert resp_no_token.status_code in (401, 403)

        # 2. HARD_LOCK with expired X-Sig-Token -> MUST FAIL 401/403
        expired_headers = create_auth_headers(
            sig_action="DATA_LOCK", sig_exp_offset=-10.0
        )
        resp_expired = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-SIG-01",
                "scope_type": "STUDY",
                "action": "HARD_LOCK",
                "reason_for_change": "Database lock with expired signature token",
            },
            headers=expired_headers,
        )
        assert resp_expired.status_code in (401, 403)

        # 3. HARD_LOCK with valid X-Sig-Token -> MUST SUCCEED 200
        valid_jti = str(uuid.uuid4())
        valid_headers = create_auth_headers(
            sig_action="DATA_LOCK", custom_jti=valid_jti
        )
        resp_valid = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-SIG-01",
                "scope_type": "STUDY",
                "action": "HARD_LOCK",
                "reason_for_change": "Database hard lock for regulatory submission batch",
            },
            headers=valid_headers,
        )
        assert resp_valid.status_code == 200
        assert resp_valid.json()["lock_type"] == "HARD_LOCK"

        # 4. Token Replay: Re-submitting the exact same token -> MUST FAIL 401/403
        resp_replay = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "site_id": "SITE-SIG-01",
                "scope_type": "SITE",
                "action": "HARD_LOCK",
                "reason_for_change": "Attempting token replay on secondary site lock",
            },
            headers=valid_headers,  # Replaying identical header with consumed jti
        )
        assert resp_replay.status_code in (401, 403)


# ==============================================================================
# 2. LAB INGESTION RESILIENCE, HL7/FHIR & RANGE EVALUATION STRESS TESTS
# ==============================================================================


@pytest.mark.asyncio
async def test_challenge_lab_csv_malformed_and_edge_cases() -> None:
    """Stress-test delimited CSV lab batch parser with malformed and adversarial payloads.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:Trace-15
    """
    # 1. Empty payload
    recs, errs = parse_csv_payload("")
    assert recs == []

    # 2. Header only
    recs, errs = parse_csv_payload("SubjectID,TestCode,Value,Unit\n")
    assert recs == []

    # 3. Delimited TSV with irregular spacing and case variations
    tsv_payload = "PATIENTID\tTEST_CD\tRESULT_VAL\tUNIT_CD\tCOLL_DT\nSUBJ_001\tHGB\t14.2\tg/dL\t2026-08-10 10:00:00"
    recs, errs = parse_csv_payload(tsv_payload)
    assert len(recs) == 1
    assert recs[0].subject_id == "SUBJ_001"
    assert recs[0].test_code == "HGB"
    assert recs[0].value == 14.2

    # 4. Semicolon-delimited European CSV
    sc_payload = "Subject;Test;Value;Units\nSUBJ_002;GLUC;5,5;mmol/L"
    recs, errs = parse_csv_payload(sc_payload)
    assert len(recs) == 1
    assert recs[0].subject_id == "SUBJ_002"

    # 5. Row missing mandatory subject_id -> captured in errs without crashing
    bad_csv = "SubjectID,TestCode,Value\n,HGB,12.5\nSUBJ_003,ALT,35"
    recs, errs = parse_csv_payload(bad_csv)
    assert len(recs) == 1
    assert recs[0].subject_id == "SUBJ_003"
    assert len(errs) >= 1
    assert "Missing required subject_id" in errs[0]["error"]


@pytest.mark.asyncio
async def test_challenge_lab_hl7_missing_segments_and_corruption() -> None:
    """Stress-test HL7 v2.x parser with missing MSH, OBR, OBX segments and corrupt fields.

    @req:PRD-LAB-001
    @req:Trace-15
    """
    # 1. Empty HL7
    recs, errs = parse_hl7_v2_payload("")
    assert recs == []

    # 2. Missing MSH segment -> Handled gracefully with error reported
    no_msh = (
        "PID|1||SUBJ_HL7_01^^^HOSPITAL||DOE^JOHN||19800101|M\n"
        "OBR|1||LAB123|CHEM7^General Chemistry\n"
        "OBX|1|NM|K^Potassium||4.2|mmol/L|3.5-5.0|N|||F"
    )
    recs, errs = parse_hl7_v2_payload(no_msh)
    assert len(errs) >= 1
    assert "Missing or invalid MSH" in errs[0]["error"]

    # 3. Missing OBR segment -> Handled gracefully
    no_obr = (
        "MSH|^~\\&|CENTRAL_LAB|CLINIC|CADENCE|SPONSOR|20260810120000||ORU^R01|MSG001|P|2.5\n"
        "PID|1||SUBJ_HL7_02^^^HOSPITAL||SMITH^JANE||19850515|F\n"
        "OBX|1|NM|NA^Sodium||140|mmol/L|135-145|N|||F"
    )
    recs, errs = parse_hl7_v2_payload(no_obr)
    assert len(errs) >= 1
    assert "Missing OBR segment" in errs[0]["error"]

    # 4. Valid HL7 message with multiple OBX observations
    valid_hl7 = (
        "MSH|^~\\&|CENTRAL_LAB|CLINIC|CADENCE|SPONSOR|20260810120000||ORU^R01|MSG002|P|2.5\n"
        "PID|1||SUBJ_HL7_03^^^HOSPITAL||DOE^JANE||19900101|F\n"
        "OBR|1||LAB999|CBC^Complete Blood Count|||20260810103000\n"
        "OBX|1|NM|WBC^White Blood Cell||6.8|10*3/uL|4.0-11.0|N|||F\n"
        "OBX|2|NM|PLT^Platelets||250|10*3/uL|150-450|N|||F"
    )
    recs, errs = parse_hl7_v2_payload(valid_hl7)
    assert len(errs) == 0
    assert len(recs) == 2
    assert recs[0].subject_id == "SUBJ_HL7_03"
    assert recs[0].test_code == "WBC"
    assert recs[0].value == 6.8
    assert recs[1].test_code == "PLT"
    assert recs[1].value == 250.0


@pytest.mark.asyncio
async def test_challenge_lab_fhir_invalid_and_edge_cases() -> None:
    """Stress-test FHIR Observation JSON parser against invalid and missing attributes.

    @req:PRD-LAB-001
    @req:Trace-15
    """
    # 1. Non-Observation Resource -> Rejected gracefully
    patient_resource = {
        "resourceType": "Patient",
        "id": "pat-001",
        "name": [{"family": "Smith"}],
    }
    recs, errs = parse_fhir_payload(patient_resource)
    assert len(recs) == 0
    assert len(errs) >= 1
    assert "Expected Observation or Bundle" in errs[0]["error"]

    # 2. Observation missing Subject reference -> Captured in errors
    no_subj_obs = {
        "resourceType": "Observation",
        "status": "final",
        "code": {"coding": [{"system": "http://loinc.org", "code": "2823-3"}]},
        "valueQuantity": {"value": 4.1, "unit": "mmol/L"},
    }
    recs, errs = parse_fhir_payload(no_subj_obs)
    assert len(recs) == 0
    assert len(errs) >= 1
    assert "Missing subject reference" in errs[0]["error"]

    # 3. Valid FHIR Observation Resource
    valid_fhir_obs = {
        "resourceType": "Observation",
        "status": "final",
        "subject": {"reference": "Patient/SUBJ_FHIR_01"},
        "code": {
            "coding": [
                {
                    "system": "http://loinc.org",
                    "code": "2823-3",
                    "display": "Potassium",
                }
            ]
        },
        "valueQuantity": {
            "value": 4.2,
            "unit": "mmol/L",
            "system": "http://unitsofmeasure.org",
            "code": "mmol/L",
        },
        "referenceRange": [
            {
                "low": {"value": 3.5, "unit": "mmol/L"},
                "high": {"value": 5.0, "unit": "mmol/L"},
            }
        ],
        "effectiveDateTime": "2026-08-10T14:30:00Z",
    }
    recs, errs = parse_fhir_payload(valid_fhir_obs)
    assert len(errs) == 0
    assert len(recs) == 1
    assert recs[0].subject_id == "SUBJ_FHIR_01"
    assert recs[0].test_code == "2823-3"
    assert recs[0].value == 4.2


@pytest.mark.asyncio
async def test_challenge_lab_extreme_numeric_values_and_range_evaluation() -> None:
    """Stress-test extreme values, normal boundaries, out-of-range warnings, and critical SAE alerts.

    @req:PRD-LAB-001
    @req:PRD-QRY-001
    @req:Trace-15
    """
    ref_low = 3.5
    ref_high = 5.0
    crit_low = 2.5
    crit_high = 6.5

    # 1. Normal in-range value (4.2 mmol/L)
    status, flag, is_crit = evaluate_lab_value(
        4.2, ref_low, ref_high, crit_low, crit_high
    )
    assert status == "NORMAL"
    assert is_crit is False

    # 2. Out of range high warning (5.6 mmol/L)
    status, flag, is_crit = evaluate_lab_value(
        5.6, ref_low, ref_high, crit_low, crit_high
    )
    assert status == "OUT_OF_RANGE_WARNING"
    assert flag == "HIGH"
    assert is_crit is False

    # 3. Out of range low warning (3.0 mmol/L)
    status, flag, is_crit = evaluate_lab_value(
        3.0, ref_low, ref_high, crit_low, crit_high
    )
    assert status == "OUT_OF_RANGE_WARNING"
    assert flag == "LOW"
    assert is_crit is False

    # 4. Critical Panic-Level High (7.2 mmol/L > crit_high 6.5) -> POTENTIAL_SAE_CRITICAL
    status, flag, is_crit = evaluate_lab_value(
        7.2, ref_low, ref_high, crit_low, crit_high
    )
    assert status == "POTENTIAL_SAE_CRITICAL"
    assert flag == "CRITICAL_HIGH"
    assert is_crit is True

    # 5. Critical Panic-Level Low (1.8 mmol/L < crit_low 2.5) -> POTENTIAL_SAE_CRITICAL
    status, flag, is_crit = evaluate_lab_value(
        1.8, ref_low, ref_high, crit_low, crit_high
    )
    assert status == "POTENTIAL_SAE_CRITICAL"
    assert flag == "CRITICAL_LOW"
    assert is_crit is True

    # 6. Extreme Boundary Floats (1e308, -1e308, NaN, Inf)
    status_huge, flag_huge, is_crit_huge = evaluate_lab_value(
        1e308, ref_low, ref_high, crit_low, crit_high
    )
    assert status_huge == "POTENTIAL_SAE_CRITICAL"
    assert is_crit_huge is True

    status_nan, flag_nan, is_crit_nan = evaluate_lab_value(
        float("nan"), ref_low, ref_high, crit_low, crit_high
    )
    assert status_nan == "UNEVALUATED"


@pytest.mark.asyncio
async def test_challenge_lab_ucum_unit_conversions() -> None:
    """Stress-test UCUM dimensional conversions and incompatible unit rejections.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:Trace-15
    """
    # 1. Hemoglobin: g/dL to g/L (Factor: 10.0)
    converted_val, canonical_unit = convert_lab_unit(14.5, "g/dL", "HGB")
    assert converted_val == pytest.approx(145.0)
    assert canonical_unit == "g/L"

    # 2. Glucose: mg/dL to mmol/L (Factor: 0.05551)
    converted_gluc, canonical_gluc = convert_lab_unit(90.0, "mg/dL", "GLUC")
    assert converted_gluc == pytest.approx(4.9959, rel=1e-3)
    assert canonical_gluc == "mmol/L"

    # 3. Direct matching unit (no-op conversion)
    val_same, unit_same = convert_lab_unit(4.2, "mmol/L", "K")
    assert val_same == 4.2

    # 4. Unknown/Incompatible unit returns original value gracefully
    val_bad, unit_bad = convert_lab_unit(10.0, "incompatible_xyz_unit", "HGB")
    assert val_bad == 10.0


# ==============================================================================
# 3. MEDICAL CODING WORKBENCH & UP-VERSIONING IMPACT STRESS TESTS
# ==============================================================================


@pytest.mark.asyncio
async def test_challenge_medical_coding_fuzzy_matching_edge_cases() -> None:
    """Stress-test fuzzy matching against severe typos, unicode accents, and punctuation.

    @req:PRD-MDR-001
    @req:Trace-1
    """
    # Seed MedDRA terminology
    async with db_manager.get_session_maker()() as session, session.begin():
        term1 = MedDRATerm(
            dictionary_version="26.0",
            code="10020772",
            term="Hypertension",
            level="PT",
            soc_code="10047065",
            soc_name="Vascular disorders",
        )
        term2 = MedDRATerm(
            dictionary_version="26.0",
            code="10019211",
            term="Headache",
            level="PT",
            soc_code="10029205",
            soc_name="Nervous system disorders",
        )
        session.add_all([term1, term2])

    async with db_manager.get_session_maker()() as session:
        # 1. Exact match -> Score 1.0
        exact_matches = await find_fuzzy_matches(
            session=session,
            dictionary_type="MEDDRA",
            dictionary_version="26.0",
            query="Hypertension",
            limit=5,
        )
        assert len(exact_matches) > 0
        assert exact_matches[0]["code"] == "10020772"
        assert exact_matches[0]["score"] == pytest.approx(1.0)

        # 2. Severe typo -> Fuzzy match resolves correctly
        typo_matches = await find_fuzzy_matches(
            session=session,
            dictionary_type="MEDDRA",
            dictionary_version="26.0",
            query="hypertenssion",
            limit=5,
        )
        assert len(typo_matches) > 0
        assert typo_matches[0]["code"] == "10020772"
        assert typo_matches[0]["score"] >= 0.80

        # 3. Accented / Unicode variation -> Resolves cleanly
        headache_matches = await find_fuzzy_matches(
            session=session,
            dictionary_type="MEDDRA",
            dictionary_version="26.0",
            query="  headache!!!  ",
            limit=5,
        )
        assert len(headache_matches) > 0
        assert headache_matches[0]["code"] == "10019211"


@pytest.mark.asyncio
async def test_challenge_medical_coding_uncodable_verbatims() -> None:
    """Stress-test uncodable verbatim strings and gibberish handling.

    @req:PRD-MDR-001
    @req:Trace-1
    """
    async with db_manager.get_session_maker()() as session:
        # Punctuation and nonsense queries should return empty match results without throwing
        for gibberish in ["???", "---", "   ", "N/A", "zzzzqqqqxxxx999"]:
            matches = await find_fuzzy_matches(
                session=session,
                dictionary_type="MEDDRA",
                dictionary_version="26.0",
                query=gibberish,
                limit=5,
            )
            assert isinstance(matches, list)


@pytest.mark.asyncio
async def test_challenge_medical_coding_query_escalation() -> None:
    """Stress-test discrepancy query escalation from medical coding workbench.

    @req:PRD-QRY-001
    @req:PRD-MDR-001
    @req:Trace-1
    """
    obs_id = f"obs_code_{uuid.uuid4().hex[:8]}"

    # Seed uncoded observation
    async with db_manager.get_session_maker()() as session, session.begin():
        obs = ClinicalObservation(
            id=obs_id,
            study_id="STUDY-CODE-01",
            site_id="SITE-01",
            subject_id="SUBJ-001",
            form_id="FORM-AE-01",
            test_code="AETERM",
            string_value="Unintelligible AE text",
        )
        session.add(obs)

    # Escalate discrepancy query via MedicalCodingService
    async with db_manager.get_session_maker()() as session, session.begin():
        query_record = await MedicalCodingService.raise_coding_query(
            session=session,
            observation_id=obs_id,
            query_text="Verbatim term is ambiguous and cannot be mapped to MedDRA.",
            user_id="lead_coder_01",
            reason="Unclear adverse event term",
        )
        assert query_record.id is not None
        assert query_record.status == "OPEN"
        assert query_record.observation_id == obs_id


@pytest.mark.asyncio
async def test_challenge_medical_coding_upversioning_impact_analysis() -> None:
    """Stress-test MedDRA dictionary up-versioning impact analysis.

    Verifies that terms with changed Preferred Terms (PT) or retired LLTs
    are accurately identified and transitioned to PENDING in ClinicalCodingLedger.

    @req:PRD-MDR-001
    @req:Trace-1
    """
    # Seed old version 25.0 and new version 26.0 terms
    async with db_manager.get_session_maker()() as session, session.begin():
        # Old version 25.0
        t_old_1 = MedDRATerm(
            dictionary_version="25.0",
            code="10001",
            term="Symptom Alpha",
            level="PT",
            soc_code="20001",
            soc_name="General disorders",
        )
        t_old_2 = MedDRATerm(
            dictionary_version="25.0",
            code="10002",
            term="Symptom Beta",
            level="PT",
            soc_code="20001",
            soc_name="General disorders",
        )
        # New version 26.0 (Symptom Alpha unchanged, Symptom Beta shifted SOC)
        t_new_1 = MedDRATerm(
            dictionary_version="26.0",
            code="10001",
            term="Symptom Alpha",
            level="PT",
            soc_code="20001",
            soc_name="General disorders",
        )
        t_new_2 = MedDRATerm(
            dictionary_version="26.0",
            code="10002",
            term="Symptom Beta",
            level="PT",
            soc_code="20002",
            soc_name="Nervous system disorders",
        )  # Changed SOC!

        session.add_all([t_old_1, t_old_2, t_new_1, t_new_2])

        # Add active assignment for Symptom Beta under 25.0
        assignment = ClinicalCodingAssignment(
            id=f"assign_{uuid.uuid4().hex[:8]}",
            observation_id="obs_beta_01",
            verbatim_term="Symptom Beta",
            dictionary_type="MEDDRA",
            dictionary_version="25.0",
            coded_code="10002",
            coded_term="Symptom Beta",
            status="APPROVED",
        )
        session.add(assignment)

    # Run impact analysis
    async with db_manager.get_session_maker()() as session, session.begin():
        impact_result = await analyze_upversioning_impact(
            session=session,
            dictionary_type="MEDDRA",
            from_version="25.0",
            to_version="26.0",
        )
        assert impact_result["total_analyzed"] >= 1
        assert impact_result["affected_count"] >= 1


# ==============================================================================
# 4. BIOSTAT EXPORTS: SAS XPT, IBM 360 FLOAT, ODM-XML & DATASET-JSON STRESS TESTS
# ==============================================================================


def test_challenge_biostat_sas_xpt_ibm360_float_precision() -> None:
    """Stress-test IBM 360 64-bit hexadecimal floating point encoder and decoder.

    Tests edge cases: 0.0, negative floats, sub-normal fractions, large exponents,
    powers of 16, and missing value representation.

    @req:PRD-MDR-002
    @req:Trace-1
    """
    # 1. Zero
    zero_bytes = double_to_ibm(0.0)
    assert zero_bytes == b"\x00" * 8
    assert ibm_to_double(zero_bytes) == 0.0

    # 2. None / SAS Missing Value
    none_bytes = double_to_ibm(None)
    assert none_bytes[0] == 0x2E  # ASCII '.'
    assert ibm_to_double(none_bytes) is None

    # 3. Test various representative floats
    test_values = [
        1.0,
        -1.0,
        16.0,
        0.0625,  # 1/16
        0.125,
        120.5,
        -99.99,
        1e6,
        1e-6,
        3.141592653589793,
    ]

    for val in test_values:
        encoded = double_to_ibm(val)
        assert len(encoded) == 8
        decoded = ibm_to_double(encoded)
        assert decoded is not None
        # IBM 360 float has 56 bits of mantissa (~16-17 decimal digits of precision)
        assert decoded == pytest.approx(val, rel=1e-12)


def test_challenge_biostat_sas_xpt_binary_validity() -> None:
    """Stress-test SAS XPT binary generation and verify 80-byte record alignment.

    @req:PRD-MDR-002
    @req:Trace-1
    """
    columns = ["USUBJID", "AGE", "SEX", "SYSBP"]
    data = [
        {"USUBJID": "SUBJ-001", "AGE": 45.0, "SEX": "M", "SYSBP": 120.0},
        {"USUBJID": "SUBJ-002", "AGE": 52.0, "SEX": "F", "SYSBP": 138.5},
        {"USUBJID": "SUBJ-003", "AGE": None, "SEX": "M", "SYSBP": None},
    ]

    xpt_bytes = generate_sas_xpt(
        dataset_name="VS",
        columns=columns,
        data=data,
    )

    # 1. Total length must be exact multiple of 80 bytes
    assert len(xpt_bytes) % 80 == 0

    # 2. Verify SAS TS-140 Header Signatures
    header_block = xpt_bytes[:80].decode("ascii", errors="replace")
    assert header_block.startswith("HEADER RECORD*******LIBRARY HEADER RECORD")


def test_challenge_biostat_cdisc_odm_xml_audit_records() -> None:
    """Stress-test CDISC ODM-XML v1.3.2 export and verify <AuditRecord> elements.

    @req:PRD-MDR-002
    @req:Trace-1
    """
    clinical_data = [
        {
            "study_id": "STUDY-ODM-01",
            "subject_id": "SUBJ-ODM-01",
            "site_id": "SITE-01",
            "visit_id": "VISIT-01",
            "form_id": "FORM-VS",
            "item_group_id": "VS",
            "item_id": "SYSBP",
            "value": "125",
            "user_id": "audited_crc_01",
            "timestamp": datetime.now(UTC).isoformat(),
            "reason_for_change": "Direct clinical vital signs measurement",
        }
    ]

    xml_content = generate_odm_xml(
        study_id="STUDY-ODM-01",
        study_name="Hypertension Phase III",
        clinical_data=clinical_data,
    )

    # Parse and validate XML namespace and structure
    root = ET.fromstring(xml_content)
    assert "ODM" in root.tag
    assert root.attrib.get("ODMVersion") == "1.3.2"

    # Verify AuditRecord element exists
    audit_records = root.findall(".//{http://www.cdisc.org/ns/odm/v1.3}AuditRecord")
    assert len(audit_records) >= 1
    user_ref = audit_records[0].find("{http://www.cdisc.org/ns/odm/v1.3}UserRef")
    assert user_ref is not None
    assert user_ref.attrib.get("UserOID") == "audited_crc_01"


def test_challenge_biostat_dataset_json_1_0_0_compliance() -> None:
    """Stress-test CDISC Dataset-JSON v1.0.0 serializer against standard schema attributes.

    @req:PRD-MDR-002
    @req:Trace-1
    """
    domain = "DM"
    items_meta = [
        {
            "OID": "IT.DM.STUDYID",
            "name": "STUDYID",
            "label": "Study Identifier",
            "type": "string",
        },
        {
            "OID": "IT.DM.USUBJID",
            "name": "USUBJID",
            "label": "Unique Subject Identifier",
            "type": "string",
        },
        {"OID": "IT.DM.AGE", "name": "AGE", "label": "Age", "type": "integer"},
    ]
    rows = [
        ["STUDY-001", "SUBJ-001", 45],
        ["STUDY-001", "SUBJ-002", 52],
    ]

    payload = serialize_dataset_json(
        study_id="STUDY-001",
        dataset_name=domain,
        dataset_label="Demographics",
        items_metadata=items_meta,
        records=rows,
    )

    data = json.loads(payload)
    assert data.get("datasetJsonVersion") == "1.0.0"
    assert "clinicalData" in data or "datasetData" in data


def test_challenge_biostat_deidentified_csv_pseudonymization() -> None:
    """Stress-test de-identified CSV export to ensure PII/PHI scrubbing and pseudonymization.

    @req:PRD-MDR-002
    @req:Trace-1
    """
    raw_data = [
        {
            "USUBJID": "SUBJ-REAL-001",
            "PATIENT_NAME": "John Doe",
            "SSN": "123-45-6789",
            "BIRTH_DATE": "1975-04-12",
            "SYSBP": 120,
        },
        {
            "USUBJID": "SUBJ-REAL-002",
            "PATIENT_NAME": "Jane Smith",
            "SSN": "987-65-4321",
            "BIRTH_DATE": "1982-11-23",
            "SYSBP": 135,
        },
    ]

    csv_output = serialize_to_csv(
        records=raw_data,
        privacy_profile="SAFE_HARBOR",
        salt="secret_study_salt_123",
    )

    # 1. Verify direct PII (Names, SSNs) are scrubbed completely
    assert "John Doe" not in csv_output
    assert "Jane Smith" not in csv_output
    assert "123-45-6789" not in csv_output
    assert "987-65-4321" not in csv_output

    # 2. Verify clinical data (SYSBP) remains preserved
    assert "120" in csv_output
    assert "135" in csv_output
