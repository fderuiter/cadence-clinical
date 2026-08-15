from contextlib import contextmanager

import pytest
from fastapi.testclient import TestClient

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.models import Base
from apps.etmf.application.classification_service import classify_tmf_document
from apps.etmf.main import app
from apps.etmf.tests.test_etmf import get_auth_headers
from apps.execution.trial_lock import TrialLockManager
from packages.security.rbac import Principal, get_principal


@pytest.fixture(autouse=True)
def setup_db():
    """
    Setup in-memory eTMF database for unit and integration testing.
    """
    TrialLockManager.reset()
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    # Create tables synchronously for tests
    import asyncio

    async def create_all():
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    asyncio.run(create_all())
    yield
    TrialLockManager.reset()

    async def drop_all():
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)

    asyncio.run(drop_all())
    # Run close coroutine
    asyncio.run(db_manager.close())


def test_resolve_document_type_helper():
    """
    Verify the resolve_document_type helper works correctly.
    """
    from apps.etmf.application.classification_service import resolve_document_type

    assert resolve_document_type("05.02.01") == "FORM_1572"
    assert resolve_document_type("05.02.02") == "FINANCIAL_DISCLOSURE"
    assert resolve_document_type("01.01.03") == "PROTOCOL_SIGNOFF"
    assert resolve_document_type("01.01.01") is None
    assert resolve_document_type("99.99.99") is None


def test_classification_service_direct():
    """
    Verify classify_tmf_document directly with various inputs.
    """
    # 1. Exact artifact_type code match
    res = classify_tmf_document(filename="", artifact_type="01.01.01")
    assert res is not None
    assert res.artifact_code == "01.01.01"
    assert res.resolved_zone == 1
    assert res.resolved_section == "01.01"
    assert res.artifact_type == "Clinical Trial Protocol"
    assert res.match_basis == "artifact_type_hint"

    # 2. Exact name match
    res = classify_tmf_document(filename="", artifact_type="Blank CRF")
    assert res is not None
    assert res.artifact_code == "10.02.01"
    assert res.match_basis == "artifact_type_hint"

    # 3. Alias match (e.g., FDA Form 1572 / FORM_1572)
    res = classify_tmf_document(filename="", artifact_type="FORM_1572")
    assert res is not None
    assert res.artifact_code == "05.02.01"
    assert res.match_basis == "artifact_type_hint"

    # 4. Filename exact code match
    res = classify_tmf_document(filename="some_document_01.01.03_draft.pdf")
    assert res is not None
    assert res.artifact_code == "01.01.03"
    assert res.match_basis == "filename_match"

    # 5. Filename substring name match
    res = classify_tmf_document(filename="pre_blank_crf_v1.zip")
    assert res is not None
    assert res.artifact_code == "10.02.01"
    assert res.match_basis == "filename_match"

    # 6. Free text substring alias match
    res = classify_tmf_document(
        filename="", free_text="Please upload the FINANCIAL_DISCLOSURE form here."
    )
    assert res is not None
    assert res.artifact_code == "05.02.02"
    assert res.match_basis == "free_text_match"

    # 7. Unresolved inputs
    res = classify_tmf_document(filename="unrelated_doc.txt")
    assert res is None


