"""Unit and integration tests for eISF document intelligence, binder classification, and eTMF cross-mapping.

@req:PRD-TMF-006
"""

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.eisf.database import db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base
from packages.testing.security import create_test_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_eisf_db():
    """Setup in-memory eISF database for testing FastAPI endpoints."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_eisf_document_intelligence_analyze_endpoint():
    """Verify /api/v1/eisf/intelligence/analyze recommends eISF folder and eTMF DIA artifact code.

    @req:PRD-TMF-006
    """
    headers = create_test_auth_headers(
        user_id="crc.user", roles=["crc"], site_id="SITE-101"
    )
    transport = ASGITransport(app=eisf_app)

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # Test 1: Investigator CV
        resp = await client.post(
            "/api/v1/eisf/intelligence/analyze",
            headers=headers,
            json={
                "filename": "Dr_Jones_CV.pdf",
                "content": "Curriculum Vitae Dr. Indiana Jones Site Number: SITE-101 Issue Date: 2026-05-01",
                "site_id": "SITE-101",
                "study_id": "STUDY-ARCH",
            },
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["recommended_binder_section"] == "05_STAFF_QUALIFICATIONS"
        assert data["recommended_etmf_artifact_code"] == "05.02.03"
        assert data["recommended_etmf_artifact_name"] == "Investigator CV"
        assert data["confidence"] >= 0.85

        # Test 2: FDA Form 1572
        resp_1572 = await client.post(
            "/api/v1/eisf/intelligence/analyze",
            headers=headers,
            json={
                "filename": "FDA_Form_1572_Signed.pdf",
                "content": "Statement of Investigator (FDA Form 1572) OMB 0910-0014 Investigator Signature: /s/ Dr. Jones",
                "site_id": "SITE-101",
                "study_id": "STUDY-ARCH",
            },
        )
        assert resp_1572.status_code == 200
        data_1572 = resp_1572.json()
        assert data_1572["recommended_binder_section"] == "04_REGULATORY"
        assert data_1572["recommended_etmf_artifact_code"] == "05.02.01"
        assert data_1572["signature_completeness"]["status"] == "FULLY_SIGNED"


@pytest.mark.asyncio
async def test_eisf_existing_site_document_intelligence_analysis():
    """Verify /api/v1/eisf/sites/{site_id}/documents/{doc_id}/analyze runs intelligence on saved documents.

    @req:PRD-TMF-006
    """
    headers = create_test_auth_headers(
        user_id="crc.user", roles=["crc"], site_id="SITE-101"
    )
    transport = ASGITransport(app=eisf_app)

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # First upload document via standard eISF endpoint
        upload_resp = await client.post(
            "/api/v1/eisf/documents",
            headers=headers,
            json={
                "study_id": "STUDY-001",
                "site_id": "SITE-101",
                "binder_classification": "05_STAFF_QUALIFICATIONS",
                "filename": "medical_license_dr_smith.pdf",
                "content": "State Medical Board Physician License Certification for Dr. Smith Issue Date: 2026-01-01",
                "mime_type": "application/pdf",
                "reason_for_change": "Initial regulatory upload for PI license verification",
            },
        )
        assert upload_resp.status_code == 201
        doc_id = upload_resp.json()["id"]

        # Run intelligence analysis on the created document
        resp = await client.post(
            f"/api/v1/eisf/sites/SITE-101/documents/{doc_id}/analyze",
            headers=headers,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["filename"] == "medical_license_dr_smith.pdf"
        assert data["recommended_etmf_artifact_code"] == "05.02.98"
        assert data["recommended_etmf_artifact_name"] == "Medical License"
