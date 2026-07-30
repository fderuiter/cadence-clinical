import io
import json
import time
import zipfile

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.eisf.database import db_manager as eisf_db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import ISFDocument
from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import Base, TMFDocument, is_site_level_artifact
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eTMF and eISF databases for testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    eisf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with eisf_db_manager.engine.begin() as conn:
        from apps.eisf.models import Base as EIsfBase

        await conn.run_sync(EIsfBase.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()

    async with eisf_db_manager.engine.begin() as conn:
        from apps.eisf.models import Base as EIsfBase

        await conn.run_sync(EIsfBase.metadata.drop_all)
    await eisf_db_manager.close()


def get_global_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """
    Helper to generate valid global (unscoped) V2 signed headers.
    """
    timestamp = str(time.time())
    user_id = "test_user"
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


def get_site_auth_headers(
    roles: str = "investigator", site_id: str = "site_alpha", change_reason: str = ""
) -> dict:
    """
    Helper to generate valid site-scoped V2 signed headers.
    """
    timestamp = str(time.time())
    user_id = f"user_{site_id}"
    sig = generate_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        version="2",
        change_reason=change_reason,
        site_id=site_id,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Site-Id": site_id,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


@pytest.mark.asyncio
async def test_is_site_level_artifact_helper():
    """
    Verify classification helper correctly determines site-scoped versus study-scoped artifacts.
    """
    assert is_site_level_artifact("FDA Form 1572") is True
    assert is_site_level_artifact("Investigator CV") is True
    assert is_site_level_artifact("Clinical Trial Protocol") is False
    assert is_site_level_artifact("Define-XML Specifications") is False
    assert (
        is_site_level_artifact("Some Custom Artifact", "05.02.04") is True
    )  # Code matches prefix
    assert is_site_level_artifact("Some Custom Artifact", "01.01.01") is False


@pytest.mark.asyncio
async def test_site_id_validation_empty_whitespace():
    """
    Ingesting empty or whitespace-only site_id should be rejected with HTTP 422.
    """
    client = TestClient(app)
    headers = get_global_auth_headers(roles="admin", change_reason="Test invalid scope")

    payload = {
        "study_id": "study_001",
        "site_id": "   ",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol content.",
        "mime_type": "application/pdf",
    }
    response = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert response.status_code == 422
    assert "whitespace-only" in response.json()["detail"]


@pytest.mark.asyncio
async def test_site_scoped_users_read_isolation():
    """
    Verify site-scoped users can only access matching site records, and cannot see other sites or study-level.
    """
    client = TestClient(app)
    admin_headers = get_global_auth_headers(
        roles="admin", change_reason="Ingest records"
    )

    # Ingest document for site_alpha
    res_alpha = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_001",
            "site_id": "site_alpha",
            "artifact_type": "Investigator CV",
            "filename": "cv_alpha.pdf",
            "content": "CV alpha content",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert res_alpha.status_code == 201
    doc_alpha_id = res_alpha.json()["document_id"]

    # Ingest document for site_beta
    res_beta = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_001",
            "site_id": "site_beta",
            "artifact_type": "Investigator CV",
            "filename": "cv_beta.pdf",
            "content": "CV beta content",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert res_beta.status_code == 201
    doc_beta_id = res_beta.json()["document_id"]

    # Ingest study-level document (site_id=None)
    res_study = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_001",
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol.pdf",
            "content": "Protocol content",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert res_study.status_code == 201
    doc_study_id = res_study.json()["document_id"]

    # Set up site_alpha credentials
    alpha_headers = get_site_auth_headers(roles="investigator", site_id="site_alpha")

    # 1. List checks: alpha user should ONLY see site_alpha CV
    list_resp = client.get("/api/v1/etmf/documents", headers=alpha_headers)
    assert list_resp.status_code == 200
    listed_ids = [d["id"] for d in list_resp.json()]
    assert doc_alpha_id in listed_ids
    assert doc_beta_id not in listed_ids
    assert (
        doc_study_id not in listed_ids
    )  # Study-level document is not visible to site-scoped user!

    # 2. View checks
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_alpha_id}", headers=alpha_headers
        ).status_code
        == 200
    )
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_beta_id}", headers=alpha_headers
        ).status_code
        == 403
    )
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_study_id}", headers=alpha_headers
        ).status_code
        == 403
    )

    # 3. Download checks
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_alpha_id}/download", headers=alpha_headers
        ).status_code
        == 200
    )
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_beta_id}/download", headers=alpha_headers
        ).status_code
        == 403
    )
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_study_id}/download", headers=alpha_headers
        ).status_code
        == 403
    )


