import copy
import hashlib
import hmac
import json
import time

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from jose import jwt
from sqlalchemy import select, text

# First-party imports
from apps.designer.db import (
    MOCK_STUDIES,
    MOCK_STUDY_VERSIONS,
)
from apps.designer.main import app as designer_app
from apps.etmf.database import db_manager as etmf_db_manager
from apps.etmf.main import app as etmf_app
from apps.etmf.models import (
    Base as EtmfBase,
)
from apps.etmf.models import (
    DocumentQCTransition,
)
from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import (
    Base as ExecBase,
)
from apps.execution.database.models import (
    ClinicalObservation,
)
from apps.execution.main import app as exec_app
from apps.execution.migration_rules import reconcile_observations

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_designer_auth_headers(
    user_id="test_designer",
    roles="STUDY_DESIGNER",
    change_reason="Study versioning operations",
    action_path=None,
    sig_token_custom=None,
):
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
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if sig_token_custom:
        headers["X-Sig-Token"] = sig_token_custom
    elif action_path:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": action_path,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
            "jti": f"jti-{time.time()}-{hash(action_path)}-{time.process_time()}",
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
        headers["X-Sig-Token"] = sig_token
    return headers


def get_exec_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
    """Generate Gateway signature-compliant authentication headers."""
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
async def setup_test_databases():
    """Setup in-memory SQLite databases before each test and clear down after."""
    # Initialize Downstream Trial Execution DB
    exec_db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with exec_db_manager.engine.begin() as conn:
        if exec_db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(ExecBase.metadata.create_all)

    # Initialize eTMF DB
    etmf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(EtmfBase.metadata.create_all)

    yield

    # Clean up Execution DB
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()

    # Clean up eTMF DB
    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(EtmfBase.metadata.drop_all)
    await etmf_db_manager.close()


# =====================================================================
# 1. DESIGNER VERSION & AMENDMENT VALIDATION (PRD-MDR-002)
# =====================================================================


@pytest.mark.asyncio
async def test_designer_amendment_immutability_and_race_safety():
    """
    Validate that study designs are properly frozen/LOCKED and that
    race safety / concurrency conflicts prevent parallel updates/duplicate creations.

    Requirements: PRD-MDR-002
    """
    study_id = "isolation_race_amend_study"
    MOCK_STUDY_VERSIONS[study_id] = []

    # Seed mock study projection so that the endpoints can find it

    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        # Create a DRAFT study version (first index)
        res_v1 = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_draft",
                "version_tag": "1.0",
                "status": "DRAFT",
                "version_index": 1,
            },
            headers=get_designer_auth_headers(),
        )
        assert res_v1.status_code == 201

        # Try to create a duplicate version index or tag -> Should raise 409 conflict
        res_dup = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_draft_dup",
                "version_tag": "1.0",
                "status": "DRAFT",
                "version_index": 1,
            },
            headers=get_designer_auth_headers(),
        )
        assert res_dup.status_code == 409
        assert "CONCURRENT_LOCKING_CONFLICT" in res_dup.json()["detail"]

        # Advance study to LOCKED state to trigger immutability constraints
        res_v2 = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_locked",
                "version_tag": "2.0",
                "status": "LOCKED",
                "version_index": 2,
            },
            headers=get_designer_auth_headers(),
        )
        assert res_v2.status_code == 201

        # Attempt to create any further rules under a LOCKED design -> Should be blocked with 403
        rule_payload = {
            "type": "skip_logic",
            "condition": {
                "type": "comparison",
                "operator": "==",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "act_1"}},
                    {"type": "constant", "value": "N"},
                ],
            },
            "action": "hide",
            "target_field": "act_2",
        }
        res_fail_rule = await client.post(
            f"/api/v1/studies/{study_id}/rules",
            json=rule_payload,
            headers=get_designer_auth_headers(),
        )
        assert res_fail_rule.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail_rule.json()["detail"]


