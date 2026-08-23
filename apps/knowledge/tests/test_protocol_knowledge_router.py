"""
Tests for Knowledge Microservice Protocol Ingestion and Search Endpoints.

Requirements:
- @req:PRD-TCK-005 (Grounded Protocol Knowledge Hub & Support Ticket RAG Triage)
- @req:PRD-SYS-051 (AI Gateway Microservice and Three-Tier Clinical Intelligence Architecture)
"""

from __future__ import annotations

import io

import pytest
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.main import app
from packages.testing.security import create_test_auth_headers

SAMPLE_PROTOCOL_DOC = """
--- Page 1 ---
PROTOCOL STUDY-KNB-001
Section 1.0 Overview
This is a phase 2 clinical trial testing compound Zeta.

--- Page 2 ---
Section 5.2 Dosage and Administration
Administer 50 mg orally once daily in the morning with food.

--- Page 3 ---
Section 9.1 Serious Adverse Event Reporting
Report all SAEs to Sponsor Safety within 24 hours of notification.
"""


@pytest.mark.asyncio
async def test_protocol_text_ingest_and_search_endpoints(db_session: AsyncSession):
    """Verify protocol text ingestion and subsequent semantic vector search via REST API.

    @req:PRD-TCK-005
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="sponsor_lead",
        roles=["sponsor_designer"],
        change_reason="Ingest protocol document",
    )

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Ingest protocol text
        ingest_payload = {
            "study_id": "STUDY-KNB-001",
            "protocol_version": "v1.0",
            "raw_text": SAMPLE_PROTOCOL_DOC,
            "filename": "protocol_knb_001.txt",
            "is_approved": True,
            "reason_for_change": "Initial protocol version ingestion",
        }
        resp = await client.post(
            "/api/v1/knowledge/protocols/ingest",
            json=ingest_payload,
            headers=headers,
        )
        assert resp.status_code == 201
        chunks = resp.json()
        assert len(chunks) >= 3
        chunk_id = chunks[0]["chunk_id"]

        # 2. Search protocol chunks
        search_payload = {
            "query": "What is the SAE reporting timeframe?",
            "study_id": "STUDY-KNB-001",
            "protocol_version": "v1.0",
            "top_k": 3,
        }
        search_resp = await client.post(
            "/api/v1/knowledge/protocols/search",
            json=search_payload,
            headers=headers,
        )
        assert search_resp.status_code == 200
        search_results = search_resp.json()
        assert len(search_results) > 0
        top = search_results[0]
        assert top["section_number"] == "9.1"
        assert top["page_number"] == 3
        assert "[Protocol v1.0, Section 9.1, Page 3]" in top["citation_marker"]

        # 3. Retrieve single chunk by ID
        chunk_resp = await client.get(
            f"/api/v1/knowledge/protocols/chunks/{chunk_id}",
            headers=headers,
        )
        assert chunk_resp.status_code == 200
        chunk_data = chunk_resp.json()
        assert chunk_data["chunk_id"] == chunk_id


@pytest.mark.asyncio
async def test_protocol_upload_file_endpoint(db_session: AsyncSession):
    """Verify multipart file upload endpoint for protocol documents.

    @req:PRD-TCK-005
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="dm_user",
        roles=["data_manager"],
        change_reason="Upload protocol file",
    )

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        file_content = SAMPLE_PROTOCOL_DOC.encode("utf-8")
        files = {
            "file": ("protocol.txt", io.BytesIO(file_content), "text/plain"),
        }
        data = {
            "study_id": "STUDY-UPLOAD-002",
            "protocol_version": "v2.0",
            "document_type": "PROTOCOL",
            "is_approved": "true",
            "reason_for_change": "Upload protocol document file",
        }
        resp = await client.post(
            "/api/v1/knowledge/protocols/upload",
            data=data,
            files=files,
            headers=headers,
        )
        assert resp.status_code == 201
        chunks = resp.json()
        assert len(chunks) >= 3
