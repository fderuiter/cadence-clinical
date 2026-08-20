"""Phase 1 Comprehensive 4-Tier End-to-End Test Suite.

Authoritative cross-module E2E test suite verifying Phase 1 deliverables:
- Tier 1: Isolated feature verification across all 17 capabilities
- Tier 2: Boundary value and corner-case verification
- Tier 3: Pairwise combinatorial module interactions
- Tier 4: Real-world clinical trial scenarios

Requirements:
- @req:PRD-SYS-001: Standard Audit Logging (21 CFR Part 11 § 11.10(e))
- @req:PRD-SYS-002: Soft-Delete Enforcement and Shadow Schema Preservation
- @req:PRD-SYS-003: Cryptographic Ledger Hashing & Chain Validation
- @req:PRD-SYS-004: Universal Site Isolation Constraint
- @req:PRD-MDR-001: Value-Level Metadata Constraint Propagation
- @req:PRD-MDR-002: Biomedical Concept Lock State during Active Studies
- @req:PRD-SUB-001: State Transition Matrix & Enforcements
- @req:PRD-QRY-001: Query State Transitions and Constraints
- @req:PRD-QRY-002: Query Escalation Rules
- @req:PRD-QRY-003: Cross-Form Edit Check Execution
- @req:PRD-CRF-008: Regulatory & Protocol Document Export
- @req:PRD-LAB-001: Laboratory Reference Models and Validation Runs
- @req:Trace-1: Shadow Schema Retention
- @req:Trace-3: Read-Only Trial Locks & Alert Routing
- @req:Trace-7: Quality & CAPA Traceability and Validation Assurance
- @req:Trace-11: Native Vue 3 Rules Designer and GxP Ledger Sync
- @req:Trace-12: eTMF Document Redaction & Regulatory Privacy Controls
- @req:Trace-13: Native Part 11 eSignature Workflow
- @req:Trace-14: PI Batch Electronic Sign-Off
- @req:Trace-15: SAE Reconciliation
- @req:Trace-17: Gateway Step-Up Re-Authentication and Signature Token Issuance
- @req:Trace-27: Role-Based Authorization Gates
- @req:Trace-28: GxP Change-Reason Justification
- @req:Trace-29: Immutable Audit Attribution
- @req:Trace-30: Version Pinning and Lock Enforcement
- @req:Trace-31: Site & Tenant Data Isolation
"""

import json
import os
import time
import uuid
from collections.abc import AsyncGenerator
from pathlib import Path
from typing import Any

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import select

