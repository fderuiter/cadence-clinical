import io
import json
import time
import zipfile

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.models import Base, TMFDocument
from apps.etmf.main import app
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eTMF database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "admin", change_reason: str = "", user_id: str = "test_user"
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
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


@pytest.mark.asyncio
async def test_classification_driven_multi_site_export():
    """
    Test the multi-site export behavior driven by document classifications:
    1. Study-level documents are deduplicated by artifact_code alone (when include_history=False).
    2. Site-level documents are deduplicated by (artifact_code, site_id) (when include_history=False).
    3. Quarantined site_id == 'QUARANTINED' documents are omitted.
    4. Site-level documents with unresolved site metadata (empty/None site_id) are omitted.
    5. Name collisions in the ZIP folder structure are resolved via numerical suffixes.
    6. The manifest.json populates site_id for site-specific records and leaves study-level records null.

    @req:PRD-TMF-001
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Ingest documents")
    auditor_headers = get_auth_headers(roles="regulatory_inspector")

    study_id = "study_multi_site_test"

    # --- INGEST STUDY-LEVEL DOCUMENTS ---
    # Study-level (Clinical Trial Protocol, artifact_code = "01.01.01", zone = 1, section = "01.01")
    # We ingest version 1 and version 2 of Clinical Trial Protocol
    p1 = {
        "study_id": study_id,
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.txt",
        "content": "Protocol Version 1 Content",
        "mime_type": "text/plain",
    }
    r1 = client.post("/api/v1/etmf/ingest", json=p1, headers=admin_headers)
    assert r1.status_code == 201

    p2 = {
        "study_id": study_id,
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.txt",
        "content": "Protocol Version 2 Content",
        "mime_type": "text/plain",
    }
    r2 = client.post("/api/v1/etmf/ingest", json=p2, headers=admin_headers)
    assert r2.status_code == 201

    # --- INGEST SITE-LEVEL DOCUMENTS ---
    # Site-level (Investigator CV, zone = 5, section = "05.02")
    # For Site A: we ingest version 1 and version 2 of Investigator CV
    s_a1 = {
        "study_id": study_id,
        "site_id": "SiteA",
        "artifact_type": "Investigator CV",
        "filename": "cv.txt",
        "content": "Investigator CV Site A V1 Content",
        "mime_type": "text/plain",
    }
    rs_a1 = client.post("/api/v1/etmf/ingest", json=s_a1, headers=admin_headers)
    assert rs_a1.status_code == 201

    s_a2 = {
        "study_id": study_id,
        "site_id": "SiteA",
        "artifact_type": "Investigator CV",
        "filename": "cv.txt",
        "content": "Investigator CV Site A V2 Content",
        "mime_type": "text/plain",
    }
    rs_a2 = client.post("/api/v1/etmf/ingest", json=s_a2, headers=admin_headers)
    assert rs_a2.status_code == 201

    # For Site B: we ingest version 1 of Investigator CV
    s_b1 = {
        "study_id": study_id,
        "site_id": "SiteB",
        "artifact_type": "Investigator CV",
        "filename": "cv.txt",
        "content": "Investigator CV Site B V1 Content",
        "mime_type": "text/plain",
    }
    rs_b1 = client.post("/api/v1/etmf/ingest", json=s_b1, headers=admin_headers)
    assert rs_b1.status_code == 201

    # --- INGEST QUARANTINED & UNRESOLVED DOCUMENTS ---
    # Quarantined document (site_id == "QUARANTINED")
    q_doc = {
        "study_id": study_id,
        "site_id": "QUARANTINED",
        "artifact_type": "Investigator CV",
        "filename": "cv_q.txt",
        "content": "Quarantined CV Content",
        "mime_type": "text/plain",
    }
    rq = client.post("/api/v1/etmf/ingest", json=q_doc, headers=admin_headers)
    assert rq.status_code == 201

    # Unresolved site metadata document (site-level but missing site_id or empty)
    # The ingestion service should automatically quarantine or assign QUARANTINED to site-level documents without site_id
    u_doc = {
        "study_id": study_id,
        "artifact_type": "Investigator CV",
        "filename": "cv_u.txt",
        "content": "Unresolved CV Content",
        "mime_type": "text/plain",
    }
    ru = client.post("/api/v1/etmf/ingest", json=u_doc, headers=admin_headers)
    assert ru.status_code == 201
    assert ru.json()["site_id"] in ["QUARANTINED", None]

    # --- EXPORT REGULATORY BINDER (include_history=False) ---
    export_resp = client.get(
        f"/api/v1/etmf/studies/{study_id}/binder?include_history=false",
        headers=auditor_headers,
    )
    assert export_resp.status_code == 200

    zip_bytes = export_resp.content
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as z:
        filenames = z.namelist()

        # Check files present in the root
        assert "manifest.json" in filenames
        assert "audit_summary.json" in filenames

        # 1. Study-level: Only latest version (v2) of Clinical Trial Protocol should be present (protocol.txt)
        # 2. Site-level: Latest version of Investigator CV for Site A (V2) and Site B (V1) should both be present!
        # Due to filename collision resolution, we expect "Zone 05/05.02/cv.txt" and "Zone 05/05.02/cv_1.txt"
        assert "Zone 01/01.01/protocol.txt" in filenames
        assert "Zone 05/05.02/cv.txt" in filenames
        assert "Zone 05/05.02/cv_1.txt" in filenames

        # Ensure quarantined and unresolved documents are omitted!
        assert not any("cv_q.txt" in f for f in filenames)
        assert not any("cv_u.txt" in f for f in filenames)

        # Check watermarked contents
        p_content = z.read("Zone 01/01.01/protocol.txt").decode("utf-8")
        assert "Protocol Version 2 Content" in p_content

        f1 = z.read("Zone 05/05.02/cv.txt").decode("utf-8")
        f2 = z.read("Zone 05/05.02/cv_1.txt").decode("utf-8")
        # Since we sorted deterministically by site_id, "SiteA" comes before "SiteB"
        # So "Zone 05/05.02/cv.txt" should be Site A V2, and "Zone 05/05.02/cv_1.txt" should be Site B V1
        assert "Investigator CV Site A V2 Content" in f1
        assert "Investigator CV Site B V1 Content" in f2

        # 3. Verify manifest metadata
        manifest_text = z.read("manifest.json").decode("utf-8")
        manifest_data = json.loads(manifest_text)

        assert manifest_data["study_id"] == study_id
        assert manifest_data["include_history"] is False
        assert manifest_data["document_count"] == 3

        docs_meta = manifest_data["documents"]
        # There should be exactly 3 documents exported
        assert len(docs_meta) == 3

        # Locate documents in metadata
        proto_meta = next(
            d for d in docs_meta if d["artifact_type"] == "Clinical Trial Protocol"
        )
        site_a_meta = next(
            d
            for d in docs_meta
            if d["artifact_type"] == "Investigator CV"
            and d["archive_path"] == "Zone 05/05.02/cv.txt"
        )
        site_b_meta = next(
            d
            for d in docs_meta
            if d["artifact_type"] == "Investigator CV"
            and d["archive_path"] == "Zone 05/05.02/cv_1.txt"
        )

        # Verify site_id populations: study-level is None (null), site-level is populated with clinical site id
        assert proto_meta["site_id"] is None
        assert site_a_meta["site_id"] == "SiteA"
        assert site_b_meta["site_id"] == "SiteB"


@pytest.mark.asyncio
async def test_unresolved_site_level_omit_manually():
    """
    Directly verify that a site-level TMFDocument with null or empty site_id is omitted from exports
    regardless of include_history setting.

    @req:PRD-TMF-001
    """
    async with db_manager.get_session_maker()() as session:
        # Create an artificial document in database that is site-level (Investigator CV) but has unresolved site_id = None
        doc = TMFDocument(
            study_id="study_unresolved_test",
            site_id=None,
            zone=5,
            section="05.02",
            artifact_type="Investigator CV",
            filename="unresolved_cv.txt",
            content="Some unresolved Investigator CV content",
            mime_type="text/plain",
            created_by="admin",
            status="APPROVED",
            artifact_code="05.02.03",
        )
        session.add(doc)
        await session.commit()

    client = TestClient(app)
    auditor_headers = get_auth_headers(roles="regulatory_inspector")

    # Export binder
    resp = client.get(
        "/api/v1/etmf/studies/study_unresolved_test/binder?include_history=false",
        headers=auditor_headers,
    )
    assert resp.status_code == 200
    with zipfile.ZipFile(io.BytesIO(resp.content)) as z:
        manifest_text = z.read("manifest.json").decode("utf-8")
        manifest_data = json.loads(manifest_text)
        assert manifest_data["document_count"] == 0
        assert len(manifest_data["documents"]) == 0
