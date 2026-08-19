import hashlib
import hmac
import json
import time
from datetime import UTC, datetime

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from jose import jwt
from sqlalchemy import select

from apps.econsent.database import db_manager
from apps.econsent.main import app
from apps.econsent.models import (
    Base,
    ComprehensionResult,
    ConsentAuditLog,
    SubjectConsent,
)

TEST_GATEWAY_SECRET = (
    "test-econsent-gateway-secret-key-12345"  # pragma: allowlist secret
)


def get_sig_token(
    user_id: str = "subj-101",
    roles: str = "patient",
    action: str = "capture-consent",
    expired: bool = False,
    secret: str = TEST_GATEWAY_SECRET,
) -> str:
    """Generate a 21 CFR Part 11 compliant re-authentication token."""
    payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() - 100.0 if expired else time.time() + 300.0,
    }
    return jwt.encode(payload, secret, algorithm="HS256")


def get_gateway_headers(
    user_id: str = "subj-101",
    roles: str = "patient",
    sig_token: str | None = None,
    change_reason: str = "test consent capture",
    secret: str = TEST_GATEWAY_SECRET,
) -> dict:
    """Generate gateway v2 signed headers for eConsent testing."""
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    sig = hmac.new(secret.encode(), serialized.encode(), hashlib.sha256).hexdigest()
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if sig_token:
        headers["X-Sig-Token"] = sig_token
    return headers


@pytest_asyncio.fixture(autouse=True)
async def setup_econsent_test_db(monkeypatch: pytest.MonkeyPatch):
    """Setup isolated in-memory eConsent database for unit and integration testing."""
    monkeypatch.setenv("GATEWAY_SECRET", TEST_GATEWAY_SECRET)
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def test_icf_builder_modular_clauses_linked_to_protocol_version():
    """Validate ICF Builder drafts modular consent clauses linked to protocol versions.

    @req:PRD-SYS-042
    """
    client = TestClient(app)

    # 1. Draft modular clauses for CADENCE-101
    clause_payload = {
        "clause_id": "clause-purpose-cadence101",
        "study_id": "CADENCE-101",
        "title": "Study Purpose & Scope",
        "text": "Evaluating safety and efficacy of Cadence-001 under protocol v1.0.",
        "reason_for_change": "Initial clause draft for protocol v1.0",
        "created_by": "sponsor.designer",
    }
    headers_designer = get_gateway_headers(
        user_id="sponsor.designer",
        roles="sponsor_designer",
        change_reason="Drafting protocol v1.0 consent clause",
    )
    res_clause = client.post(
        "/api/v1/econsent/clauses", json=clause_payload, headers=headers_designer
    )
    assert res_clause.status_code == 201
    clause_data = res_clause.json()
    assert clause_data["clause_id"] == "clause-purpose-cadence101"
    assert clause_data["version_index"] == 1

    # 2. Assemble ICF Template linked to protocol version v1.0
    template_payload = {
        "template_id": "icf-cadence101",
        "study_id": "CADENCE-101",
        "template_name": "CADENCE-101 Informed Consent Form",
        "protocol_version": "v1.0",
        "requires_reconsent": True,
        "clauses": ["clause-purpose-cadence101"],
        "workflow_steps": [
            {
                "type": "comprehension_check",
            },
            {
                "type": "signature_placeholder",
                "role": "Subject",
            },
            {
                "type": "signature_placeholder",
                "role": "PI",
            },
        ],
        "reason_for_change": "Initial ICF template assembly for protocol v1.0",
        "created_by": "sponsor.designer",
    }
    res_template = client.post(
        "/api/v1/econsent/templates", json=template_payload, headers=headers_designer
    )
    assert res_template.status_code == 201
    template_data = res_template.json()
    assert template_data["template_id"] == "icf-cadence101"
    assert template_data["protocol_version"] == "v1.0"
    assert template_data["study_id"] == "CADENCE-101"
    assert template_data["version_index"] == 1