@pytest.mark.asyncio
async def test_site_scoped_write_restrictions():
    """
    Verify site-scoped users can only ingest documents for their own assigned site.
    """
    client = TestClient(app)
    alpha_headers = get_site_auth_headers(
        roles="cra", site_id="site_alpha", change_reason="Ingest CV"
    )

    # Ingest for same site -> should succeed
    payload_ok = {
        "study_id": "study_001",
        "site_id": "site_alpha",
        "artifact_type": "Investigator CV",
        "filename": "cv_smith.pdf",
        "content": "Dr Smith CV.",
        "mime_type": "application/pdf",
    }
    assert (
        client.post(
            "/api/v1/etmf/ingest", json=payload_ok, headers=alpha_headers
        ).status_code
        == 201
    )

    # Ingest for another site -> should fail
    payload_bad = {
        "study_id": "study_001",
        "site_id": "site_beta",
        "artifact_type": "Investigator CV",
        "filename": "cv_jones.pdf",
        "content": "Dr Jones CV.",
        "mime_type": "application/pdf",
    }
    assert (
        client.post(
            "/api/v1/etmf/ingest", json=payload_bad, headers=alpha_headers
        ).status_code
        == 403
    )


@pytest.mark.asyncio
async def test_site_scoping_on_redactions_and_signatures():
    """
    Ensure site-scoped users cannot transition, sign, or redact documents from other sites or study-level.
    """
    client = TestClient(app)
    admin_headers = get_global_auth_headers(roles="admin", change_reason="Setup")
    alpha_headers = get_site_auth_headers(
        roles="cra", site_id="site_alpha", change_reason="Mutation test"
    )

    # Ingest site_beta document
    res = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_001",
            "site_id": "site_beta",
            "artifact_type": "Investigator CV",
            "filename": "cv.pdf",
            "content": "CV beta content",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    doc_beta_id = res.json()["document_id"]

    # Try transitions
    assert (
        client.post(
            f"/api/v1/etmf/documents/{doc_beta_id}/transition",
            json={
                "to_status": "TECHNICAL_QC",
                "reason_for_change": "Technical QC reviews",
            },
            headers=alpha_headers,
        ).status_code
        == 403
    )

    # Try sign-off (requires re-auth token)
    from jose import jwt

    sig_token = jwt.encode(
        {
            "sub": "user_site_alpha",
            "action": f"/api/v1/etmf/documents/{doc_beta_id}/sign-off",
            "exp": time.time() + 3600,
        },
        "internal-gateway-secret-12345",
        algorithm="HS256",
    )
    alpha_sign_headers = dict(alpha_headers)
    alpha_sign_headers["X-Sig-Token"] = sig_token

    assert (
        client.post(
            f"/api/v1/etmf/documents/{doc_beta_id}/sign-off",
            json={"signing_reason": "APPROVAL"},
            headers=alpha_sign_headers,
        ).status_code
        == 403
    )

    # Try redact
    assert (
        client.post(
            f"/api/v1/etmf/documents/{doc_beta_id}/redact",
            json={"redacted_content": "Redacted content", "manifest": {}},
            headers=alpha_headers,
        ).status_code
        == 403
    )


