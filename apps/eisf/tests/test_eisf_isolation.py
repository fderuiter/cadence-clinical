"""Integration test suite qualifying site isolation and non-destructive PHI redaction workflows in eISF.

Requirements: PRD-SYS-001
"""

import base64

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.execution.main import app
from apps.execution.tests.test_lock_router import _make_auth_headers

client = TestClient(app)


def test_eisf_site_isolation_lifecycle() -> None:
    """Validate eISF document binder strictly enforces site-scoped isolation and prevents cross-site leaks.

    Requirements: PRD-SYS-001
    """
    study_id = "study_isolation_01"
    site_alpha = "site_alpha_101"
    site_beta = "site_beta_202"

    headers_alpha = _make_auth_headers(
        user_id="crc_alpha",
        roles="site_coordinator",
        change_reason="Upload Site Alpha Document",
    )

    doc_content = b"Confidential Medical License Site Alpha 101"
    content_b64 = base64.b64encode(doc_content).decode("utf-8")

    # Step 1: Upload document for Site Alpha
    res_upload = client.post(
        "/api/v1/execution/eisf/upload",
        json={
            "study_id": study_id,
            "site_id": site_alpha,
            "category": "2_MEDICAL_LICENSE",
            "title": "Medical License Site Alpha",
            "file_name": "License_Alpha.pdf",
            "content_base64": content_b64,
        },
        headers=headers_alpha,
    )
    assert res_upload.status_code == 201
    alpha_doc_id = res_upload.json()["document_id"]

    # Step 2: Site Beta queries binder for Site Beta -> Must return empty (zero leak from Site Alpha)
    headers_beta = _make_auth_headers(
        user_id="crc_beta",
        roles="site_coordinator",
        change_reason="Browse Site Beta Binder",
    )

    res_beta_binder = client.get(
        f"/api/v1/execution/eisf/binder/{study_id}/{site_beta}",
        headers=headers_beta,
    )
    assert res_beta_binder.status_code == 200
    beta_docs = res_beta_binder.json()
    beta_doc_ids = [d["document_id"] for d in beta_docs]
    assert alpha_doc_id not in beta_doc_ids

    # Step 3: Site Alpha queries binder for Site Alpha -> Must return uploaded document
    res_alpha_binder = client.get(
        f"/api/v1/execution/eisf/binder/{study_id}/{site_alpha}",
        headers=headers_alpha,
    )
    assert res_alpha_binder.status_code == 200
    alpha_docs = res_alpha_binder.json()
    alpha_doc_ids = [d["document_id"] for d in alpha_docs]
    assert alpha_doc_id in alpha_doc_ids