@pytest.mark.asyncio
async def test_designer_amendment_signature_validation():
    """
    Validate that loading, amending, or upgrading a study design version enforces
    valid canonical payload signatures and strictly rejects tampered/un-signed records.

    Requirements: PRD-MDR-002
    """
    study_id = "signature_tamper_test_study"
    MOCK_STUDY_VERSIONS[study_id] = []

    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        # Establish parent version
        res_parent = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_parent_sig",
                "version_tag": "2.1",
                "status": "LOCKED",
                "version_index": 2,
            },
            headers=get_designer_auth_headers(),
        )
        assert res_parent.status_code == 201

        # Tamper with the canonical signature of the parent version inside Mock DB
        assert len(MOCK_STUDY_VERSIONS[study_id]) > 0
        MOCK_STUDY_VERSIONS[study_id][0]["signature"] = "tampered-signature-invalid-1"

        # Attempting to amend this version must be rejected since signature integrity is broken
        res_amend = await client.post(
            f"/api/designer/protocols/{study_id}/amend",
            json={"amendment_type": "clinical-amendment"},
            headers=get_designer_auth_headers(),
        )
        assert res_amend.status_code == 400
        assert "INVALID_OR_MISSING_SIGNATURE" in res_amend.json()["detail"]


# =====================================================================
# 2. EXACT-VERSION CONSENT & RE-CONSENT GATING (PRD-SUB-007)
# =====================================================================