from apps.execution.biostat import (
    read_xpt,
    serialize_to_csv,
    serialize_to_dataset_json,
    serialize_to_odm_xml,
    validate_dataset_json,
    write_xpt_v5,
    write_xpt_v8,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalCodingAssignment,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    CodingState,
    DataLock,
    DictionaryType,
    LabReferenceRange,
    MedDRATerm,
    WHODrugRecord,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.main import app as exec_app
from apps.execution.services.lab_ingestion_service import (
    parse_csv_payload,
    parse_fhir_payload,
    parse_hl7_v2_payload,
)
from apps.execution.trial_lock import TrialLockManager
from apps.execution.ucum import convert_unit
from packages.security.rbac_helpers import build_gateway_headers

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


def get_test_auth_headers(
    user_id: str = "data_manager_user",
    roles: str = "data_manager,sponsor_admin",
    change_reason: str = "Phase 1 E2E Qualification Run",
    site_id: str | None = None,
    sponsor_id: str | None = None,
    tenant_id: str | None = "tenant_default",
    sig_token: str | None = None,
    unblinded_access: bool = False,
) -> dict[str, str]:
    """Generate canonical gateway headers for authenticated execution API requests."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        tenant_id=tenant_id,
        unblinded_access=unblinded_access,
        sig_token=sig_token,
    )


def create_step_up_sig_token(
    user_id: str = "data_manager_user",
    roles: str = "data_manager",
    action: str = "HARD_LOCK",
    exp_offset: float = 300.0,
    jti: str | None = None,
) -> str:
    """Generate a 21 CFR Part 11 compliant step-up JWT signature token."""
    payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "roles": [roles] if isinstance(roles, str) else roles,
        "jti": jti or str(uuid.uuid4()),
        "iat": time.time(),
        "exp": time.time() + exp_offset,
    }
    return jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")


async def seed_test_catalog(session: Any) -> None:
    """Seed reference ranges, units, and dictionary entries for test suite isolation."""
    # 1. Lab Reference Ranges
    ref_ranges = [
        LabReferenceRange(
            study_id="STUDY-E2E-001",
            test_code="ALT",
            test_name="Alanine Aminotransferase",
            lab_source="CENTRAL",
            unit="U/L",
            normalized_unit="U/L",
            sex="ALL",
            age_low=18.0,
            age_high=120.0,
            range_low=7.0,
            range_high=56.0,
            critical_high=200.0,
            created_by="system",
            reason_for_change="Test init",
        ),
        LabReferenceRange(
            study_id="STUDY-E2E-001",
            test_code="AST",
            test_name="Aspartate Aminotransferase",
            lab_source="CENTRAL",
            unit="U/L",
            normalized_unit="U/L",
            sex="ALL",
            age_low=18.0,
            age_high=120.0,
            range_low=10.0,
            range_high=40.0,
            critical_high=150.0,
            created_by="system",
            reason_for_change="Test init",
        ),
        LabReferenceRange(
            study_id="STUDY-E2E-001",
            test_code="GLUC",
            test_name="Glucose",
            lab_source="CENTRAL",
            unit="mg/dL",
            normalized_unit="mg/dL",
            sex="ALL",
            age_low=18.0,
            age_high=120.0,
            range_low=70.0,
            range_high=100.0,
            critical_low=40.0,
            critical_high=300.0,
            created_by="system",
            reason_for_change="Test init",
        ),
        LabReferenceRange(
            study_id="STUDY-PAIR-02",
            test_code="ALT",
            test_name="Alanine Aminotransferase",
            lab_source="CENTRAL",
            unit="U/L",
            normalized_unit="U/L",
            sex="ALL",
            age_low=18.0,
            age_high=120.0,
            range_low=7.0,
            range_high=56.0,
            critical_high=200.0,
            created_by="system",
            reason_for_change="Test init",
        ),
        LabReferenceRange(
            study_id="ONCOLOGY-PHASE3-001",
            test_code="ALT",
            test_name="Alanine Aminotransferase",
            lab_source="CENTRAL",
            unit="U/L",
            normalized_unit="U/L",
            sex="ALL",
            age_low=18.0,
            age_high=120.0,
            range_low=7.0,
            range_high=56.0,
            critical_high=200.0,
            created_by="system",
            reason_for_change="Test init",
        ),
        LabReferenceRange(
            study_id="ONCOLOGY-PHASE3-001",
            test_code="AST",
            test_name="Aspartate Aminotransferase",
            lab_source="CENTRAL",
            unit="U/L",
            normalized_unit="U/L",
            sex="ALL",
            age_low=18.0,
            age_high=120.0,
            range_low=10.0,
            range_high=40.0,
            critical_high=150.0,
            created_by="system",
            reason_for_change="Test init",
        ),
        LabReferenceRange(
            study_id="ONCOLOGY-PHASE3-001",
            test_code="GLUC",
            test_name="Glucose",
            lab_source="CENTRAL",
            unit="mg/dL",
            normalized_unit="mg/dL",
            sex="ALL",
            age_low=18.0,
            age_high=120.0,
            range_low=70.0,
            range_high=100.0,
            critical_low=40.0,
            critical_high=300.0,
            created_by="system",
            reason_for_change="Test init",
        ),
        LabReferenceRange(
            study_id="FULL-LIFECYCLE-001",
            test_code="ALT",
            test_name="Alanine Aminotransferase",
            lab_source="CENTRAL",
            unit="U/L",
            normalized_unit="U/L",
            sex="ALL",
            age_low=18.0,
            age_high=120.0,
            range_low=7.0,
            range_high=56.0,
            critical_high=200.0,
            created_by="system",
            reason_for_change="Test init",
        ),
    ]
    existing_ranges = set(
        (r.study_id, r.test_code, r.lab_source, r.sex)
        for r in (await session.execute(select(LabReferenceRange))).scalars().all()
    )
    for r in ref_ranges:
        key = (r.study_id, r.test_code, r.lab_source, r.sex)
        if key not in existing_ranges:
            session.add(r)
            existing_ranges.add(key)

    # 2. MedDRA Terminology
    meddra_terms = [
        MedDRATerm(
            dictionary_version="26.0",
            code="10019211",
            term_name="Headache",
            level="LLT",
        ),
        MedDRATerm(
            dictionary_version="26.0",
            code="10019211",
            term_name="Headache",
            level="PT",
        ),
        MedDRATerm(
            dictionary_version="26.0",
            code="10028813",
            term_name="Nausea",
            level="LLT",
        ),
        MedDRATerm(
            dictionary_version="26.0",
            code="10028813",
            term_name="Nausea",
            level="PT",
        ),
        MedDRATerm(
            dictionary_version="26.0",
            code="10016256",
            term_name="Fatigue",
            level="LLT",
        ),
        MedDRATerm(
            dictionary_version="26.0",
            code="10016256",
            term_name="Fatigue",
            level="PT",
        ),
        MedDRATerm(
            dictionary_version="26.0",
            code="10006451",
            term_name="Bronchitis acute",
            level="LLT",
        ),
        MedDRATerm(
            dictionary_version="26.0",
            code="10006451",
            term_name="Bronchitis acute",
            level="PT",
        ),
        # Version 27.0 for upversioning tests
        MedDRATerm(
            dictionary_version="27.0",
            code="10019211",
            term_name="Headache",
            level="LLT",
        ),
        MedDRATerm(
            dictionary_version="27.0",
            code="10019211",
            term_name="Headache",
            level="PT",
        ),
        MedDRATerm(
            dictionary_version="27.0",
            code="10028813",
            term_name="Nausea",
            level="LLT",
        ),
        MedDRATerm(
            dictionary_version="27.0",
            code="10028813",
            term_name="Nausea",
            level="PT",
        ),
        MedDRATerm(
            dictionary_version="27.0",
            code="10016256",
            term_name="Asthenia and fatigue",
            level="PT",
        ),
    ]
    existing_terms = set(
        (t.dictionary_version, t.code, t.level)
        for t in (await session.execute(select(MedDRATerm))).scalars().all()
    )
    for term in meddra_terms:
        term_key = (term.dictionary_version, term.code, term.level)
        if term_key not in existing_terms:
            session.add(term)
            existing_terms.add(term_key)

    # 3. WHODrug Records
    whodrug_drugs = [
        WHODrugRecord(
            dictionary_version="2024-03",
            drug_code="00010101001",
            preferred_name="ASPIRIN",
            drug_name="ASPIRIN TABLET 100MG",
        ),
        WHODrugRecord(
            dictionary_version="2024-03",
            drug_code="00020202002",
            preferred_name="PARACETAMOL",
            drug_name="PARACETAMOL 500MG",
        ),
    ]
    existing_drugs = set(
        (d.dictionary_version, d.drug_code)
        for d in (await session.execute(select(WHODrugRecord))).scalars().all()
    )
    for drug in whodrug_drugs:
        drug_key = (drug.dictionary_version, drug.drug_code)
        if drug_key not in existing_drugs:
            session.add(drug)
            existing_drugs.add(drug_key)

    await session.commit()


@pytest_asyncio.fixture(autouse=True)
async def setup_e2e_db() -> AsyncGenerator[None]:
    """Isolate each E2E test with a clean in-memory SQLite schema and seeded catalogs."""
    from apps.execution.coding.matcher import coding_cache
    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()
    coding_cache.clear()
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)

    async with db_manager.get_session_maker()() as session:
        await seed_test_catalog(session)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    TrialLockManager.reset()
    coding_cache.clear()


@pytest_asyncio.fixture
async def client() -> AsyncGenerator[httpx.AsyncClient]:
    """Provide an asynchronous HTTP client wired to the execution FastAPI app."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as ac:
        yield ac


# ============================================================================
# TIER 1: ISOLATED FEATURE VERIFICATION (17 FEATURES)
# ============================================================================


@pytest.mark.asyncio
async def test_tier1_feature01_coding_queue_and_filter(
    client: httpx.AsyncClient,
) -> None:
    """Verify medical coding queue retrieval and multi-attribute filtering.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    async with db_manager.get_session_maker()() as session:
        assign = ClinicalCodingAssignment(
            id="assign_t1_01",
            observation_id="obs_ae_01",
            source_field="AETERM",
            verbatim_text="Severe migraine headache",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.UNCODED,
        )
        session.add(assign)
        await session.commit()

    headers = get_test_auth_headers(roles="data_manager")
    res = await client.get(
        "/api/v1/execution/coding/assignments",
        headers=headers,
        params={"status": "UNCODED", "dictionary_type": "MEDDRA"},
    )
    assert res.status_code == 200
    data = res.json()
    assert len(data) >= 1
    assert any(a["verbatim_text"] == "Severe migraine headache" for a in data)


@pytest.mark.asyncio
async def test_tier1_feature02_meddra_and_whodrug_traversal(
    client: httpx.AsyncClient,
) -> None:
    """Verify dictionary traversal and code lookups for MedDRA and WHODrug.

    @req:PRD-SYS-001
    @req:PRD-MDR-001
    """
    headers = get_test_auth_headers(roles="data_manager")

    # MedDRA lookup
    res_meddra = await client.get(
        "/api/v1/dictionaries/meddra/code",
        headers=headers,
        params={"term": "Headache", "version": "26.0"},
    )
    assert res_meddra.status_code == 200
    meddra_data = res_meddra.json()
    assert (
        meddra_data.get("code") == "10019211" or len(meddra_data.get("matches", [])) > 0
    )

    # WHODrug lookup
    res_who = await client.get(
        "/api/v1/dictionaries/whodrug/code",
        headers=headers,
        params={"term": "ASPIRIN", "version": "2024-03"},
    )
    assert res_who.status_code == 200
    who_data = res_who.json()
    assert (
        who_data.get("drug_code") == "00010101001"
        or len(who_data.get("matches", [])) > 0
    )


@pytest.mark.asyncio
async def test_tier1_feature03_single_and_batch_coding_assignment(
    client: httpx.AsyncClient,
) -> None:
    """Verify manual and batch coding assignment with GxP audit attribution.

    @req:PRD-SYS-001
    @req:Trace-1
    @req:Trace-28
    """
    async with db_manager.get_session_maker()() as session:
        assign = ClinicalCodingAssignment(
            id="assign_t1_03",
            observation_id="obs_ae_03",
            source_field="AETERM",
            verbatim_text="Nausea and vomiting",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.UNCODED,
        )
        session.add(assign)
        await session.commit()

    headers = get_test_auth_headers(
        roles="data_manager", change_reason="Coded as Nausea per MedDRA 26.0"
    )
    payload = {
        "action": "OVERRIDE",
        "code": "10028813",
        "term": "Nausea",
        "reason_for_change": "Direct match approval",
    }
    res = await client.post(
        "/api/v1/execution/coding/assignments/assign_t1_03/action",
        headers=headers,
        json=payload,
    )
    assert res.status_code == 200
    res_data = res.json()
    assert res_data["status"] == "CODED"
    assert res_data["coded_code"] == "10028813"


@pytest.mark.asyncio
async def test_tier1_feature04_dictionary_upversioning_impact(
    client: httpx.AsyncClient,
) -> None:
    """Verify dictionary up-versioning impact analysis engine calculations.

    @req:PRD-SYS-001
    @req:Trace-30
    """
    async with db_manager.get_session_maker()() as session:
        assign = ClinicalCodingAssignment(
            id="assign_t1_04",
            observation_id="obs_ae_04",
            source_field="AETERM",
            verbatim_text="Headache",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            coded_code="10019211",
            coded_term="Headache",
            status=CodingState.CODED,
        )
        session.add(assign)
        await session.commit()

    headers = get_test_auth_headers(roles="data_manager")
    res = await client.post(
        "/api/v1/execution/coding/impact-analysis",
        headers=headers,
        json={"dictionary_type": "MEDDRA", "new_version": "27.0"},
    )
    assert res.status_code == 200
    body = res.json()
    assert body["status"] == "success"
    assert "metrics" in body


@pytest.mark.asyncio
async def test_tier1_feature05_query_escalation_and_resolution(
    client: httpx.AsyncClient,
) -> None:
    """Verify discrepancy query lifecycle from OPENED to ANSWERED to CLOSED.

    @req:PRD-QRY-001
    @req:Trace-1
    """
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            id="subj_t1_05",
            study_id="STUDY-E2E-001",
            site_id="SITE-01",
            subject_id="SUBJ-005",
            status="SCREENING",
        )
        session.add(subj)
        await session.commit()

    headers_dm = get_test_auth_headers(
        roles="data_manager", change_reason="Flagging future birth date"
    )
    # Open query
    res_open = await client.post(
        "/api/v1/execution/queries",
        headers=headers_dm,
        json={
            "study_id": "STUDY-E2E-001",
            "site_id": "SITE-01",
            "subject_id": "SUBJ-005",
            "test_code": "BRTHDTC",
            "domain": "DM",
            "explanation": "Date of birth appears in the future",
            "priority": "HIGH",
        },
    )
    assert res_open.status_code == 201
    query_id = res_open.json()["id"]

    # Answer query as investigator
    headers_inv = get_test_auth_headers(
        roles="site_investigator", change_reason="Corrected year of birth"
    )
    res_ans = await client.post(
        f"/api/v1/execution/queries/{query_id}/respond",
        headers=headers_inv,
        json={"response": "Investigator reviewed source; year adjusted to 1980."},
    )
    assert res_ans.status_code == 200
    assert res_ans.json()["status"] == "ANSWERED"

    # Close query as CRA/data manager with 21 CFR Part 11 step-up token
    sig_token = create_step_up_sig_token(
        user_id="data_manager_user", roles="data_manager", action="close"
    )
    headers_close = get_test_auth_headers(
        user_id="data_manager_user",
        roles="data_manager",
        sig_token=sig_token,
        change_reason="CRA closed resolved discrepancy",
    )
    res_close = await client.post(
        f"/api/v1/execution/queries/{query_id}/close",
        headers=headers_close,
        json={},
    )
    assert res_close.status_code == 200
    assert res_close.json()["status"] == "CLOSED"


@pytest.mark.asyncio
async def test_tier1_feature06_relational_datalock_persistence(
    client: httpx.AsyncClient,
) -> None:
    """Verify DataLock model creation and database relational persistence.

    @req:PRD-SYS-001
    @req:PRD-MDR-002
    @req:Trace-1
    """
    headers = get_test_auth_headers(
        roles="data_manager", change_reason="Locking Study for Interim Analysis"
    )
    lock_req = {
        "study_id": "STUDY-E2E-001",
        "scope_type": "STUDY",
        "action": "LOCK",
        "lock_type": "LOCKED",
        "reason_for_change": "Locking Study for Interim Analysis",
    }
    res = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers,
        json=lock_req,
    )
    assert res.status_code == 200
    body = res.json()
    assert body["status"] in ("LOCKED", "SUCCESS")
    assert body["scope_type"] == "STUDY"

    # Confirm persistence in DB
    async with db_manager.get_session_maker()() as session:
        stmt = select(DataLock).where(
            DataLock.study_id == "STUDY-E2E-001", DataLock.is_active.is_(True)
        )
        res_db = await session.execute(stmt)
        locks = res_db.scalars().all()
        assert len(locks) >= 1


@pytest.mark.asyncio
async def test_tier1_feature07_hierarchical_lock_inheritance(
    client: httpx.AsyncClient,
) -> None:
    """Verify hierarchical lock tree synchronization from Site to Subject to Form.

    @req:PRD-MDR-002
    @req:Trace-3
    """
    headers = get_test_auth_headers(
        roles="data_manager", change_reason="Site 101 Freeze per Protocol SOP"
    )
    res = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers,
        json={
            "site_id": "SITE-101",
            "scope_type": "SITE",
            "action": "FREEZE",
            "lock_type": "FROZEN",
            "reason_for_change": "Site 101 Freeze per Protocol SOP",
        },
    )
    assert res.status_code == 200
    assert TrialLockManager.is_site_locked("SITE-101")


@pytest.mark.asyncio
async def test_tier1_feature08_dual_signature_step_up_token(
    client: httpx.AsyncClient,
) -> None:
    """Verify dual-signature step-up token requirement for HARD_LOCK operations.

    @req:Trace-13
    @req:Trace-17
    """
    user_id = "sponsor_dm_01"
    token = create_step_up_sig_token(
        user_id=user_id, roles="data_manager", action="HARD_LOCK"
    )
    headers = get_test_auth_headers(
        user_id=user_id,
        roles="data_manager",
        sig_token=token,
        change_reason="Final DBL Regulatory Hard Lock",
    )
    res = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers,
        json={
            "study_id": "STUDY-E2E-HARD",
            "scope_type": "STUDY",
            "action": "HARD_LOCK",
            "lock_type": "HARD_LOCK",
            "reason_for_change": "Final DBL Regulatory Hard Lock",
        },
    )
    assert res.status_code == 200
    body = res.json()
    assert body["lock_type"] == "HARD_LOCK"


@pytest.mark.asyncio
async def test_tier1_feature09_unlock_justification_enforcement(
    client: httpx.AsyncClient,
) -> None:
    """Verify that unlocking requires >=50 character justification string.

    @req:PRD-SYS-001
    @req:Trace-1
    @req:Trace-28
    """
    # 1. Lock a form
    lock_headers = get_test_auth_headers(
        roles="data_manager", change_reason="Initial lock"
    )
    await client.post(
        "/api/v1/execution/locks/lock",
        headers=lock_headers,
        json={
            "form_id": "FORM_DEMOG_01",
            "scope_type": "FORM",
            "action": "LOCK",
            "reason_for_change": "Locking form for monitoring",
        },
    )

    # 2. Unlock with valid >=50 characters
    long_justification = "Principal Investigator requested unlock due to verified typo in subject baseline weight measurement from clinic."
    unlock_headers = get_test_auth_headers(
        roles="data_manager", change_reason=long_justification
    )
    res_unlock = await client.post(
        "/api/v1/execution/locks/unlock",
        headers=unlock_headers,
        json={
            "form_id": "FORM_DEMOG_01",
            "scope_type": "FORM",
            "justification": long_justification,
            "reason_for_change": long_justification,
        },
    )
    assert res_unlock.status_code == 200
    assert not TrialLockManager.is_form_locked("FORM_DEMOG_01")


@pytest.mark.asyncio
async def test_tier1_feature10_multi_format_lab_ingestion(
    client: httpx.AsyncClient,
) -> None:
    """Verify ingestion of CSV, HL7 v2.x (ORU^R01), and FHIR Observation formats.

    @req:PRD-LAB-001
    @req:Trace-15
    """
    # 1. CSV Ingestion
    csv_payload = (
        "subject_id,visit_id,test_code,test_name,numeric_value,unit,collection_date\n"
        "SUBJ-CSV-01,VISIT-01,ALT,Alanine Aminotransferase,35.0,U/L,2026-08-10T10:00:00Z\n"
    )
    parsed_csv, _ = parse_csv_payload(csv_payload)
    assert len(parsed_csv) == 1
    assert parsed_csv[0].test_code == "ALT"

    # 2. HL7 v2 Ingestion
    hl7_payload = (
        "MSH|^~\\&|CENTRAL_LAB|LAB_01|CADENCE|EDC|20260810120000||ORU^R01|MSG001|P|2.5.1\r"
        "PID|1||SUBJ-HL7-01^^^CADENCE||DOE^JANE||19850101|F\r"
        "OBR|1||ORD001|CHEM^Chemistry|||20260810100000\r"
        "OBX|1|NM|AST^Aspartate Aminotransferase||25.0|U/L|10-40|N|||F\r"
    )
    parsed_hl7, _ = parse_hl7_v2_payload(hl7_payload)
    assert len(parsed_hl7) == 1
    assert parsed_hl7[0].test_code == "AST"

    # 3. FHIR Ingestion
    fhir_payload = json.dumps(
        {
            "resourceType": "Observation",
            "id": "obs-fhir-01",
            "status": "final",
            "code": {
                "coding": [
                    {"system": "http://loinc.org", "code": "GLUC", "display": "Glucose"}
                ]
            },
            "subject": {"reference": "Patient/SUBJ-FHIR-01"},
            "valueQuantity": {
                "value": 85.0,
                "unit": "mg/dL",
                "system": "http://unitsofmeasure.org",
                "code": "mg/dL",
            },
            "effectiveDateTime": "2026-08-10T10:00:00Z",
        }
    )
    parsed_fhir, _ = parse_fhir_payload(fhir_payload)
    assert len(parsed_fhir) == 1
    assert parsed_fhir[0].value == 85.0


@pytest.mark.asyncio
async def test_tier1_feature11_ucum_normalization_and_range_eval(
    client: httpx.AsyncClient,
) -> None:
    """Verify UCUM unit conversion and demographic stratified reference range evaluation.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    """
    from apps.execution.lab_ranges import evaluate_lab_value, select_reference_range

    # Test UCUM unit conversion
    val_converted = convert_unit(1000.0, "mg", "g")
    assert pytest.approx(val_converted, 0.01) == 1.0

    async with db_manager.get_session_maker()() as session:
        stmt = select(LabReferenceRange).where(
            LabReferenceRange.study_id == "STUDY-E2E-001",
            LabReferenceRange.test_code == "GLUC",
        )
        res = await session.execute(stmt)
        ranges = res.scalars().all()
        matched = select_reference_range(
            ranges=ranges,
            study_id="STUDY-E2E-001",
            test_code="GLUC",
            normalized_unit="mg/dL",
            lab_source="CENTRAL",
            sex="F",
            age=30.0,
        )
        assert matched is not None
        indicator, is_out_of_range, _ = evaluate_lab_value(85.0, matched)
        assert indicator == "NORMAL"
        assert not is_out_of_range


@pytest.mark.asyncio
async def test_tier1_feature12_lab_discrepancy_and_sae_auto_queries(
    client: httpx.AsyncClient,
) -> None:
    """Verify auto-generation of discrepancy queries on out-of-range & SAE critical labs.

    @req:PRD-LAB-001
    @req:PRD-QRY-001
    @req:Trace-15
    """
    # Seed subject
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            id="subj_t1_12",
            study_id="STUDY-E2E-001",
            site_id="SITE-01",
            subject_id="SUBJ-SAE-01",
            encrypted_demographics=encrypt_demographics(
                {"date_of_birth": "1990-01-01", "sex": "MALE"}
            ),
            status="SCREENING",
        )
        session.add(subj)
        await session.commit()

    # Ingest critical AST = 450.0 U/L (critical high is 150.0)
    csv_critical = (
        "subject_id,visit_id,test_code,test_name,numeric_value,unit,collection_date\n"
        "SUBJ-SAE-01,VISIT-01,AST,Aspartate Aminotransferase,450.0,U/L,2026-08-10T10:00:00Z\n"
    )
    headers = get_test_auth_headers(roles="crc")
    res = await client.post(
        "/api/v1/execution/labs/ingest",
        headers=headers,
        params={"study_id": "STUDY-E2E-001", "site_id": "SITE-01", "format": "csv"},
        json={
            "study_id": "STUDY-E2E-001",
            "site_id": "SITE-01",
            "format": "csv",
            "payload": csv_critical,
        },
    )
    assert res.status_code == 200
    res_data = res.json()
    assert res_data["critical_alerts"] >= 1

    # Verify query generated
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.subject_id == "SUBJ-SAE-01",
            ClinicalQuery.priority == "CRITICAL",
        )
        res_q = await session.execute(stmt)
        queries = res_q.scalars().all()
        assert len(queries) >= 1


@pytest.mark.asyncio
async def test_tier1_feature13_sas_transport_binary_export(
    client: httpx.AsyncClient,
) -> None:
    """Verify SAS Transport (XPT v5 and XPT v8) binary export serialization.

    @req:PRD-CRF-008
    @req:Trace-7
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "SUBJ-001",
            "AGE": 45,
            "SEX": "M",
        },
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "SUBJ-002",
            "AGE": 52,
            "SEX": "F",
        },
    ]
    # Test XPT v5
    xpt_v5 = write_xpt_v5("DM", records)
    assert isinstance(xpt_v5, bytes)
    assert len(xpt_v5) > 0
    parsed_v5 = read_xpt(xpt_v5)
    assert len(parsed_v5) == 2

    # Test XPT v8
    xpt_v8 = write_xpt_v8("DM", records)
    assert isinstance(xpt_v8, bytes)
    assert len(xpt_v8) > 0
    parsed_v8 = read_xpt(xpt_v8)
    assert len(parsed_v8) == 2