def test_comprehension_quiz_evaluation_and_threshold_enforcement():
    """Validate comprehension quiz evaluation with instant feedback and threshold checks.

    @req:PRD-SYS-043
    """
    client = TestClient(app)

    # 1. Setup template with comprehension check
    headers = get_gateway_headers(
        user_id="sponsor.designer",
        roles="sponsor_designer",
        change_reason="Creating comprehension assessment",
    )

    clause_payload = {
        "clause_id": "clause-comp-1",
        "study_id": "CADENCE-101",
        "title": "Study Overview",
        "text": "Full study overview for CADENCE-101.",
        "reason_for_change": "Clause creation",
        "created_by": "sponsor.designer",
    }
    client.post("/api/v1/econsent/clauses", json=clause_payload, headers=headers)

    template_payload = {
        "template_id": "tpl-comp-eval",
        "study_id": "CADENCE-101",
        "template_name": "Comprehension Test ICF",
        "protocol_version": "v1.0",
        "requires_reconsent": True,
        "clauses": ["clause-comp-1"],
        "workflow_steps": [
            {
                "type": "comprehension_check",
            },
            {
                "type": "signature_placeholder",
                "role": "Subject",
            },
        ],
        "reason_for_change": "Publishing comprehension template",
        "created_by": "sponsor.designer",
    }
    res_tpl = client.post(
        "/api/v1/econsent/templates", json=template_payload, headers=headers
    )
    assert res_tpl.status_code == 201

    # Define Comprehension Check questions and expected answers
    check_payload = {
        "questions": [
            {
                "id": "q1",
                "text": "Is participation voluntary?",
                "options": ["No", "Yes"],
            },
            {
                "id": "q2",
                "text": "Can you withdraw at any time?",
                "options": ["No", "Yes"],
            },
        ],
        "expected_answers": {"q1": "Yes", "q2": "Yes"},
        "threshold_policy": {"passing_percentage": 80.0},
        "reason_for_change": "Defining questions for comprehension check",
        "created_by": "sponsor.designer",
    }
    res_chk = client.post(
        "/api/v1/econsent/templates/tpl-comp-eval/versions/1/comprehension-checks",
        json=check_payload,
        headers=headers,
    )
    assert res_chk.status_code == 201

    # Publish template
    res_pub = client.post(
        "/api/v1/econsent/templates/tpl-comp-eval/publish", headers=headers
    )
    assert res_pub.status_code == 200

    # 2. Evaluate passing answers (100% >= 80% threshold)
    eval_headers = get_gateway_headers(
        user_id="subj-pass",
        roles="patient",
        change_reason="Submitting comprehension answers",
    )
    res_eval_pass = client.post(
        "/api/v1/econsent/templates/tpl-comp-eval/versions/1/submit-answers",
        json={
            "subject_pseudonym": "SUBJ-PASS",
            "submitted_answers": {"q1": "Yes", "q2": "Yes"},
            "reason_for_change": "Submitting correct answers",
        },
        headers=eval_headers,
    )
    assert res_eval_pass.status_code == 200
    eval_pass_data = res_eval_pass.json()
    assert eval_pass_data["score"] == 100.0
    assert eval_pass_data["passed"] is True
    assert eval_pass_data["next_step"] == "sign_consent"

    # 3. Evaluate failing answers (50% < 80% threshold)
    res_eval_fail = client.post(
        "/api/v1/econsent/templates/tpl-comp-eval/versions/1/submit-answers",
        json={
            "subject_pseudonym": "SUBJ-FAIL",
            "submitted_answers": {"q1": "No", "q2": "Yes"},
            "reason_for_change": "Submitting incorrect answers",
        },
        headers=eval_headers,
    )
    assert res_eval_fail.status_code == 200
    eval_fail_data = res_eval_fail.json()
    assert eval_fail_data["score"] == 50.0
    assert eval_fail_data["passed"] is False
    assert eval_fail_data["next_step"] == "retry_checks"


