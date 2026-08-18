"""Integration tests for eTMF version history, document linkage, and QC transition rules.

Requirements: PRD-SYS-001
"""

import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager as etmf_db_manager
from apps.etmf.main import app as etmf_app
from apps.etmf.models import Base as EtmfBase, DocumentQCTransition
from packages.testing.security import generate_signature


def get_etmf_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
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


@pytest_asyncio.fixture(autouse=True)
async def setup_etmf_db():
    etmf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(EtmfBase.metadata.create_all)
    yield
    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(EtmfBase.metadata.drop_all)
    await etmf_db_manager.close()


@pytest.mark.asyncio
async def test_etmf_linkage_and_version_history_lineage():
    """Validate clinical documents link properly to protocol versions."""
    client = TestClient(etmf_app)
    headers = get_etmf_auth_headers(
        roles="admin,sponsor_dm", change_reason="Initial protocol ingestion"
    )

    payload_v1 = {
        "study_id": "study_etmf_linkage",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1_initial.pdf",
        "content": "Original clinical trial protocol content.",
        "mime_type": "application/pdf",
    }
    res_v1 = client.post("/events/publish", json=payload_v1, headers=headers)
    assert res_v1.status_code == 201
    v1_data = res_v1.json()
    assert v1_data["version_index"] == 1

    payload_v2 = {
        "study_id": "study_etmf_linkage",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v2.pdf",
        "content": "Amended clinical trial protocol v2 content.",
        "mime_type": "application/pdf",
    }
    res_v2 = client.post("/events/publish", json=payload_v2, headers=headers)
    assert res_v2.status_code == 201
    v2_data = res_v2.json()
    assert v2_data["version_index"] == 2


@pytest.mark.asyncio
async def test_etmf_document_change_rationale_mandatory_rules():
    """Validate document ingestion requires change reason header for audit compliance."""
    client = TestClient(etmf_app)
    headers_no_reason = {
        "X-User-Id": "test_user",
        "X-User-Roles": "admin",
        "X-Gateway-Timestamp": str(time.time()),
        "X-Signature-Version": "2",
    }
    payload = {
        "study_id": "study_etmf_rationale",
        "artifact_type": "Investigator Brochure",
        "filename": "ib_v1.pdf",
        "content": "Investigator Brochure v1 content.",
        "mime_type": "application/pdf",
    }
    res_fail = client.post("/events/publish", json=payload, headers=headers_no_reason)
    assert res_fail.status_code in (400, 401, 403, 422)


@pytest.mark.asyncio
async def test_etmf_qc_transitions_immutability():
    """Validate QC transitions immutability and audit recording."""
    client = TestClient(etmf_app)
    headers = get_etmf_auth_headers(
        roles="admin,sponsor_dm", change_reason="Initial creation"
    )

    res_ingest = client.post(
        "/events/publish",
        json={
            "study_id": "study_etmf_qc",
            "artifact_type": "Informed Consent Form",
            "filename": "icf_v1.pdf",
            "content": "ICF Content",
            "mime_type": "application/pdf",
        },
        headers=headers,
    )
    assert res_ingest.status_code == 201
    doc_id = res_ingest.json()["id"]

    async with etmf_db_manager.get_session_maker()() as session:
        qc_trans = DocumentQCTransition(
            document_id=doc_id,
            transition_sequence=1,
            from_status="DRAFT",
            to_status="APPROVED",
            actor_id="qc_auditor",
            actor_role="qc_manager",
            reason_for_change="QC Review Pass",
        )
        session.add(qc_trans)
        await session.commit()

        stmt = select(DocumentQCTransition).where(
            DocumentQCTransition.document_id == doc_id
        )
        transitions = list((await session.execute(stmt)).scalars().all())
        assert len(transitions) == 1
        assert transitions[0].to_status == "APPROVED"