@pytest.mark.asyncio
async def test_auto_quarantine_site_level_no_site_id():
    """
    Verify that if site_id is missing on a site-level document ingestion, it is automatically quarantined.
    """
    client = TestClient(app)
    headers = get_global_auth_headers(roles="admin", change_reason="Ingestion")

    # Ingest site-level artifact ("Investigator CV") without site_id
    res = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_001",
            "artifact_type": "Investigator CV",
            "filename": "cv_unassigned.pdf",
            "content": "CV content.",
            "mime_type": "application/pdf",
        },
        headers=headers,
    )
    assert res.status_code == 201
    doc_id = res.json()["document_id"]

    # Verify site_id is "QUARANTINED" in DB
    async with db_manager.get_session_maker()() as session:
        doc = await session.get(TMFDocument, doc_id)
        assert doc.site_id == "QUARANTINED"


@pytest.mark.asyncio
async def test_legacy_records_quarantine_policy():
    """
    Verify the migration script safely backfills legacy records (quarantines missing site scopes for site-level documents).
    """
    # 1. Manually insert legacy records with NULL site_id
    async with db_manager.get_session_maker()() as session:
        # A site-level document without site_id
        doc_site = TMFDocument(
            id="legacy_site_doc",
            study_id="study_001",
            site_id=None,
            zone=5,
            section="05.02",
            artifact_type="Investigator CV",
            filename="legacy_cv.pdf",
            content="Legacy CV content",
            mime_type="application/pdf",
            created_by="system",
            version_index=1,
            taxonomy_version="v3.2.0-complete",
            artifact_code="05.02.03",
        )
        # A study-level document without site_id (legitimate None)
        doc_study = TMFDocument(
            id="legacy_study_doc",
            study_id="study_001",
            site_id=None,
            zone=1,
            section="01.01",
            artifact_type="Clinical Trial Protocol",
            filename="protocol.pdf",
            content="Protocol text",
            mime_type="application/pdf",
            created_by="system",
            version_index=1,
            taxonomy_version="v3.2.0-complete",
            artifact_code="01.01.01",
        )
        session.add(doc_site)
        session.add(doc_study)
        await session.commit()

    # 2. Run migrate.upgrade_existing_tables to run backfill/quarantine
    from apps.etmf.migrate import upgrade_existing_tables

    async with db_manager.engine.begin() as conn:
        await upgrade_existing_tables(conn, "sqlite")

    # 3. Check values in DB
    async with db_manager.get_session_maker()() as session:
        res_site = await session.get(TMFDocument, "legacy_site_doc")
        assert res_site.site_id == "QUARANTINED"  # Site-level is quarantined!

        res_study = await session.get(TMFDocument, "legacy_study_doc")
        assert res_study.site_id is None  # Study-level remains None!


@pytest.mark.asyncio
async def test_eisf_to_etmf_sync_preserves_scope(monkeypatch):
    """
    Verify eISF sync preserves and propagates site_id accurately to eTMF.
    """
    eisf_client = TestClient(eisf_app)
    from tests.test_eisf_api import get_eisf_auth_headers

    eisf_headers = get_eisf_auth_headers(
        user_id="pi_user",
        roles="site investigator",
        site_id="site_boston",
        change_reason="Syncing required documents",
    )

    # Mock httpx AsyncClient post to capture propagated payload to eTMF
    captured_payloads = []

    async def mock_post(self_client, url, *args, **kwargs):
        captured_payloads.append(kwargs.get("json"))
        import httpx

        return httpx.Response(
            201, json={"status": "success", "document_id": "propagated_id"}
        )

    import httpx

    monkeypatch.setattr(httpx.AsyncClient, "post", mock_post)

    payload = {
        "submissions": [
            {
                "study_id": "study_001",
                "site_id": "site_boston",
                "binder_classification": "Investigator CV",
                "filename": "cv_boston.pdf",
                "content": "Dr. Boston CV.",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "CLIENT_WINS",
            }
        ]
    }

    resp = eisf_client.post("/api/v1/eisf/sync", json=payload, headers=eisf_headers)
    assert resp.status_code == 200

    # Verify site_id is preserved in eISF DB
    async with eisf_db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(ISFDocument).where(ISFDocument.filename == "cv_boston.pdf")
        )
        doc = res.scalars().one()
        assert doc.site_id == "site_boston"

    # Verify site_id was correctly forwarded to eTMF in the propagation payload
    assert len(captured_payloads) == 1
    assert captured_payloads[0]["site_id"] == "site_boston"
    assert captured_payloads[0]["study_id"] == "study_001"