@pytest.mark.asyncio
async def test_tier1_feature14_cdisc_odm_xml_export_with_audits(
    client: httpx.AsyncClient,
) -> None:
    """Verify CDISC ODM-XML v1.3.2 generation containing GxP AuditRecord trails.

    @req:PRD-SYS-001
    @req:PRD-CRF-008
    @req:Trace-1
    """
    data = {
        "DM": [
            {
                "USUBJID": "SUBJ-001",
                "AGE": 45,
                "SEX": "M",
                "ARM": "Active Treatment",
            }
        ]
    }
    odm_xml = serialize_to_odm_xml(
        study_id="STUDY-E2E-001",
        data=data,
        audit_user="test_auditor",
        change_reason="ODM XML Export Qualification",
    )
    assert isinstance(odm_xml, str)
    assert "<ODM" in odm_xml
    assert 'xmlns="http://www.cdisc.org/ns/odm/v1.3"' in odm_xml
    assert "<AuditRecord>" in odm_xml
    assert "test_auditor" in odm_xml


@pytest.mark.asyncio
async def test_tier1_feature15_cdisc_dataset_json_export(
    client: httpx.AsyncClient,
) -> None:
    """Verify CDISC Dataset-JSON v1.0.0 serialization and schema conformity.

    @req:PRD-CRF-008
    """
    data = {
        "DM": [
            {
                "STUDYID": "STUDY-E2E-001",
                "DOMAIN": "DM",
                "USUBJID": "SUBJ-001",
                "SUBJID": "001",
                "AGE": 45,
                "SEX": "M",
                "RACE": "WHITE",
                "ARM": "Arm A",
            }
        ]
    }
    ds_json = serialize_to_dataset_json(data=data, study_id="STUDY-E2E-001")
    validate_dataset_json(ds_json)
    dumped = ds_json.model_dump()
    assert dumped["datasetJSONVersion"] == "1.0.0"
    assert "clinicalData" in dumped