@pytest.mark.asyncio
async def test_exact_version_consent_and_reconsent_gating():
    """
    Validate exact-version re-consent gating to ensure subject-level clinical write operations
    are completely blocked if a newer protocol version requiring re-consent has been activated
    but the subject has not yet signed the new consent form.

    Requirements: PRD-SUB-007
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Create clinical subject
        subject_payload = {
            "subject_id": "SUBJ-GATE-Y",
            "study_id": "STUDY-GATE",
            "demographics": {
                "name": "Alice Smith",
                "birthdate": "1995-05-15",
                "gender": "F",
                "race": "Asian",
            },
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_subj.status_code == 200

        # Sign-off initial version 1.0 consent
        consent_v1_payload = {
            "protocol_version": {
                "study_id": "STUDY-GATE",
                "version_tag": "1.0",
                "version_index": 1,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        res_consent_v1 = await client.post(
            "/api/v1/execution/subjects/SUBJ-GATE-Y/consent",
            json=consent_v1_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_consent_v1.status_code == 200

        # Record a newer version 2.0 requiring re-consent
        reconsent_v2_payload = {
            "protocol_version": {
                "study_id": "STUDY-GATE",
                "version_tag": "2.0",
                "version_index": 2,
                "status": "PUBLISHED",
            },
            "icf_signed": False,
            "requires_reconsent": True,
        }
        res_reconsent_v2 = await client.post(
            "/api/v1/execution/subjects/SUBJ-GATE-Y/consent",
            json=reconsent_v2_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_reconsent_v2.status_code == 200

        # Attempt to capture a visit (should fail since subject lacks re-consent for v2.0)
        visit_payload = {
            "subject_id": "SUBJ-GATE-Y",
            "visit_name": "Week 2",
            "study_id": "STUDY-GATE",
        }
        with pytest.raises(PermissionError) as exc_info:
            await client.post(
                "/api/v1/execution/visits",
                json=visit_payload,
                headers=get_exec_auth_headers(),
            )
        assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
            exc_info.value
        )

        # Clear the gate by signing the consent for version 2.0
        matching_consent_payload = {
            "protocol_version": {
                "study_id": "STUDY-GATE",
                "version_tag": "2.0",
                "version_index": 2,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        res_matching = await client.post(
            "/api/v1/execution/subjects/SUBJ-GATE-Y/consent",
            json=matching_consent_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_matching.status_code == 200

        # Subsequent writes must now succeed cleanly
        res_visit_unblocked = await client.post(
            "/api/v1/execution/visits",
            json=visit_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_visit_unblocked.status_code == 200
        assert res_visit_unblocked.json()["visit_name"] == "Week 2"


# =====================================================================
# 3. CLINICAL CAPTURE PROVENANCE & RECONCILIATION (PRD-SYS-001)
# =====================================================================


@pytest.mark.asyncio
async def test_clinical_capture_provenance_and_version_stamping():
    """
    Validate that captured visit structures and clinical observations are automatically
    stamped with the subject's active protocol version tag and index, establishing
    clear provenance and electronic tracking.

    Requirements: PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Create clinical subject
        subject_payload = {
            "subject_id": "SUBJ-STAMP",
            "study_id": "STUDY-STAMP",
            "demographics": {
                "name": "Bob Green",
                "birthdate": "1991-01-01",
                "gender": "M",
                "race": "White",
            },
        }
        await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_exec_auth_headers(),
        )

        # Sign-off protocol version 3.4
        consent_v34_payload = {
            "protocol_version": {
                "study_id": "STUDY-STAMP",
                "version_tag": "3.4",
                "version_index": 3,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        await client.post(
            "/api/v1/execution/subjects/SUBJ-STAMP/consent",
            json=consent_v34_payload,
            headers=get_exec_auth_headers(),
        )

        # Post a visit and verify active stamp is applied
        visit_payload = {
            "subject_id": "SUBJ-STAMP",
            "visit_name": "Screening",
            "study_id": "STUDY-STAMP",
        }
        res_visit = await client.post(
            "/api/v1/execution/visits",
            json=visit_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_visit.status_code == 200
        data_visit = res_visit.json()
        assert data_visit["protocol_version_tag"] == "3.4"
        assert data_visit["protocol_version_index"] == 3

        # Post a clinical observation and verify active stamp is applied
        obs_payload = {
            "subject_id": "SUBJ-STAMP",
            "domain": "VS",
            "test_code": "VSSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 121.0,
            "unit": "mmHg",
        }
        res_obs = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_obs.status_code == 200
        data_obs = res_obs.json()
        assert data_obs["protocol_version_tag"] == "3.4"
        assert data_obs["protocol_version_index"] == 3


@pytest.mark.asyncio
async def test_non_destructive_reconciliation_and_multi_hop():
    """
    Validate that running the clinical data up-versioning reconciliation
    applies multi-hop rules recursively and on-the-fly, producing deterministic target records
    with exact rename, add, and remove provenance metadata while keeping historical
    original database observations completely pristine and unaltered.

    Requirements: PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # Create a subject
        subject_payload = {
            "subject_id": "SUBJ-MIG",
            "study_id": "STUDY-MIG",
            "demographics": {
                "name": "Jane Doe",
                "birthdate": "1994-04-04",
                "gender": "F",
                "race": "Asian",
            },
        }
        await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_exec_auth_headers(),
        )

        # Consent version 1.0
        consent_v1_payload = {
            "protocol_version": {
                "study_id": "STUDY-MIG",
                "version_tag": "1.0",
                "version_index": 1,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        await client.post(
            "/api/v1/execution/subjects/SUBJ-MIG/consent",
            json=consent_v1_payload,
            headers=get_exec_auth_headers(),
        )

        # Capture visit and clinical observation under 1.0
        visit_payload = {
            "subject_id": "SUBJ-MIG",
            "visit_name": "Screening",
            "study_id": "STUDY-MIG",
        }
        await client.post(
            "/api/v1/execution/visits",
            json=visit_payload,
            headers=get_exec_auth_headers(),
        )

        obs_payload = {
            "subject_id": "SUBJ-MIG",
            "domain": "VS",
            "test_code": "VSSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 115.0,
            "unit": "mmHg",
        }
        await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_exec_auth_headers(),
        )

        # Setup Migration Rules:
        # 1. Rename "VSSBP" -> "SYSBP" (1.0 -> 2.0)
        rule_1 = {
            "study_id": "STUDY-MIG",
            "source_version": "1.0",
            "target_version": "2.0",
            "rule_type": "rename",
            "source_field": "VSSBP",
            "target_field": "SYSBP",
        }
        res_r1 = await client.post(
            "/api/v1/execution/migration-rules",
            json=rule_1,
            headers=get_exec_auth_headers(roles="data manager"),
        )
        assert res_r1.status_code == 201

        # 2. Add "DIABP" with default value 75.0 (2.0 -> 3.0)
        rule_2 = {
            "study_id": "STUDY-MIG",
            "source_version": "2.0",
            "target_version": "3.0",
            "rule_type": "add",
            "target_field": "DIABP",
            "default_value_string": "75.0",
            "default_value_float": 75.0,
        }
        res_r2 = await client.post(
            "/api/v1/execution/migration-rules",
            json=rule_2,
            headers=get_exec_auth_headers(roles="data manager"),
        )
        assert res_r2.status_code == 201

        # Consent newer versions 2.0 and 3.0
        for ver in ["2.0", "3.0"]:
            await client.post(
                "/api/v1/execution/subjects/SUBJ-MIG/consent",
                json={
                    "protocol_version": {
                        "study_id": "STUDY-MIG",
                        "version_tag": ver,
                        "version_index": 2 if ver == "2.0" else 3,
                        "status": "PUBLISHED",
                    },
                    "icf_signed": True,
                    "requires_reconsent": False,
                },
                headers=get_exec_auth_headers(),
            )

        # Execute reconciliation logic and verify outputs
        async with exec_db_manager.get_session_maker()() as session:
            stmt_obs = select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-MIG"
            )
            raw_obs = list((await session.execute(stmt_obs)).scalars().all())
            assert len(raw_obs) == 1
            assert raw_obs[0].test_code == "VSSBP"

            # Reconcile on-the-fly to target version 3.0
            reconciled = await reconcile_observations(session, raw_obs, "3.0")

            assert len(reconciled) == 2
            sysbp = next(o for o in reconciled if o.test_code == "SYSBP")
            diabp = next(o for o in reconciled if o.test_code == "DIABP")

            # Check values and recursive stamping
            assert sysbp.value == 115.0
            assert sysbp.protocol_version_tag == "3.0"
            assert diabp.value == 75.0
            assert diabp.protocol_version_tag == "3.0"

            # Verify original source DB observation is untouched (non-destructive)
            session.expire_all()
            stmt_pristine = select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-MIG"
            )
            pristine_obs = list((await session.execute(stmt_pristine)).scalars().all())
            assert len(pristine_obs) == 1
            assert pristine_obs[0].test_code == "VSSBP"
            assert pristine_obs[0].protocol_version_tag == "1.0"
            assert pristine_obs[0].value == 115.0


# =====================================================================
# 4. eTMF VERSION HISTORY & RATIONALE CONSTRAINTS (PRD-SYS-001)
# =====================================================================


@pytest.mark.asyncio
async def test_etmf_linkage_and_version_history_lineage():
    """
    Validate that ingested clinical documents link properly to protocol versions
    and that version history lineage tracks chronological sequence transitions,
    supporting standard metadata queries and validation checks.

    Requirements: PRD-SYS-001
    """
    client = TestClient(etmf_app)
    headers = get_exec_auth_headers(
        roles="admin,sponsor_dm", change_reason="Initial protocol ingestion"
    )

    # Ingest document version 1
    payload_v1 = {
        "study_id": "study_etmf_linkage",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1_initial.pdf",
        "content": "Original clinical trial protocol content.",
        "mime_type": "application/pdf",
    }
    res_v1 = client.post("/api/v1/etmf/ingest", json=payload_v1, headers=headers)
    assert res_v1.status_code == 201
    v1_data = res_v1.json()
    doc_id_v1 = v1_data["document_id"] if "document_id" in v1_data else v1_data["id"]

    # Ingest document version 2 (amended version)
    payload_v2 = {
        "study_id": "study_etmf_linkage",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1_amended.pdf",
        "content": "Amended clinical trial protocol content.",
        "mime_type": "application/pdf",
    }
    headers_v2 = get_exec_auth_headers(
        roles="admin,sponsor_dm", change_reason="Amending protocol to version 2"
    )
    res_v2 = client.post("/api/v1/etmf/ingest", json=payload_v2, headers=headers_v2)
    assert res_v2.status_code == 201
    v2_data = res_v2.json()
    doc_id_v2 = v2_data["document_id"] if "document_id" in v2_data else v2_data["id"]

    # Query version history lineage
    res_history = client.get(
        f"/api/v1/etmf/documents/{doc_id_v2}/versions", headers=headers
    )
    assert res_history.status_code == 200
    history_data = res_history.json()

    assert history_data["study_id"] == "study_etmf_linkage"
    assert len(history_data["versions"]) == 2

    # Verify ascending version ordering and matching IDs
    assert history_data["versions"][0]["id"] == doc_id_v1
    assert history_data["versions"][0]["version_index"] == 1
    assert history_data["versions"][1]["id"] == doc_id_v2
    assert history_data["versions"][1]["version_index"] == 2


@pytest.mark.asyncio
async def test_etmf_document_change_rationale_mandatory_rules():
    """
    Validate that an explicit, non-empty, and non-whitespace GxP justification
    under `X-Change-Reason` / `reason_for_change` is strictly mandatory for all
    eTMF document ingestion and status transition updates.

    Requirements: PRD-SYS-001
    """
    client = TestClient(etmf_app)

    # 1. Ingestion without X-Change-Reason -> Should fail with 422/400/403 (Validation failure)
    payload = {
        "study_id": "study_rationale_test",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol body",
        "mime_type": "application/pdf",
    }
    res_fail_ingest_missing = client.post(
        "/api/v1/etmf/ingest",
        json=payload,
        headers=get_exec_auth_headers(change_reason=""),  # Cleared change reason
    )
    assert res_fail_ingest_missing.status_code in (400, 403, 422)

    # 2. Ingestion with only whitespace justification -> Should fail
    res_fail_ingest_whitespace = client.post(
        "/api/v1/etmf/ingest",
        json=payload,
        headers=get_exec_auth_headers(change_reason="   \t   "),
    )
    assert res_fail_ingest_whitespace.status_code in (400, 403, 422)

    # 3. Successful ingestion with proper justification
    headers_valid = get_exec_auth_headers(
        roles="admin,sponsor_dm", change_reason="Ingesting primary protocol doc"
    )
    res_success = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=headers_valid
    )
    assert res_success.status_code == 201
    doc_id = res_success.json()["document_id"]

    # 4. Transition without valid justification -> Should fail
    res_fail_trans = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "TECHNICAL_QC",
            "reason_for_change": "   ",  # Whitespace only
        },
        headers=headers_valid,
    )
    assert res_fail_trans.status_code in (400, 403, 422)


@pytest.mark.asyncio
async def test_etmf_qc_transitions_immutability():
    """
    Validate that the Technical and Clinical Quality Control (QC) status transition history
    records (`DocumentQCTransition`) are strictly append-only, and any attempts to update
    or delete them are blocked at the database layer (via SQLAlchemy event listeners).

    Requirements: PRD-SYS-001
    """
    client = TestClient(etmf_app)
    headers = get_exec_auth_headers(
        roles="admin,sponsor_dm", change_reason="Ingesting protocol"
    )

    # Ingest a document
    payload = {
        "study_id": "study_immutability_test",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol body",
        "mime_type": "application/pdf",
    }
    res_ingest = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert res_ingest.status_code == 201
    doc_id = res_ingest.json()["document_id"]

    # Execute a valid status transition
    res_trans = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "TECHNICAL_QC",
            "reason_for_change": "Perform first-stage technical verification",
        },
        headers=headers,
    )
    assert res_trans.status_code == 200

    # Retrieve transition log from DB and verify immutability triggers block writes/deletes
    async with etmf_db_manager.get_session_maker()() as session:
        stmt = select(DocumentQCTransition).where(
            DocumentQCTransition.document_id == doc_id
        )
        transitions = list((await session.execute(stmt)).scalars().all())
        assert len(transitions) == 1
        transition_record = transitions[0]

        # Try to update status -> Should raise OperationalError or IntegrityError or custom exception
        transition_record.to_status = "APPROVED"
        with pytest.raises(Exception) as exc_info:
            await session.commit()
        assert "IMMUTABILITY_VIOLATION" in str(exc_info.value)

        await session.rollback()

        # Try to delete transition -> Should raise Exception
        await session.delete(transition_record)
        with pytest.raises(Exception) as exc_info_del:
            await session.commit()
        assert "IMMUTABILITY_VIOLATION" in str(exc_info_del.value)