@pytest.mark.asyncio
async def test_completeness_site_isolation():
    """
    Ensure site-scoped users cannot check completeness of studies for other sites.
    """
    client = TestClient(app)
    alpha_headers = get_site_auth_headers(roles="investigator", site_id="site_alpha")

    # Run for same site -> should succeed (returns 200)
    res_ok = client.get(
        "/api/v1/etmf/completeness?study_id=study_001&site_id=site_alpha&milestone=INITIATION",
        headers=alpha_headers,
    )
    assert res_ok.status_code == 200

    # Run for other site -> should be rejected with 403 Forbidden
    res_bad = client.get(
        "/api/v1/etmf/completeness?study_id=study_001&site_id=site_beta&milestone=INITIATION",
        headers=alpha_headers,
    )
    assert res_bad.status_code == 403


@pytest.mark.asyncio
async def test_regulatory_binder_export_site_isolation():
    """
    Verify regulatory binder export organizes files and filters correctly for site-scoped auditors.
    """
    client = TestClient(app)
    admin_headers = get_global_auth_headers(
        roles="admin", change_reason="Setup study docs"
    )

    # Ingest a site-level document for site_alpha
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_export_test",
            "site_id": "site_alpha",
            "artifact_type": "Investigator CV",
            "filename": "cv_alpha.pdf",
            "content": "Alpha CV content.",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )

    # Ingest a site-level document for site_beta
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_export_test",
            "site_id": "site_beta",
            "artifact_type": "Investigator CV",
            "filename": "cv_beta.pdf",
            "content": "Beta CV content.",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )

    # Auditor scoped to site_alpha only
    auditor_headers = get_site_auth_headers(
        roles="regulatory_inspector", site_id="site_alpha"
    )

    # Export regulatory binder
    resp = client.get(
        "/api/v1/etmf/studies/study_export_test/binder", headers=auditor_headers
    )
    assert resp.status_code == 200

    # Inspect ZIP contents
    with zipfile.ZipFile(io.BytesIO(resp.content)) as z:
        filenames = z.namelist()

        # Should contain site_alpha CV but NOT site_beta CV
        assert "Zone 05/05.02/cv_alpha.pdf" in filenames
        assert "Zone 05/05.02/cv_beta.pdf" not in filenames

        # Verify manifest.json matches
        manifest = json.loads(z.read("manifest.json").decode("utf-8"))
        doc_entries = manifest["documents"]
        assert len(doc_entries) == 1
        assert doc_entries[0]["site_id"] == "site_alpha"
        assert doc_entries[0]["filename"] == "cv_alpha.pdf"


@pytest.mark.asyncio
async def test_unauthorized_role_denied_on_all_paths():
    client = TestClient(app)
    admin_headers = get_global_auth_headers(roles="admin", change_reason="Setup")

    # Ingest a real document first
    res = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_001",
            "site_id": "site_alpha",
            "artifact_type": "Investigator CV",
            "filename": "cv_test.pdf",
            "content": "CV test content",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert res.status_code == 201
    doc_id = res.json()["document_id"]

    # Role "subject" does not have etmf_document:read
    headers = get_global_auth_headers(roles="subject")

    # 1. list_documents
    assert client.get("/api/v1/etmf/documents", headers=headers).status_code == 403

    # 2. view_document
    assert (
        client.get(f"/api/v1/etmf/documents/{doc_id}", headers=headers).status_code
        == 403
    )

    # 3. download_document
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_id}/download", headers=headers
        ).status_code
        == 403
    )

    # 4. history (transitions)
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_id}/transitions", headers=headers
        ).status_code
        == 403
    )

    # 5. qc-history
    assert (
        client.get(
            f"/api/v1/etmf/documents/{doc_id}/qc-history", headers=headers
        ).status_code
        == 403
    )

    # 6. completeness
    assert (
        client.get(
            "/api/v1/etmf/completeness?study_id=study_001&milestone=INITIATION",
            headers=headers,
        ).status_code
        == 403
    )

    # 7. binder structure
    assert (
        client.get(
            "/api/v1/etmf/studies/study_001/binder/structure", headers=headers
        ).status_code
        == 403
    )

    # 8. export regulatory binder
    assert (
        client.get("/api/v1/etmf/studies/study_001/binder", headers=headers).status_code
        == 403
    )


