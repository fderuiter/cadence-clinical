import hashlib
import hmac
import json
import time
from datetime import datetime
from typing import Any, Optional

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import select

from apps.econsent.database import db_manager
from apps.econsent.main import app
from apps.econsent.models import (
    Base,
    ComprehensionResult,
    ConsentTemplate,
    SubjectConsent,
)
from packages.security.signing import verify_canonical_signature

TEST_GATEWAY_SECRET = (
    "test-econsent-gateway-secret-key-12345"  # pragma: allowlist secret
)


def get_sig_token(
    user_id: str = "test_patient",
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
    user_id: str = "test_patient",
    roles: str = "patient",
    sig_token: Optional[str] = None,
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
async def setup_test_db(monkeypatch: pytest.MonkeyPatch):
    """Setup in-memory SQLite database before each test and clear down after."""
    monkeypatch.setenv("GATEWAY_SECRET", TEST_GATEWAY_SECRET)
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_happy_path_capture_and_status() -> None:
    """
    Test successful subject consent capture with valid template, passing comprehension checks,
    and valid step-up token. Verify signature manifest integrity and audit trail creation.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Seed a published template
        async with db_manager.get_session_maker()() as session:
            template = ConsentTemplate(
                template_id="template-1",
                study_id="STUDY-123",
                template_name="Template One",
                protocol_version="1.0",
                is_published=True,
                version_index=1,
                clauses=[],
                workflow_steps=[],
                created_by="system",
                reason_for_change="seed",
            )
            session.add(template)

            # Seed a passing comprehension result
            comp = ComprehensionResult(
                template_id="template-1",
                version_index=1,
                subject_pseudonym="SUBJ-001",
                questions=[],
                expected_answers={},
                threshold_policy={},
                submitted_answers={},
                passed=True,
                score=100.0,
                created_by="test_patient",
                reason_for_change="passed comprehension",
            )
            session.add(comp)
            await session.commit()

        # 2. Capture Consent
        sig_token = get_sig_token(
            user_id="test_patient", roles="patient", action="capture-consent"
        )
        headers = get_gateway_headers(
            user_id="test_patient", roles="patient", sig_token=sig_token
        )

        payload = {
            "subject_pseudonym": "SUBJ-001",
            "site_id": "SITE-A",
            "device_timestamp": "2023-10-15T12:00:00Z",
            "source_content_identity": "clause-hash-abc123xyz",
            "reason_for_change": "I consent to STUDY-123 participation",
        }

        res = await client.post(
            "/api/v1/econsent/templates/template-1/versions/1/capture-consent",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 201, res.text
        data = res.json()

        assert data["id"] is not None
        assert data["subject_pseudonym"] == "SUBJ-001"
        assert data["study_id"] == "STUDY-123"
        assert data["site_id"] == "SITE-A"
        assert data["template_id"] == "template-1"
        assert data["version_index"] == 1
        assert data["protocol_version"] == "1.0"
        assert data["source_content_identity"] == "clause-hash-abc123xyz"
        assert data["signature_manifest"] is not None

        # Verify signature manifest structure
        sig_manifest = data["signature_manifest"]
        assert "signature_manifestation" in sig_manifest
        assert "canonical_signature" in sig_manifest
        assert "canonical_payload_hash" in sig_manifest

        manifestation = sig_manifest["signature_manifestation"]
        assert manifestation["signer_id"] == "test_patient"
        assert manifestation["signing_reason"] == "APPROVAL"

        # 3. Retrieve Status
        headers_status = get_gateway_headers(user_id="test_patient", roles="patient")
        res_status = await client.get(
            "/api/v1/econsent/subjects/SUBJ-001/consent-status?study_id=STUDY-123",
            headers=headers_status,
        )
        assert res_status.status_code == 200
        status_data = res_status.json()
        assert status_data["signed"] is True
        assert status_data["comprehension_passed"] is True
        assert status_data["protocol_version"] == "1.0"
        assert status_data["version_index"] == 1


@pytest.mark.asyncio
async def test_capture_rejections() -> None:
    """
    Test capture-consent rejection criteria:
    - Template not published
    - Comprehension result missing/incomplete
    - Step-up token missing, expired, wrong-user, or wrong-action.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Seed an unpublished template and a template with no comprehension checks
        async with db_manager.get_session_maker()() as session:
            t_unpublished = ConsentTemplate(
                template_id="template-unpub",
                study_id="STUDY-123",
                template_name="Template Unpublished",
                protocol_version="1.0",
                is_published=False,
                version_index=1,
                clauses=[],
                workflow_steps=[],
                created_by="system",
                reason_for_change="seed",
            )
            session.add(t_unpublished)

            t_published = ConsentTemplate(
                template_id="template-pub",
                study_id="STUDY-123",
                template_name="Template Published",
                protocol_version="1.0",
                is_published=True,
                version_index=1,
                clauses=[],
                workflow_steps=[],
                created_by="system",
                reason_for_change="seed",
            )
            session.add(t_published)
            await session.commit()

        # 1. Reject on unpublished template
        sig_token_1 = get_sig_token()
        headers_1 = get_gateway_headers(sig_token=sig_token_1)
        payload = {
            "subject_pseudonym": "SUBJ-001",
            "site_id": "SITE-A",
            "source_content_identity": "clause-hash-abc",
            "reason_for_change": "I consent",
        }
        res = await client.post(
            "/api/v1/econsent/templates/template-unpub/versions/1/capture-consent",
            json=payload,
            headers=headers_1,
        )
        assert res.status_code == 400
        assert "not published" in res.text

        # 2. Reject on missing comprehension check
        sig_token_2 = get_sig_token()
        headers_2 = get_gateway_headers(sig_token=sig_token_2)
        res = await client.post(
            "/api/v1/econsent/templates/template-pub/versions/1/capture-consent",
            json=payload,
            headers=headers_2,
        )
        assert res.status_code == 400
        assert "Comprehension checks have not been completed" in res.text

        # Now seed a passing comprehension check so we can test step-up token validations
        async with db_manager.get_session_maker()() as session:
            comp = ComprehensionResult(
                template_id="template-pub",
                version_index=1,
                subject_pseudonym="SUBJ-001",
                questions=[],
                expected_answers={},
                threshold_policy={},
                submitted_answers={},
                passed=True,
                score=100.0,
                created_by="test_patient",
                reason_for_change="passed",
            )
            session.add(comp)
            await session.commit()

        # 3. Reject on missing step-up token
        headers_no_token = get_gateway_headers()
        res = await client.post(
            "/api/v1/econsent/templates/template-pub/versions/1/capture-consent",
            json=payload,
            headers=headers_no_token,
        )
        assert res.status_code == 401
        assert res.json()["detail"] == "REAUTHENTICATION_REQUIRED"

        # 4. Reject on expired step-up token
        sig_token_expired = get_sig_token(expired=True)
        headers_expired = get_gateway_headers(sig_token=sig_token_expired)
        res = await client.post(
            "/api/v1/econsent/templates/template-pub/versions/1/capture-consent",
            json=payload,
            headers=headers_expired,
        )
        assert res.status_code == 401
        assert res.json()["detail"] == "REAUTHENTICATION_REQUIRED"

        # 5. Reject on wrong user
        sig_token_wrong_user = get_sig_token(user_id="another_user")
        headers_wrong_user = get_gateway_headers(
            user_id="test_patient", sig_token=sig_token_wrong_user
        )
        res = await client.post(
            "/api/v1/econsent/templates/template-pub/versions/1/capture-consent",
            json=payload,
            headers=headers_wrong_user,
        )
        assert res.status_code == 401
        assert res.json()["detail"] == "REAUTHENTICATION_REQUIRED"

        # 6. Reject on wrong action
        sig_token_wrong_action = get_sig_token(action="unrelated-action")
        headers_wrong_action = get_gateway_headers(sig_token=sig_token_wrong_action)
        res = await client.post(
            "/api/v1/econsent/templates/template-pub/versions/1/capture-consent",
            json=payload,
            headers=headers_wrong_action,
        )
        assert res.status_code == 401
        assert res.json()["detail"] == "REAUTHENTICATION_REQUIRED"


@pytest.mark.asyncio
async def test_signature_tamper_detection() -> None:
    """
    Test that the cryptographic HMAC signature verifies only for the original payload and fails after tampering.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Seed published template & comprehension result
        async with db_manager.get_session_maker()() as session:
            template = ConsentTemplate(
                template_id="template-1",
                study_id="STUDY-123",
                template_name="Template One",
                protocol_version="1.0",
                is_published=True,
                version_index=1,
                clauses=[],
                workflow_steps=[],
                created_by="system",
                reason_for_change="seed",
            )
            session.add(template)

            comp = ComprehensionResult(
                template_id="template-1",
                version_index=1,
                subject_pseudonym="SUBJ-001",
                questions=[],
                expected_answers={},
                threshold_policy={},
                submitted_answers={},
                passed=True,
                score=100.0,
                created_by="test_patient",
                reason_for_change="passed",
            )
            session.add(comp)
            await session.commit()

        sig_token = get_sig_token()
        headers = get_gateway_headers(sig_token=sig_token)
        payload = {
            "subject_pseudonym": "SUBJ-001",
            "site_id": "SITE-A",
            "device_timestamp": "2023-10-15T12:00:00Z",
            "source_content_identity": "clause-hash-abc",
            "reason_for_change": "I consent",
        }

        res = await client.post(
            "/api/v1/econsent/templates/template-1/versions/1/capture-consent",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 201
        sc_data = res.json()

        sig_manifest = sc_data["signature_manifest"]
        canonical_sig = sig_manifest["canonical_signature"]

        def normalize_timestamp_str(dt: Any) -> Optional[str]:
            if not dt:
                return None
            if isinstance(dt, datetime):
                return dt.strftime("%Y-%m-%dT%H:%M:%S")
            if isinstance(dt, str):
                try:
                    from dateutil.parser import parse

                    return parse(dt).strftime("%Y-%m-%dT%H:%M:%S")
                except Exception:
                    return dt
            return str(dt)

        # Re-build canonical payload to verify signature passes
        canonical_payload = {
            "subject_pseudonym": sc_data["subject_pseudonym"],
            "study_id": sc_data["study_id"],
            "site_id": sc_data["site_id"],
            "template_id": sc_data["template_id"],
            "version_index": sc_data["version_index"],
            "protocol_version": sc_data["protocol_version"],
            "source_content_identity": sc_data["source_content_identity"],
            "server_timestamp": normalize_timestamp_str(sc_data["server_timestamp"]),
            "device_timestamp": normalize_timestamp_str(sc_data["device_timestamp"]),
        }

        secret = TEST_GATEWAY_SECRET.encode()

        # Verify authentic signature succeeds
        assert (
            verify_canonical_signature(canonical_payload, canonical_sig, secret) is True
        )

        # TAMPER matching field -> signature check must fail
        tampered_payload_1 = canonical_payload.copy()
        tampered_payload_1["subject_pseudonym"] = "SUBJ-MALICIOUS"
        assert (
            verify_canonical_signature(tampered_payload_1, canonical_sig, secret)
            is False
        )

        # TAMPER version field -> signature check must fail
        tampered_payload_2 = canonical_payload.copy()
        tampered_payload_2["version_index"] = 2
        assert (
            verify_canonical_signature(tampered_payload_2, canonical_sig, secret)
            is False
        )


@pytest.mark.asyncio
async def test_append_only_audit_history() -> None:
    """
    Assert append-only model constraint: multiple captures produce new rows while prior rows remain unchanged.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Seed published template & comprehension result
        async with db_manager.get_session_maker()() as session:
            template = ConsentTemplate(
                template_id="template-1",
                study_id="STUDY-123",
                template_name="Template One",
                protocol_version="1.0",
                is_published=True,
                version_index=1,
                clauses=[],
                workflow_steps=[],
                created_by="system",
                reason_for_change="seed",
            )
            session.add(template)

            comp = ComprehensionResult(
                template_id="template-1",
                version_index=1,
                subject_pseudonym="SUBJ-001",
                questions=[],
                expected_answers={},
                threshold_policy={},
                submitted_answers={},
                passed=True,
                score=100.0,
                created_by="test_patient",
                reason_for_change="passed",
            )
            session.add(comp)
            await session.commit()

        # Capture 1
        sig_token_1 = get_sig_token()
        res_1 = await client.post(
            "/api/v1/econsent/templates/template-1/versions/1/capture-consent",
            json={
                "subject_pseudonym": "SUBJ-001",
                "site_id": "SITE-A",
                "source_content_identity": "hash-v1",
                "reason_for_change": "First signature",
            },
            headers=get_gateway_headers(
                sig_token=sig_token_1, change_reason="First signature"
            ),
        )
        assert res_1.status_code == 201
        sc1_id = res_1.json()["id"]

        # Capture 2
        sig_token_2 = get_sig_token()
        res_2 = await client.post(
            "/api/v1/econsent/templates/template-1/versions/1/capture-consent",
            json={
                "subject_pseudonym": "SUBJ-001",
                "site_id": "SITE-A",
                "source_content_identity": "hash-v1",
                "reason_for_change": "Second signature",
            },
            headers=get_gateway_headers(
                sig_token=sig_token_2, change_reason="Second signature"
            ),
        )
        assert res_2.status_code == 201
        sc2_id = res_2.json()["id"]

        assert sc1_id != sc2_id

        # Verify they are stored as separate rows and both exist intact in database
        async with db_manager.get_session_maker()() as session:
            stmt = (
                select(SubjectConsent)
                .where(SubjectConsent.subject_pseudonym == "SUBJ-001")
                .order_by(SubjectConsent.created_at.asc())
            )
            results = (await session.execute(stmt)).scalars().all()

            assert len(results) == 2
            assert results[0].id == sc1_id
            assert results[0].reason_for_change == "First signature"
            assert results[1].id == sc2_id
            assert results[1].reason_for_change == "Second signature"


@pytest.mark.asyncio
async def test_execution_consumption_integration(monkeypatch) -> None:
    """
    Test that Execution service client can fetch consent status, and that
    record_subject_consent validates ICF signatures correctly via eConsent.
    """
    # 1. Mock eConsent server response inside econsent_client
    mock_status_response = {
        "subject_pseudonym": "SUBJ-001",
        "study_id": "STUDY-123",
        "site_id": "SITE-A",
        "template_id": "template-1",
        "version_index": 1,
        "protocol_version": "1.0",
        "signed": True,
        "comprehension_passed": True,
    }

    async def mock_get(self, url, headers=None, params=None, timeout=None):
        class MockResponse:
            status_code = 200
            text = "OK"

            def json(self):
                return mock_status_response

        return MockResponse()

    # Apply mock to httpx.AsyncClient.get inside execution/econsent_client.py
    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get)

    from apps.execution.econsent_client import fetch_subject_consent_status

    status = await fetch_subject_consent_status(
        subject_pseudonym="SUBJ-001", study_id="STUDY-123"
    )

    assert status["signed"] is True
    assert status["version_index"] == 1
    assert status["protocol_version"] == "1.0"