@pytest.mark.asyncio
async def test_21_cfr_part_11_dual_credential_signature_capture():
    """Validate 21 CFR Part 11 electronic signature capture creates cryptographic consent records.

    @req:PRD-SYS-042
    """
    client = TestClient(app)

    # 1. Setup published template
    headers = get_gateway_headers(
        user_id="sponsor.designer",
        roles="sponsor_designer",
        change_reason="Setup template for signature",
    )
    client.post(
        "/api/v1/econsent/clauses",
        json={
            "clause_id": "cl-sig-1",
            "study_id": "CADENCE-101",
            "title": "Consent Terms",
            "text": "Consent terms for signature capture.",
            "reason_for_change": "Initial clause",
            "created_by": "sponsor.designer",
        },
        headers=headers,
    )
    client.post(
        "/api/v1/econsent/templates",
        json={
            "template_id": "tpl-sig-flow",
            "study_id": "CADENCE-101",
            "template_name": "Dual Credential ICF",
            "protocol_version": "v1.0",
            "requires_reconsent": True,
            "clauses": ["cl-sig-1"],
            "workflow_steps": [
                {"type": "comprehension_check"},
                {"type": "signature_placeholder", "role": "Subject"},
                {"type": "signature_placeholder", "role": "PI"},
            ],
            "reason_for_change": "Signature capture template",
            "created_by": "sponsor.designer",
        },
        headers=headers,
    )

    # Seed passing comprehension result for subj-101
    async with db_manager.get_session_maker()() as session:
        comp = ComprehensionResult(
            template_id="tpl-sig-flow",
            version_index=1,
            subject_pseudonym="subj-101",
            questions=[],
            expected_answers={},
            threshold_policy={},
            submitted_answers={},
            passed=True,
            score=100.0,
            created_by="subj-101",
            reason_for_change="passed",
        )
        session.add(comp)
        await session.commit()

    # Publish template
    res_pub = client.post(
        "/api/v1/econsent/templates/tpl-sig-flow/publish",
        headers=headers,
    )
    assert res_pub.status_code == 200

    # 2. Capture Subject 21 CFR Part 11 eSignature with Step-up Token
    sig_token = get_sig_token(
        user_id="subj-101", roles="patient", action="capture-consent"
    )
    subject_headers = get_gateway_headers(
        user_id="subj-101",
        roles="patient",
        sig_token=sig_token,
        change_reason="I agree to participate",
    )
    subject_signature_payload = {
        "subject_pseudonym": "subj-101",
        "site_id": "SITE-01",
        "source_content_identity": "clause-hash-cadence101",
        "device_timestamp": datetime.now(UTC).isoformat(),
        "reason_for_change": "I agree to participate in CADENCE-101",
    }

    res_sig = client.post(
        "/api/v1/econsent/templates/tpl-sig-flow/versions/1/capture-consent",
        json=subject_signature_payload,
        headers=subject_headers,
    )
    assert res_sig.status_code == 201
    consent_record = res_sig.json()

    assert consent_record["id"] is not None
    assert consent_record["subject_pseudonym"] == "subj-101"
    assert consent_record["study_id"] == "CADENCE-101"
    assert consent_record["site_id"] == "SITE-01"
    assert consent_record["signature_manifest"] is not None

    sig_manifest = consent_record["signature_manifest"]
    assert "signature_manifestation" in sig_manifest
    assert "canonical_signature" in sig_manifest
    assert "canonical_payload_hash" in sig_manifest

    manifestation = sig_manifest["signature_manifestation"]
    assert manifestation["signer_id"] == "subj-101"
    assert manifestation["signing_reason"] == "APPROVAL"
    assert len(manifestation["sha256_hash"]) == 64

    # 3. Verify Database Persistence and Audit Trail Immutability
    async with db_manager.get_session_maker()() as session:
        # Verify SubjectConsent record
        sc_stmt = select(SubjectConsent).where(
            SubjectConsent.id == consent_record["id"]
        )
        sc_res = await session.execute(sc_stmt)
        sc_db = sc_res.scalar_one()
        assert sc_db.subject_pseudonym == "subj-101"
        assert sc_db.study_id == "CADENCE-101"

        # Verify Part 11 Audit Trail
        audit_stmt = select(ConsentAuditLog).where(
            ConsentAuditLog.action == "CAPTURE_CONSENT"
        )
        audit_res = await session.execute(audit_stmt)
        audit_logs = audit_res.scalars().all()
        assert len(audit_logs) >= 1
        assert audit_logs[0].actor_id == "subj-101"

    # 4. Verify Subject Consent Status Endpoint
    res_status = client.get(
        "/api/v1/econsent/subjects/subj-101/consent-status?study_id=CADENCE-101",
        headers=subject_headers,
    )
    assert res_status.status_code == 200
    status_data = res_status.json()
    assert status_data["signed"] is True
    assert status_data["comprehension_passed"] is True
    assert status_data["protocol_version"] == "v1.0"
    assert status_data["version_index"] == 1