@pytest.mark.asyncio
async def test_site_scoped_no_assigned_sites_fail_closed():
    client = TestClient(app)
    # Generate headers for investigator with empty site_id (empty assigned sites)
    headers = get_site_auth_headers(roles="investigator", site_id="")

    # list_documents should return an empty list because of fail-closed SQL predicate (1 == 0)
    resp = client.get("/api/v1/etmf/documents", headers=headers)
    assert resp.status_code == 200
    assert resp.json() == []


@pytest.mark.asyncio
async def test_raw_original_suppression_without_read_raw():
    client = TestClient(app)
    admin_headers = get_global_auth_headers(roles="admin", change_reason="Ingestion")

    # 1. Ingest raw original
    res_orig = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_redact_test",
            "site_id": "site_alpha",
            "artifact_type": "Investigator CV",
            "filename": "cv_raw.pdf",
            "content": "CV raw unredacted content containing PII",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert res_orig.status_code == 201
    orig_id = res_orig.json()["document_id"]

    # 2. Ingest/Create redacted successor linked to original
    res_red = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_redact_test",
            "site_id": "site_alpha",
            "artifact_type": "Investigator CV",
            "filename": "cv_redacted.pdf",
            "content": "CV redacted content containing no PII",
            "mime_type": "application/pdf",
            "metadata_json": {
                "is_redacted": True,
            },
        },
        headers=admin_headers,
    )
    assert res_red.status_code == 201
    red_id = res_red.json()["document_id"]

    # Manually link them in DB using database session
    async with db_manager.get_session_maker()() as session:
        await session.get(TMFDocument, orig_id)
        doc_red = await session.get(TMFDocument, red_id)
        doc_red.is_redacted = True
        doc_red.redaction_source_id = orig_id
        await session.commit()

    # 3. Request lists/view as user lacking read_raw (e.g. investigator)
    inv_headers = get_site_auth_headers(roles="investigator", site_id="site_alpha")

    list_resp = client.get(
        "/api/v1/etmf/documents?study_id=study_redact_test", headers=inv_headers
    )
    assert list_resp.status_code == 200
    listed_ids = [d["id"] for d in list_resp.json()]

    # Raw original must be suppressed, redacted successor must be present
    assert orig_id not in listed_ids
    assert red_id in listed_ids

    # Viewing/downloading the raw original must be blocked (403)
    assert (
        client.get(f"/api/v1/etmf/documents/{orig_id}", headers=inv_headers).status_code
        == 403
    )
    assert (
        client.get(
            f"/api/v1/etmf/documents/{orig_id}/download", headers=inv_headers
        ).status_code
        == 403
    )

    # Viewing/downloading the redacted successor must be allowed (200)
    assert (
        client.get(f"/api/v1/etmf/documents/{red_id}", headers=inv_headers).status_code
        == 200
    )
    assert (
        client.get(
            f"/api/v1/etmf/documents/{red_id}/download", headers=inv_headers
        ).status_code
        == 200
    )

    # 4. Request as user with read_raw (e.g. admin)
    list_admin = client.get(
        "/api/v1/etmf/documents?study_id=study_redact_test", headers=admin_headers
    )
    assert list_admin.status_code == 200
    admin_listed_ids = [d["id"] for d in list_admin.json()]

    # Both must be present
    assert orig_id in admin_listed_ids
    assert red_id in admin_listed_ids

    # View/download orig must be allowed
    assert (
        client.get(
            f"/api/v1/etmf/documents/{orig_id}", headers=admin_headers
        ).status_code
        == 200
    )
    assert (
        client.get(
            f"/api/v1/etmf/documents/{orig_id}/download", headers=admin_headers
        ).status_code
        == 200
    )