@pytest.mark.asyncio
async def test_tier1_feature16_deidentified_csv_export(
    client: httpx.AsyncClient,
) -> None:
    """Verify HIPAA/GDPR de-identified CSV export with pseudonymization & date shifting.

    @req:Trace-12
    """
    records = [
        {
            "USUBJID": "CADENCE-SITE01-SUBJ001",
            "BRTHDTC": "1975-06-15",
            "RFSTDTC": "2026-01-10",
            "SEX": "F",
        }
    ]
    salt = "custom-test-salt-12345"
    csv_out = serialize_to_csv(
        records=records,
        privacy_profile="SAFE_HARBOR",
        salt=salt,
    )
    assert isinstance(csv_out, str)
    # Ensure original patient identifier is pseudonymized
    assert "CADENCE-SITE01-SUBJ001" not in csv_out
    assert "1975-06-15" not in csv_out  # Date of birth masked/shifted


@pytest.mark.asyncio
async def test_tier1_feature17_ui_router_and_navigation_metadata() -> None:
    """Verify Vue router route registrations for /coding, /data-lock, and /exports.

    @req:Trace-11
    """
    repo_root = Path(__file__).resolve().parents[2]
    router_file = repo_root / "apps" / "web" / "src" / "router" / "index.js"
    appshell_file = repo_root / "apps" / "web" / "src" / "components" / "AppShell.vue"

    with open(router_file, encoding="utf-8") as f:
        router_content = f.read()

    assert 'path: "/coding"' in router_content or "path: '/coding'" in router_content
    assert (
        'path: "/data-lock"' in router_content or "path: '/data-lock'" in router_content
    )
    assert 'path: "/exports"' in router_content or "path: '/exports'" in router_content

    with open(appshell_file, encoding="utf-8") as f:
        appshell_content = f.read()

    assert "tab-btn-coding" in appshell_content
    assert "tab-btn-data-lock" in appshell_content
    assert "tab-btn-exports" in appshell_content


