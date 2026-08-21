from contextlib import contextmanager

import pytest

from apps.designer.main import app as designer_app
from packages.security.rbac import Principal, get_principal
from tests.rbac_helpers import build_gateway_headers, data_manager, sponsor_designer


@contextmanager
def override_principal(app, principal: Principal):
    """
    Context manager to temporarily override the get_principal dependency in a FastAPI app.
    This injects a Principal with explicit attributes (e.g. assigned_studies, assigned_sites).
    """

    async def mock_get_principal():
        return principal

    app.dependency_overrides[get_principal] = mock_get_principal
    try:
        yield
    finally:
        app.dependency_overrides.pop(get_principal, None)


@pytest.mark.asyncio
async def test_rbac_execution_access(shared_sqlite_dbs, execution_client):
    """
    Verify that an authorized data manager can access the queries endpoint successfully.

    @req:PRD-SYS-050
    """
    headers = data_manager()
    resp = await execution_client.get("/api/v1/execution/queries", headers=headers)
    assert resp.status_code == 200
    assert isinstance(resp.json(), list)


@pytest.mark.asyncio
async def test_rbac_execution_unauthorized(shared_sqlite_dbs, execution_client):
    """
    Verify that an unauthorized persona (e.g., subject) is denied access to mutating query endpoints.
    """
    headers = build_gateway_headers(
        user_id="test_subject", roles="subject", change_reason="Trying to write"
    )
    payload = {
        "study_id": "study_1",
        "subject_id": "sub_1",
        "visit_id": "visit_1",
        "test_code": "TC1",
        "explanation": "Test explanation",
    }
    resp = await execution_client.post(
        "/api/v1/execution/queries", json=payload, headers=headers
    )
    assert resp.status_code == 403


@pytest.mark.asyncio
async def test_rbac_etmf_site_scoping(shared_sqlite_dbs, etmf_client, signed_headers):
    """
    Verify that site-level access restrictions are correctly applied to site-scoped users.
    """
    admin_hdrs = signed_headers(
        user_id="admin_user", roles="admin", change_reason="Setup documents"
    )

    res_alpha = await etmf_client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_001",
            "site_id": "site_alpha",
            "artifact_type": "Investigator CV",
            "filename": "cv_alpha.pdf",
            "content": "CV alpha content",
            "mime_type": "application/pdf",
        },
        headers=admin_hdrs,
    )
    assert res_alpha.status_code == 201
    doc_alpha_id = res_alpha.json()["document_id"]

    res_beta = await etmf_client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_001",
            "site_id": "site_beta",
            "artifact_type": "Investigator CV",
            "filename": "cv_beta.pdf",
            "content": "CV beta content",
            "mime_type": "application/pdf",
        },
        headers=admin_hdrs,
    )
    assert res_beta.status_code == 201
    doc_beta_id = res_beta.json()["document_id"]

    # Site investigator scoped to site_alpha
    alpha_hdrs = signed_headers(
        user_id="user_alpha",
        roles="investigator",
        site_id="site_alpha",
        change_reason="Reading documents",
    )

    # 1. View site_alpha doc -> should succeed (200)
    resp_alpha = await etmf_client.get(
        f"/api/v1/etmf/documents/{doc_alpha_id}", headers=alpha_hdrs
    )
    assert resp_alpha.status_code == 200

    # 2. View site_beta doc -> should fail with 403
    resp_beta = await etmf_client.get(
        f"/api/v1/etmf/documents/{doc_beta_id}", headers=alpha_hdrs
    )
    assert resp_beta.status_code == 403


@pytest.mark.asyncio
async def test_rbac_designer_study_scoping(designer_client, mock_designer_driver):
    """
    Verify that study-level access restrictions are correctly applied in the Designer app
    using dependency overrides to inject custom assigned_studies.
    """
    # Create a Principal scoped ONLY to study_bar
    principal = Principal(
        user_id="test_designer",
        roles=["sponsor_designer"],
        assigned_studies=["study_bar"],
    )

    # Get valid signature headers for authentication
    headers = sponsor_designer()

    # Use our local override helper
    with override_principal(designer_app, principal):
        # 1. Access study_foo (not in assigned_studies) -> should return 403
        resp_deny = await designer_client.get(
            "/api/v1/studies/study_foo/export", headers=headers
        )
        assert resp_deny.status_code == 403
        assert "Forbidden" in resp_deny.json()["detail"]

        # 2. Access study_bar (in assigned_studies) -> should pass the scope check.
        # It will then try to look up the study, which returns 404 because the study projection doesn't exist.
        resp_allow = await designer_client.get(
            "/api/v1/studies/study_bar/export", headers=headers
        )
        assert resp_allow.status_code == 404
        assert "not found" in resp_allow.json()["detail"].lower()