def test_get_taxonomy_endpoint():
    """
    Verify GET /api/v1/etmf/taxonomy endpoint.
    """
    client = TestClient(app)

    # 1. Request with correct permissions
    headers = get_auth_headers(roles="sysadmin", change_reason="taxonomy check")
    resp = client.get("/api/v1/etmf/taxonomy", headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert "version" in data
    assert len(data["zones"]) == 11

    # Check Zone 1 structure
    zone_1 = data["zones"][0]
    assert zone_1["zone_code"] == 1
    assert zone_1["zone_name"] == "Trial Management"
    assert len(zone_1["sections"]) > 0

    # Check Section 01.01 artifacts
    sec_1_1 = zone_1["sections"][0]
    assert sec_1_1["section_code"] == "01.01"
    assert sec_1_1["section_name"] == "Trial Design"
    assert len(sec_1_1["artifacts"]) > 0
    assert sec_1_1["artifacts"][0]["artifact_code"] == "01.01.01"
    assert sec_1_1["artifacts"][0]["artifact_name"] == "Clinical Trial Protocol"

    # 2. Request with incorrect/insufficient permissions (e.g., subject or guest role)
    # The get_auth_headers helper uses "sysadmin" as default if roles is not supplied.
    # Let's test with a role that lacks "etmf_taxonomy:read" permission.
    # In rbac.py, "subject" only has ecrf_data_entry permissions.
    bad_headers = get_auth_headers(roles="subject", change_reason="unauthorized check")
    resp_bad = client.get("/api/v1/etmf/taxonomy", headers=bad_headers)
    assert resp_bad.status_code == 403


def test_classify_endpoints():
    """
    Verify POST /api/v1/etmf/taxonomy/classify and fallback POST /api/v1/etmf/classify endpoints.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="sysadmin", change_reason="classify check")

    # 1. Test POST /api/v1/etmf/taxonomy/classify - successful resolution
    payload = {
        "filename": "protocol_amendment_2026.pdf",
        "artifact_type": "Clinical Trial Protocol Amendment",
    }
    resp = client.post("/api/v1/etmf/taxonomy/classify", json=payload, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["resolved_zone"] == 1
    assert data["resolved_section"] == "01.01"
    assert data["artifact_code"] == "01.01.02"
    assert data["artifact_type"] == "Clinical Trial Protocol Amendment"
    assert data["match_basis"] == "artifact_type_hint"

    # 2. Test fallback POST /api/v1/etmf/classify - successful resolution via filename
    payload_fallback = {"filename": "my_blank_crf_form.pdf"}
    resp_fallback = client.post(
        "/api/v1/etmf/classify", json=payload_fallback, headers=headers
    )
    assert resp_fallback.status_code == 200
    data_fb = resp_fallback.json()
    assert data_fb["artifact_code"] == "10.02.01"
    assert data_fb["match_basis"] == "filename_match"

    # 3. Test unresolved classification - returns HTTP 422
    payload_unresolved = {"filename": "completely_arbitrary_random_file.zip"}
    resp_unresolved = client.post(
        "/api/v1/etmf/classify", json=payload_unresolved, headers=headers
    )
    assert resp_unresolved.status_code == 422
    assert "unable to auto-classify" in resp_unresolved.json()["detail"].lower()


@contextmanager
def override_principal_ctx(app_inst, principal: Principal):
    """
    Temporarily override get_principal dependency.
    """

    async def mock_get_principal():
        return principal

    app_inst.dependency_overrides[get_principal] = mock_get_principal
    try:
        yield
    finally:
        app_inst.dependency_overrides.pop(get_principal, None)


def test_auto_file_endpoint():
    """Verify the POST /api/v1/etmf/auto-file endpoint and study scope enforcement.

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)
    headers_admin = get_auth_headers(roles="admin", change_reason="auto-file check")

    # 1. Successful auto-file with matching artifact type
    payload = {
        "filename": "protocol_amendment_2026.pdf",
        "artifact_type": "Clinical Trial Protocol Amendment",
        "study_id": "study_001",
    }
    resp = client.post("/api/v1/etmf/auto-file", json=payload, headers=headers_admin)
    assert resp.status_code == 200
    data = resp.json()
    assert data["resolved_zone"] == 1
    assert data["resolved_section"] == "01.01"
    assert data["artifact_code"] == "01.01.02"
    assert data["artifact_type"] == "Clinical Trial Protocol Amendment"
    assert data["match_basis"] == "artifact_type_hint"

    # 2. Permission check: subject lacks etmf_document:read and is forbidden
    headers_subject = get_auth_headers(roles="subject", change_reason="unauthorized")
    resp_sub = client.post(
        "/api/v1/etmf/auto-file", json=payload, headers=headers_subject
    )
    assert resp_sub.status_code == 403

    # 3. Study scope check: study-scoped user is forbidden from other studies
    principal_scoped = Principal(
        user_id="user_scoped",
        roles=["sponsor_dm"],  # Has etmf_document:read
        assigned_studies=["study_001"],
    )

    with override_principal_ctx(app, principal_scoped):
        # 3.1. Requesting study_001 (assigned) should succeed
        resp_scoped_ok = client.post(
            "/api/v1/etmf/auto-file", json=payload, headers=headers_admin
        )
        assert resp_scoped_ok.status_code == 200

        # 3.2. Requesting study_002 (not assigned) should return 403 Forbidden
        payload_other = {
            "filename": "protocol_amendment_2026.pdf",
            "artifact_type": "Clinical Trial Protocol Amendment",
            "study_id": "study_002",
        }
        resp_scoped_deny = client.post(
            "/api/v1/etmf/auto-file", json=payload_other, headers=headers_admin
        )
        assert resp_scoped_deny.status_code == 403
        assert "Forbidden" in resp_scoped_deny.json()["detail"]

    # 4. Unresolved classification returns 422
    payload_unresolved = {
        "filename": "completely_arbitrary_random_file.zip",
        "study_id": "study_001",
    }
    resp_unresolved = client.post(
        "/api/v1/etmf/auto-file", json=payload_unresolved, headers=headers_admin
    )
    assert resp_unresolved.status_code == 422
    assert "unable to auto-classify" in resp_unresolved.json()["detail"].lower()