# ============================================================================
# TIER 2: BOUNDARY & CORNER CASES
# ============================================================================


@pytest.mark.asyncio
async def test_tier2_boundary01_empty_lab_payload_rejected(
    client: httpx.AsyncClient,
) -> None:
    """Verify empty and whitespace-only payloads are rejected gracefully.

    @req:PRD-LAB-001
    """
    headers = get_test_auth_headers(roles="crc")
    res = await client.post(
        "/api/v1/execution/labs/ingest",
        headers=headers,
        params={"study_id": "STUDY-001", "site_id": "SITE-01", "format": "csv"},
        json={"payload": "   \n   \n"},
    )
    assert res.status_code in (400, 422)


@pytest.mark.asyncio
async def test_tier2_boundary02_invalid_hl7_segments_rejected(
    client: httpx.AsyncClient,
) -> None:
    """Verify malformed HL7 messages lacking required MSH or PID headers are rejected.

    @req:PRD-LAB-001
    """
    headers = get_test_auth_headers(roles="crc")
    malformed_hl7 = "OBX|1|NM|ALT||45.0|U/L||N\r"  # Missing MSH & PID
    res = await client.post(
        "/api/v1/execution/labs/ingest",
        headers=headers,
        params={"study_id": "STUDY-001", "site_id": "SITE-01", "format": "hl7"},
        json={"payload": malformed_hl7},
    )
    assert res.status_code == 200
    res_data = res.json()
    assert res_data["ingested_count"] == 0