@pytest.mark.asyncio
async def test_binder_export_redaction_representation_policy():
    client = TestClient(app)
    admin_headers = get_global_auth_headers(roles="admin", change_reason="Setup")

    # Ingest raw original
    res_orig = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_binder_redact_test",
            "site_id": "site_alpha",
            "artifact_type": "Investigator CV",
            "filename": "cv_raw.pdf",
            "content": "CV raw unredacted content",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    orig_id = res_orig.json()["document_id"]

    # Ingest redacted successor
    res_red = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_binder_redact_test",
            "site_id": "site_alpha",
            "artifact_type": "Investigator CV",
            "filename": "cv_redacted.pdf",
            "content": "CV redacted content",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    red_id = res_red.json()["document_id"]

    # Manually link them in DB
    async with db_manager.get_session_maker()() as session:
        await session.get(TMFDocument, orig_id)
        doc_red = await session.get(TMFDocument, red_id)
        doc_red.is_redacted = True
        doc_red.redaction_source_id = orig_id
        await session.commit()

    # Auditor scoped to site_alpha only, who has etmf_document:read but NOT read_raw
    auditor_headers = get_site_auth_headers(
        roles="regulatory_inspector", site_id="site_alpha"
    )

    # Export binder with include_history=True
    resp = client.get(
        "/api/v1/etmf/studies/study_binder_redact_test/binder?include_history=True",
        headers=auditor_headers,
    )
    assert resp.status_code == 200

    # Read the zip file
    with zipfile.ZipFile(io.BytesIO(resp.content)) as z:
        manifest = json.loads(z.read("manifest.json").decode("utf-8"))
        doc_ids = [d["id"] for d in manifest["documents"]]

        # Original (unredacted) ID must NOT be present in manifest
        assert orig_id not in doc_ids
        # Redacted successor ID must be present
        assert red_id in doc_ids


@pytest.mark.asyncio
async def test_site_scoped_cannot_read_study_level_or_quarantined_documents():
    client = TestClient(app)
    admin_headers = get_global_auth_headers(
        roles="admin", change_reason="Setup study docs"
    )

    # 1. Ingest a study-level document (site_id=None) but set its site_id to "site_alpha" in DB to check zone/artifact-type check
    res_study = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_attr_test",
            "site_id": "site_alpha",
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol_alpha.pdf",
            "content": "Protocol alpha",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert res_study.status_code == 201
    study_doc_id = res_study.json()["document_id"]

    # 2. Ingest a quarantined document (site_id is "QUARANTINED")
    res_quar = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_attr_test",
            "artifact_type": "Investigator CV",
            "filename": "cv_quar.pdf",
            "content": "Quarantined CV",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert res_quar.status_code == 201
    quar_doc_id = res_quar.json()["document_id"]

    # Retrieve as investigator at site_alpha
    inv_headers = get_site_auth_headers(roles="investigator", site_id="site_alpha")

    # Listing documents for study_attr_test should exclude both the study-level protocol and the quarantined document!
    list_resp = client.get(
        "/api/v1/etmf/documents?study_id=study_attr_test", headers=inv_headers
    )
    assert list_resp.status_code == 200
    listed_ids = [d["id"] for d in list_resp.json()]
    assert study_doc_id not in listed_ids
    assert quar_doc_id not in listed_ids

    # Viewing/downloading them directly must return 403 Forbidden
    assert (
        client.get(
            f"/api/v1/etmf/documents/{study_doc_id}", headers=inv_headers
        ).status_code
        == 403
    )
    assert (
        client.get(
            f"/api/v1/etmf/documents/{quar_doc_id}", headers=inv_headers
        ).status_code
        == 403
    )