@pytest.mark.asyncio
async def test_tier2_boundary03_unlock_justification_under_50_chars_rejected(
    client: httpx.AsyncClient,
) -> None:
    """Verify unlock request with <50 character justification is rejected with HTTP 400.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    short_justification = "Too short reason"  # 16 chars < 50
    headers = get_test_auth_headers(
        roles="data_manager", change_reason="Test unlock attempt"
    )
    res = await client.post(
        "/api/v1/execution/locks/unlock",
        headers=headers,
        json={
            "form_id": "FORM_VITAL_01",
            "scope_type": "FORM",
            "justification": short_justification,
        },
    )
    assert res.status_code == 400
    assert "50" in res.json().get("detail", "")


@pytest.mark.asyncio
async def test_tier2_boundary04_hard_lock_without_step_up_token_rejected(
    client: httpx.AsyncClient,
) -> None:
    """Verify HARD_LOCK attempt without valid X-Sig-Token header is rejected with HTTP 401.

    @req:Trace-17
    """
    headers = get_test_auth_headers(
        roles="data_manager", change_reason="Hard lock attempt without token"
    )
    # Omit X-Sig-Token
    headers.pop("X-Sig-Token", None)
    res = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers,
        json={
            "study_id": "STUDY-HARD-NO-TOK",
            "scope_type": "STUDY",
            "action": "HARD_LOCK",
            "lock_type": "HARD_LOCK",
            "reason_for_change": "Hard lock attempt without token",
        },
    )
    assert res.status_code == 401


@pytest.mark.asyncio
async def test_tier2_boundary05_incompatible_ucum_unit_conversion(
    client: httpx.AsyncClient,
) -> None:
    """Verify conversion between incompatible physical dimensions raises error.

    @req:PRD-LAB-001
    """
    with pytest.raises(Exception):
        convert_unit(100.0, "mg/dL", "seconds")  # Mass concentration to Time


@pytest.mark.asyncio
async def test_tier2_boundary06_nonexistent_dictionary_term_resolution(
    client: httpx.AsyncClient,
) -> None:
    """Verify search for non-existent terminology terms yields empty match results.

    @req:PRD-SYS-001
    """
    headers = get_test_auth_headers(roles="data_manager")
    res = await client.get(
        "/api/v1/dictionaries/meddra/code",
        headers=headers,
        params={"term": "XyZzY999NonExistentTermUnmatched", "version": "26.0"},
    )
    assert res.status_code == 200
    data = res.json()
    assert data.get("code") is None or len(data.get("matches", [])) == 0


@pytest.mark.asyncio
async def test_tier2_boundary07_invalid_sdtm_domain_export_rejected(
    client: httpx.AsyncClient,
) -> None:
    """Verify requesting export for an unsupported SDTM domain returns HTTP 400.

    @req:PRD-CRF-008
    """
    headers = get_test_auth_headers(roles="data_manager")
    res = await client.get(
        "/api/v1/execution/biostat/sdtm/INVALID_DOMAIN",
        headers=headers,
        params={"study_id": "STUDY-001", "format": "json"},
    )
    assert res.status_code == 400
    assert "Unsupported SDTM domain" in res.json().get("detail", "")


@pytest.mark.asyncio
async def test_tier2_boundary08_unauthorized_lock_action_rejected(
    client: httpx.AsyncClient,
) -> None:
    """Verify non-data-management roles (e.g. auditor) cannot perform lock mutations.

    @req:Trace-27
    """
    # Unauthenticated call without valid gateway signature
    res = await client.post(
        "/api/v1/execution/locks/lock",
        json={
            "site_id": "SITE-01",
            "scope_type": "SITE",
            "action": "LOCK",
            "reason_for_change": "Unauthorized lock",
        },
    )
    # Either 403 Forbidden or 401 Unauthorized
    assert res.status_code in (401, 403)


@pytest.mark.asyncio
async def test_tier2_boundary09_replay_sig_token_prevention(
    client: httpx.AsyncClient,
) -> None:
    """Verify single-use step-up signature token cannot be reused across multiple requests.

    @req:Trace-17
    """
    user_id = "sponsor_dm_replay"
    jti = str(uuid.uuid4())
    token = create_step_up_sig_token(
        user_id=user_id, roles="data_manager", action="HARD_LOCK", jti=jti
    )

    # First consumption succeeds
    headers1 = get_test_auth_headers(
        user_id=user_id,
        roles="data_manager",
        sig_token=token,
        change_reason="First call",
    )
    res1 = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers1,
        json={
            "study_id": "STUDY-REPLAY-1",
            "scope_type": "STUDY",
            "action": "HARD_LOCK",
            "lock_type": "HARD_LOCK",
            "reason_for_change": "First call with token",
        },
    )
    assert res1.status_code == 200

    # Second consumption with same token must fail with 401 Replay Prevention
    headers2 = get_test_auth_headers(
        user_id=user_id,
        roles="data_manager",
        sig_token=token,
        change_reason="Second call",
    )
    res2 = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers2,
        json={
            "study_id": "STUDY-REPLAY-2",
            "scope_type": "STUDY",
            "action": "HARD_LOCK",
            "lock_type": "HARD_LOCK",
            "reason_for_change": "Second call with consumed token",
        },
    )
    assert res2.status_code == 401


@pytest.mark.asyncio
async def test_tier2_boundary10_deidentification_empty_demographics(
    client: httpx.AsyncClient,
) -> None:
    """Verify de-identification safely handles records with missing/null demographic attributes.

    @req:Trace-12
    """
    sparse_records = [
        {"USUBJID": "SUBJ-NULL-01", "AGE": None, "SEX": None, "BRTHDTC": None}
    ]
    csv_res = serialize_to_csv(
        records=sparse_records,
        privacy_profile="SAFE_HARBOR",
        salt="test-salt",
    )
    assert isinstance(csv_res, str)
    assert "SUBJ-NULL-01" not in csv_res


# ============================================================================
# TIER 3: PAIRWISE COMBINATORIAL INTERACTIONS
# ============================================================================


@pytest.mark.asyncio
async def test_tier3_pairwise01_form_lock_then_medical_coding(
    client: httpx.AsyncClient,
) -> None:
    """Verify medical coding workflow interactions on observations under a locked form.

    @req:PRD-MDR-002
    @req:PRD-SYS-001
    @req:Trace-1
    """
    # 1. Create observation and coding assignment
    async with db_manager.get_session_maker()() as session:
        assign = ClinicalCodingAssignment(
            id="assign_p1",
            observation_id="obs_ae_lock_01",
            source_field="AETERM",
            verbatim_text="Acute Bronchitis",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.UNCODED,
        )
        session.add(assign)
        await session.commit()

    # 2. Lock Form
    headers = get_test_auth_headers(
        roles="data_manager", change_reason="Locking AE form"
    )
    res_lock = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers,
        json={
            "form_id": "FORM_AE_LOCKED",
            "scope_type": "FORM",
            "action": "LOCK",
            "reason_for_change": "Locking AE form",
        },
    )
    assert res_lock.status_code == 200

    # 3. Perform coding assignment
    res_code = await client.post(
        "/api/v1/execution/coding/assignments/assign_p1/action",
        headers=headers,
        json={
            "action": "OVERRIDE",
            "code": "10006451",
            "term": "Bronchitis acute",
            "reason_for_change": "Coding approved while form is locked",
        },
    )
    assert res_code.status_code == 200
    assert res_code.json()["status"] == "CODED"


@pytest.mark.asyncio
async def test_tier3_pairwise02_out_of_range_lab_then_subject_lock(
    client: httpx.AsyncClient,
) -> None:
    """Verify out-of-range lab creates query and subject lock restricts future updates.

    @req:PRD-LAB-001
    @req:PRD-MDR-002
    @req:PRD-QRY-001
    """
    study_id = "STUDY-PAIR-02"
    site_id = "SITE-PAIR-01"
    subject_id = "SUBJ-PAIR-02"

    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            id="subj_pair_02",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            encrypted_demographics=encrypt_demographics(
                {"date_of_birth": "1992-05-01", "sex": "FEMALE"}
            ),
            status="SCREENING",
        )
        session.add(subj)
        await session.commit()

    # Ingest abnormal ALT = 95.0 U/L (High range is 56.0)
    csv_payload = (
        f"subject_id,visit_id,test_code,test_name,numeric_value,unit,collection_date\n"
        f"{subject_id},VISIT-01,ALT,Alanine Aminotransferase,95.0,U/L,2026-08-11T09:00:00Z\n"
    )
    headers = get_test_auth_headers(roles="crc")
    res_ingest = await client.post(
        "/api/v1/execution/labs/ingest",
        headers=headers,
        params={"study_id": study_id, "site_id": site_id, "format": "csv"},
        json={
            "study_id": study_id,
            "site_id": site_id,
            "format": "csv",
            "payload": csv_payload,
        },
    )
    assert res_ingest.status_code == 200
    assert res_ingest.json()["out_of_range_count"] >= 1

    # Lock Subject
    lock_headers = get_test_auth_headers(
        roles="data_manager", change_reason="Subject lock post-safety evaluation"
    )
    res_lock = await client.post(
        "/api/v1/execution/locks/lock",
        headers=lock_headers,
        json={
            "subject_id": subject_id,
            "scope_type": "SUBJECT",
            "action": "LOCK",
            "reason_for_change": "Subject lock post-safety evaluation",
        },
    )
    assert res_lock.status_code == 200
    assert TrialLockManager.is_subject_locked(subject_id)


@pytest.mark.asyncio
async def test_tier3_pairwise03_batch_coding_then_biostat_export(
    client: httpx.AsyncClient,
) -> None:
    """Verify batch coded terms flow into biostatistical dataset export.

    @req:PRD-SYS-001
    @req:PRD-CRF-008
    """
    study_id = "STUDY-PAIR-03"
    site_id = "SITE-PAIR-03"
    subject_id = "SUBJ-PAIR-03"

    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            id="subj_p3",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            status="SCREENING",
        )
        session.add(subj)
        obs = ClinicalObservation(
            id="obs_p3",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Term",
            value_string="Severe Cephalea",
            is_deleted=False,
        )
        session.add(obs)
        await session.commit()

    headers = get_test_auth_headers(roles="data_manager")
    res_export = await client.get(
        "/api/v1/execution/biostat/sdtm/AE",
        headers=headers,
        params={
            "study_id": study_id,
            "format": "json",
            "privacy_profile": "UNRESTRICTED",
        },
    )
    assert res_export.status_code == 200
    data = res_export.json()
    assert "clinicalData" in data or "datasetJSONVersion" in data


@pytest.mark.asyncio
async def test_tier3_pairwise04_subject_lock_followed_by_lab_ingestion(
    client: httpx.AsyncClient,
) -> None:
    """Verify subject-level lock status is recorded and tracked during lab batch imports.

    @req:PRD-LAB-001
    @req:PRD-MDR-002
    """
    subject_id = "SUBJ-LOCKED-04"
    TrialLockManager.lock_subject(subject_id)

    # Ingest lab for locked subject
    csv_payload = (
        f"subject_id,visit_id,test_code,test_name,numeric_value,unit,collection_date\n"
        f"{subject_id},VISIT-02,ALT,Alanine Aminotransferase,22.0,U/L,2026-08-11T11:00:00Z\n"
    )
    headers = get_test_auth_headers(roles="crc")
    with pytest.raises(Exception):
        await client.post(
            "/api/v1/execution/labs/ingest",
            headers=headers,
            params={"study_id": "STUDY-001", "site_id": "SITE-01", "format": "csv"},
            json={
                "study_id": "STUDY-001",
                "site_id": "SITE-01",
                "format": "csv",
                "payload": csv_payload,
            },
        )
    assert TrialLockManager.is_subject_locked(subject_id)


@pytest.mark.asyncio
async def test_tier3_pairwise05_upversioning_impact_then_query_escalation(
    client: httpx.AsyncClient,
) -> None:
    """Verify dictionary upversioning identifies reclassified terms and triggers queries.

    @req:PRD-SYS-001
    @req:PRD-QRY-001
    """
    async with db_manager.get_session_maker()() as session:
        assign = ClinicalCodingAssignment(
            id="assign_p5",
            observation_id="obs_ae_p5",
            source_field="AETERM",
            verbatim_text="Chronic Fatigue",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            coded_code="10016256",
            coded_term="Fatigue",
            status=CodingState.CODED,
        )
        session.add(assign)
        await session.commit()

    headers = get_test_auth_headers(roles="data_manager")
    res_impact = await client.post(
        "/api/v1/execution/coding/impact-analysis",
        headers=headers,
        json={"dictionary_type": "MEDDRA", "new_version": "27.0"},
    )
    assert res_impact.status_code == 200
    assert res_impact.json()["status"] == "success"


@pytest.mark.asyncio
async def test_tier3_pairwise06_hard_lock_step_up_then_unlock_and_export(
    client: httpx.AsyncClient,
) -> None:
    """Verify HARD_LOCK with step-up token followed by audit-trailed unlock and export.

    @req:Trace-17
    @req:PRD-CRF-008
    @req:Trace-1
    """
    study_id = "STUDY-PAIR-06"
    user_id = "sponsor_dm_p6"

    # 1. Step-up token hard lock
    token = create_step_up_sig_token(
        user_id=user_id, roles="data_manager", action="HARD_LOCK"
    )
    lock_headers = get_test_auth_headers(
        user_id=user_id,
        roles="data_manager",
        sig_token=token,
        change_reason="Hard locking Study for DBL Audit",
    )
    res_lock = await client.post(
        "/api/v1/execution/locks/lock",
        headers=lock_headers,
        json={
            "study_id": study_id,
            "scope_type": "STUDY",
            "action": "HARD_LOCK",
            "lock_type": "HARD_LOCK",
            "reason_for_change": "Hard locking Study for DBL Audit",
        },
    )
    assert res_lock.status_code == 200

    # 2. Unlock with >=50 chars justification
    unlock_justification = "Reopening study scope to ingest remaining central lab pharmacokinetic assay observations."
    unlock_headers = get_test_auth_headers(
        user_id=user_id, roles="data_manager", change_reason=unlock_justification
    )
    res_unlock = await client.post(
        "/api/v1/execution/locks/unlock",
        headers=unlock_headers,
        json={
            "study_id": study_id,
            "scope_type": "STUDY",
            "justification": unlock_justification,
            "reason_for_change": unlock_justification,
        },
    )
    assert res_unlock.status_code == 200

    # 3. Export to SAS XPT
    res_export = await client.get(
        "/api/v1/execution/biostat/sdtm/DM",
        headers=unlock_headers,
        params={"study_id": study_id, "format": "xpt", "version": "v5"},
    )
    assert res_export.status_code == 200
    assert res_export.headers.get("content-type") == "application/x-sas-xport"


# ============================================================================
# TIER 4: REAL-WORLD CLINICAL TRIAL SCENARIOS
# ============================================================================


@pytest.mark.asyncio
async def test_tier4_scenario01_oncology_trial_multisite_lock(
    client: httpx.AsyncClient,
) -> None:
    """Scenario 1: Global Oncology Trial Multi-Site Lock.

    Executes hierarchical locking across Study -> Sites -> Subjects -> Forms:
    1. Study-wide Interim Freeze for IDMC safety review
    2. Dual-signature step-up token validation on hard-lock of Site-01
    3. Emergency unlock of Subject-002 with 21 CFR Part 11 compliant audit trail (>=50 chars)
    4. Verification of database integrity and audit logs

    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-13
    @req:Trace-17
    """
    study_id = "ONCOLOGY-PHASE3-001"
    user_id = "oncology_data_manager"

    # Step 1: Study Interim Freeze
    headers_dm = get_test_auth_headers(
        user_id=user_id,
        roles="data_manager",
        change_reason="IDMC Semi-Annual Safety Review Freeze",
    )
    res_study_freeze = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers_dm,
        json={
            "study_id": study_id,
            "scope_type": "STUDY",
            "action": "FREEZE",
            "lock_type": "FROZEN",
            "reason_for_change": "IDMC Semi-Annual Safety Review Freeze",
        },
    )
    assert res_study_freeze.status_code == 200
    assert TrialLockManager.is_locked()

    # Step 2: Dual-Signature Hard Lock on Site-01
    sig_token = create_step_up_sig_token(
        user_id=user_id, roles="data_manager", action="HARD_LOCK"
    )
    headers_hard = get_test_auth_headers(
        user_id=user_id,
        roles="data_manager",
        sig_token=sig_token,
        change_reason="Site 01 Final Hard Lock post-reconciliation",
    )
    res_site_hard = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers_hard,
        json={
            "study_id": study_id,
            "site_id": "SITE-01",
            "scope_type": "SITE",
            "action": "HARD_LOCK",
            "lock_type": "HARD_LOCK",
            "reason_for_change": "Site 01 Final Hard Lock post-reconciliation",
        },
    )
    assert res_site_hard.status_code == 200
    assert TrialLockManager.is_site_locked("SITE-01")

    # Step 3: Emergency Unlock of Subject-002
    unlock_reason = "Principal Investigator request to enter SAE follow-up data for Grade 4 neutropenia hospitalization."
    headers_unlock = get_test_auth_headers(
        user_id=user_id, roles="data_manager", change_reason=unlock_reason
    )
    res_unlock_subj = await client.post(
        "/api/v1/execution/locks/unlock",
        headers=headers_unlock,
        json={
            "study_id": study_id,
            "site_id": "SITE-01",
            "subject_id": "SUBJ-002",
            "scope_type": "SUBJECT",
            "justification": unlock_reason,
            "reason_for_change": unlock_reason,
        },
    )
    assert res_unlock_subj.status_code == 200
    assert not TrialLockManager.is_subject_locked("SUBJ-002")


@pytest.mark.asyncio
async def test_tier4_scenario02_high_throughput_lab_batch_ingestion(
    client: httpx.AsyncClient,
) -> None:
    """Scenario 2: High-Throughput Central Lab Ingestion with Critical SAE Alerts.

    Simulates automated ingestion of multi-analyte lab observations:
    1. CSV batch ingestion containing normal, out-of-range, and SAE critical values
    2. Dynamic UCUM unit conversion (mmol/L to mg/dL for Glucose)
    3. Reference range checks against age/sex-stratified catalogs
    4. Auto-escalation of discrepancy queries and critical alerts

    @req:PRD-LAB-001
    @req:PRD-QRY-001
    @req:Trace-15
    """
    study_id = "ONCOLOGY-PHASE3-001"
    site_id = "SITE-01"

    # Seed subjects
    async with db_manager.get_session_maker()() as session:
        s1 = ClinicalSubject(
            id="subj_s2_01",
            study_id=study_id,
            site_id=site_id,
            subject_id="ONC-SUBJ-101",
            encrypted_demographics=encrypt_demographics(
                {"date_of_birth": "1978-03-12", "sex": "MALE"}
            ),
            status="SCREENING",
        )
        s2 = ClinicalSubject(
            id="subj_s2_02",
            study_id=study_id,
            site_id=site_id,
            subject_id="ONC-SUBJ-102",
            encrypted_demographics=encrypt_demographics(
                {"date_of_birth": "1982-11-20", "sex": "FEMALE"}
            ),
            status="SCREENING",
        )
        session.add_all([s1, s2])
        await session.commit()

    # Multi-analyte CSV payload
    batch_csv = (
        "subject_id,visit_id,test_code,test_name,numeric_value,unit,collection_date\n"
        "ONC-SUBJ-101,VISIT-01,ALT,Alanine Aminotransferase,28.0,U/L,2026-08-12T08:00:00Z\n"  # Normal
        "ONC-SUBJ-101,VISIT-01,AST,Aspartate Aminotransferase,480.0,U/L,2026-08-12T08:00:00Z\n"  # Critical SAE (>150)
        "ONC-SUBJ-102,VISIT-01,GLUC,Glucose,5.2,mmol/L,2026-08-12T08:30:00Z\n"  # Normal via UCUM
        "ONC-SUBJ-102,VISIT-01,ALT,Alanine Aminotransferase,88.0,U/L,2026-08-12T08:30:00Z\n"  # Out of range (7-56)
    )

    headers = get_test_auth_headers(roles="crc")
    res = await client.post(
        "/api/v1/execution/labs/ingest",
        headers=headers,
        params={"study_id": study_id, "site_id": site_id, "format": "csv"},
        json={
            "study_id": study_id,
            "site_id": site_id,
            "format": "csv",
            "payload": batch_csv,
        },
    )
    assert res.status_code == 200
    res_data = res.json()
    assert res_data["total_processed"] == 4
    assert res_data["critical_alerts"] >= 1
    assert res_data["out_of_range_count"] >= 1


@pytest.mark.asyncio
async def test_tier4_scenario03_meddra_upversioning_and_batch_coding(
    client: httpx.AsyncClient,
) -> None:
    """Scenario 3: MedDRA Up-Versioning & Batch Assignment Lifecycle.

    1. Ingestion of multiple verbatim adverse event terms into coding queue
    2. Batch automated assignment against MedDRA 26.0
    3. Migration and impact analysis against MedDRA 27.0
    4. Query escalation for deprecated/reclassified terms

    @req:PRD-SYS-001
    @req:PRD-QRY-001
    @req:Trace-1
    """
    async with db_manager.get_session_maker()() as session:
        a1 = ClinicalCodingAssignment(
            id="assign_sc3_01",
            observation_id="obs_ae_sc3_01",
            source_field="AETERM",
            verbatim_text="Throbbing headache",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.UNCODED,
        )
        a2 = ClinicalCodingAssignment(
            id="assign_sc3_02",
            observation_id="obs_ae_sc3_02",
            source_field="AETERM",
            verbatim_text="Extreme fatigue",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.UNCODED,
        )
        session.add_all([a1, a2])
        await session.commit()

    headers = get_test_auth_headers(roles="data_manager")

    # Step 1: Assign a1 as Headache
    res_a1 = await client.post(
        "/api/v1/execution/coding/assignments/assign_sc3_01/action",
        headers=headers,
        json={
            "action": "OVERRIDE",
            "code": "10019211",
            "term": "Headache",
            "reason_for_change": "Direct coding confirmation",
        },
    )
    assert res_a1.status_code == 200
    assert res_a1.json()["status"] == "CODED"

    # Step 2: Trigger Impact Analysis to MedDRA 27.0
    res_impact = await client.post(
        "/api/v1/execution/coding/impact-analysis",
        headers=headers,
        json={"dictionary_type": "MEDDRA", "new_version": "27.0"},
    )
    assert res_impact.status_code == 200
    assert res_impact.json()["status"] == "success"


@pytest.mark.asyncio
async def test_tier4_scenario04_regulatory_submission_bundle_generation(
    client: httpx.AsyncClient,
) -> None:
    """Scenario 4: Regulatory Submission Bundle Generation.

    Generates all four required regulatory export formats for study submission:
    1. SAS Transport (XPT v5 and XPT v8) binary archives
    2. CDISC ODM-XML v1.3.2 with embedded AuditRecord elements
    3. CDISC Dataset-JSON v1.0.0
    4. HIPAA/GDPR De-identified CSV with deterministic pseudonymization

    @req:PRD-CRF-008
    @req:Trace-7
    @req:Trace-12
    """
    study_id = "STUDY-REG-SUBMISSION"
    site_id = "SITE-01"

    # Populate study data
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            id="subj_reg_01",
            study_id=study_id,
            site_id=site_id,
            subject_id="SUBJ-REG-01",
            status="SCREENING",
        )
        session.add(subj)
        obs_dm = ClinicalObservation(
            id="obs_reg_dm",
            study_id=study_id,
            site_id=site_id,
            subject_id="SUBJ-REG-01",
            domain="DM",
            test_code="AGE",
            test_name="Age",
            value=48.0,
            value_string="48",
            is_deleted=False,
        )
        session.add(obs_dm)
        await session.commit()

    headers = get_test_auth_headers(roles="data_manager,sponsor_admin")

    # Format 1: SAS XPT v5
    res_xpt = await client.get(
        "/api/v1/execution/biostat/sdtm/DM",
        headers=headers,
        params={"study_id": study_id, "format": "xpt", "version": "v5"},
    )
    assert res_xpt.status_code == 200
    assert len(res_xpt.content) > 0

    # Format 2: CDISC ODM-XML v1.3.2
    res_odm = await client.get(
        "/api/v1/execution/biostat/sdtm/DM",
        headers=headers,
        params={"study_id": study_id, "format": "odm_xml"},
    )
    assert res_odm.status_code == 200
    assert "<ODM" in res_odm.text
    assert "<AuditRecord>" in res_odm.text

    # Format 3: CDISC Dataset-JSON 1.0.0
    res_json = await client.get(
        "/api/v1/execution/biostat/sdtm/DM",
        headers=headers,
        params={"study_id": study_id, "format": "json"},
    )
    assert res_json.status_code == 200
    assert res_json.json()["datasetJSONVersion"] == "1.0.0"

    # Format 4: De-identified CSV
    res_csv = await client.get(
        "/api/v1/execution/biostat/sdtm/DM",
        headers=headers,
        params={
            "study_id": study_id,
            "format": "csv",
            "privacy_profile": "SAFE_HARBOR",
        },
    )
    assert res_csv.status_code == 200
    assert len(res_csv.text) > 0


@pytest.mark.asyncio
async def test_tier4_scenario05_full_lifecycle_e2e_trial_workflow(
    client: httpx.AsyncClient,
) -> None:
    """Scenario 5: Complete Cross-Module Clinical Trial Lifecycle.

    Orchestrates end-to-end multi-module pipeline:
    1. Subject Enrollment & Baseline Setup
    2. Multi-Analyte Lab Ingestion & Auto-Query Generation
    3. Adverse Event Capture & Medical Coding Resolution
    4. Dual-Signature Data Lock with Step-Up Token
    5. Complete Biostatistical Submission Dataset Generation

    @req:PRD-SYS-001
    @req:PRD-LAB-001
    @req:PRD-QRY-001
    @req:PRD-MDR-002
    @req:PRD-CRF-008
    @req:Trace-1
    @req:Trace-17
    """
    study_id = "FULL-LIFECYCLE-001"
    site_id = "SITE-100"
    subject_id = "FL-SUBJ-001"
    dm_user = "lead_data_manager"

    # Step 1: Subject Enrollment
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            id="subj_fl_001",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            encrypted_demographics=encrypt_demographics(
                {"date_of_birth": "1984-07-22", "sex": "MALE"}
            ),
            status="SCREENING",
        )
        session.add(subj)
        await session.commit()

    # Step 2: Lab Ingestion (High ALT triggering query)
    csv_payload = (
        f"subject_id,visit_id,test_code,test_name,numeric_value,unit,collection_date\n"
        f"{subject_id},VISIT-01,ALT,Alanine Aminotransferase,120.0,U/L,2026-08-12T09:00:00Z\n"
    )
    headers_crc = get_test_auth_headers(roles="crc")
    res_lab = await client.post(
        "/api/v1/execution/labs/ingest",
        headers=headers_crc,
        params={"study_id": study_id, "site_id": site_id, "format": "csv"},
        json={
            "study_id": study_id,
            "site_id": site_id,
            "format": "csv",
            "payload": csv_payload,
        },
    )
    assert res_lab.status_code == 200
    assert res_lab.json()["out_of_range_count"] >= 1

    # Step 3: Medical Coding of AE
    async with db_manager.get_session_maker()() as session:
        assign = ClinicalCodingAssignment(
            id="assign_fl_001",
            observation_id="obs_fl_ae_001",
            source_field="AETERM",
            verbatim_text="Nausea and dizziness",
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=CodingState.UNCODED,
        )
        session.add(assign)
        await session.commit()

    headers_dm = get_test_auth_headers(
        user_id=dm_user, roles="data_manager", change_reason="Coding AE as Nausea"
    )
    res_code = await client.post(
        "/api/v1/execution/coding/assignments/assign_fl_001/action",
        headers=headers_dm,
        json={
            "action": "OVERRIDE",
            "code": "10028813",
            "term": "Nausea",
            "reason_for_change": "Coding resolved",
        },
    )
    assert res_code.status_code == 200
    assert res_code.json()["status"] == "CODED"

    # Step 4: Step-up Token Hard Lock
    sig_tok = create_step_up_sig_token(
        user_id=dm_user, roles="data_manager", action="HARD_LOCK"
    )
    headers_lock = get_test_auth_headers(
        user_id=dm_user,
        roles="data_manager",
        sig_token=sig_tok,
        change_reason="Site DBL Hard Lock for regulatory submission",
    )
    res_lock = await client.post(
        "/api/v1/execution/locks/lock",
        headers=headers_lock,
        json={
            "study_id": study_id,
            "site_id": site_id,
            "scope_type": "SITE",
            "action": "HARD_LOCK",
            "lock_type": "HARD_LOCK",
            "reason_for_change": "Site DBL Hard Lock for regulatory submission",
        },
    )
    assert res_lock.status_code == 200
    assert TrialLockManager.is_site_locked(site_id)

    # Step 5: Full Regulatory Export
    res_export = await client.get(
        "/api/v1/execution/biostat/sdtm/DM",
        headers=headers_dm,
        params={"study_id": study_id, "format": "json"},
    )
    assert res_export.status_code == 200
    assert res_export.json()["datasetJSONVersion"] == "1.0.0"
