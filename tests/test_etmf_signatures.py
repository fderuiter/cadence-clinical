import time
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.main import app, map_artifact_to_tmf
from apps.etmf.models import Base, TMFDocument, ExpectedDocument
from tests.test_etmf import get_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eTMF database for signature and routing testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_headers(roles: str = "admin", change_reason: str = "") -> dict:
    return get_auth_headers(roles=roles, change_reason=change_reason)


@pytest.mark.asyncio
async def test_signature_document_routing_and_classification():
    """
    Verify that FDA Form 1572, Financial Disclosure, and Protocol Sign-off
    are correctly routed to their respective sections and classified with typed document types.
    """
    client = TestClient(app)
    headers = get_headers(roles="admin", change_reason="Ingest signature lifecycle documents")

    # 1. FDA Form 1572 -> Zone 5, Section 05.02 (Bypassing signature requirement with override)
    resp_1572 = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_sig_01",
            "artifact_type": "FORM_1572",
            "filename": "form1572.pdf",
            "content": "Statement of Investigator Form 1572 content.",
            "mime_type": "application/pdf",
            "metadata_json": {"requires_signature": False},
        },
        headers=headers,
    )
    assert resp_1572.status_code == 201
    data_1572 = resp_1572.json()
    assert data_1572["zone"] == 5
    assert data_1572["section"] == "05.02"
    assert data_1572["artifact_code"] == "05.02.01"

    # 2. Financial Disclosure -> Zone 5, Section 05.02 (Bypassing signature requirement with override)
    resp_fin = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_sig_01",
            "artifact_type": "FINANCIAL_DISCLOSURE",
            "filename": "findisclose.pdf",
            "content": "Investigator Financial Disclosure Form content.",
            "mime_type": "application/pdf",
            "metadata_json": {"requires_signature": False},
        },
        headers=headers,
    )
    assert resp_fin.status_code == 201
    data_fin = resp_fin.json()
    assert data_fin["zone"] == 5
    assert data_fin["section"] == "05.02"
    assert data_fin["artifact_code"] == "05.02.02"

    # 3. Protocol Sign-off -> Zone 1, Section 01.01 (Bypassing signature requirement with override)
    resp_so = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_sig_01",
            "artifact_type": "PROTOCOL_SIGNOFF",
            "filename": "signoff.pdf",
            "content": "Protocol signature approval sign-off page content.",
            "mime_type": "application/pdf",
            "metadata_json": {"requires_signature": False},
        },
        headers=headers,
    )
    assert resp_so.status_code == 201
    data_so = resp_so.json()
    assert data_so["zone"] == 1
    assert data_so["section"] == "01.01"
    assert data_so["artifact_code"] == "01.01.03"

    # 4. Ingesting without signature AND without override must fail with 422
    resp_fail = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_sig_01",
            "artifact_type": "FORM_1572",
            "filename": "form1572_fail.pdf",
            "content": "Unsigned without override content.",
            "mime_type": "application/pdf",
        },
        headers=headers,
    )
    assert resp_fail.status_code == 422
    assert "missing required digital signature" in resp_fail.json()["detail"].lower()

    # 5. Verify DB storage, typing, and default unsigned (PENDING) status
    async with db_manager.get_session_maker()() as session:
        stmt = select(TMFDocument).where(TMFDocument.study_id == "study_sig_01").order_by(TMFDocument.artifact_code)
        docs = (await session.execute(stmt)).scalars().all()
        assert len(docs) == 3

        # Protocol Sign-off (01.01.03)
        assert docs[0].document_type == "PROTOCOL_SIGNOFF"
        assert docs[0].approval_status == "PENDING"
        assert docs[0].signer is None
        assert docs[0].signature_manifestation is None

        # FDA Form 1572 (05.02.01)
        assert docs[1].document_type == "FORM_1572"
        assert docs[1].approval_status == "PENDING"

        # Financial Disclosure (05.02.02)
        assert docs[2].document_type == "FINANCIAL_DISCLOSURE"
        assert docs[2].approval_status == "PENDING"


@pytest.mark.asyncio
async def test_signature_lifecycle_with_mock_signature():
    """
    Verify that ingesting a document with a valid mock signature correctly transition its
    approval_status to APPROVED and populates signature metadata fields on the TMFDocument.
    """
    client = TestClient(app)
    headers = get_headers(roles="admin", change_reason="Ingest signed form")

    # Ingest with mock signature in content
    content_with_signature = (
        "This is an approved FDA Form 1572 content.\n"
        "-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE\n-----END CERTIFICATE-----\n"
        "-----BEGIN SIGNATURE-----\nTU9DS19TSUdfREFUQQ==\n-----END SIGNATURE-----"
    )
    resp = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_sig_02",
            "artifact_type": "FORM_1572",
            "filename": "form1572_signed.pdf",
            "content": content_with_signature,
            "mime_type": "application/pdf",
        },
        headers=headers,
    )
    assert resp.status_code == 201
    doc_id = resp.json()["document_id"]

    # Verify fields on the retrieved TMFDocument response
    resp_doc = client.get(f"/api/v1/etmf/documents/{doc_id}", headers=headers)
    assert resp_doc.status_code == 200
    doc_data = resp_doc.json()
    assert doc_data["document_type"] == "FORM_1572"
    assert doc_data["approval_status"] == "APPROVED"
    assert doc_data["signer"] == "Mock Signer"
    assert doc_data["signing_timestamp"] is not None
    assert doc_data["signature_manifestation"] is not None
    assert doc_data["signature_manifestation"]["signer_id"] == "Mock Signer"
    assert doc_data["signature_manifestation"]["signature"] == "TU9DS19TSUdfREFUQQ=="


@pytest.mark.asyncio
async def test_completeness_signature_lifecycle_distinction():
    """
    Verify that check_completeness distinguishes between ABSENT, UNSIGNED, and SIGNED.
    Ensure milestone completeness is blocked if mandatory signed documents are present but UNSIGNED.
    """
    client = TestClient(app)
    admin_headers = get_headers(roles="admin", change_reason="Configure completeness expectations")
    inspector_headers = get_headers(roles="regulatory_inspector")

    study_id = "study_completeness_test"

    # 1. Create expectation for FORM_1572 under INITIATION milestone
    exp_resp = client.post(
        "/api/v1/etmf/edl",
        json={
            "study_id": study_id,
            "milestone": "INITIATION",
            "artifact_type": "FDA Form 1572",
            "reason_for_change": "FDA Form 1572 is required for Initiation",
        },
        headers=admin_headers,
    )
    assert exp_resp.status_code == 201

    # 2. Check completeness before any document is uploaded -> status should be ABSENT
    comp_resp_absent = client.get(
        f"/api/v1/etmf/completeness?study_id={study_id}&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert comp_resp_absent.status_code == 200
    data_absent = comp_resp_absent.json()
    assert data_absent["is_complete"] is False
    assert "FDA Form 1572" in data_absent["missing_artifacts"]
    assert len(data_absent["per_artifact_detail"]) == 1
    assert data_absent["per_artifact_detail"][0]["status"] == "ABSENT"

    # 3. Ingest an UNSIGNED Form 1572 (explicitly bypassed so ingestion succeeds, but approval_status remains PENDING)
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": study_id,
            "artifact_type": "FORM_1572",
            "filename": "form1572_unsigned.pdf",
            "content": "Unsigned investigator qualification document",
            "mime_type": "application/pdf",
            "metadata_json": {"requires_signature": False},
        },
        headers=admin_headers,
    )

    # 4. Check completeness again -> status should be UNSIGNED, and milestone remains incomplete!
    comp_resp_unsigned = client.get(
        f"/api/v1/etmf/completeness?study_id={study_id}&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert comp_resp_unsigned.status_code == 200
    data_unsigned = comp_resp_unsigned.json()
    assert data_unsigned["is_complete"] is False
    assert "FDA Form 1572" in data_unsigned["missing_artifacts"]
    assert data_unsigned["per_artifact_detail"][0]["status"] == "UNSIGNED"

    # 5. Ingest a SIGNED Form 1572 (increments version index to 2)
    signed_content = (
        "Signed investigator qualification document\n"
        "-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE\n-----END CERTIFICATE-----\n"
        "-----BEGIN SIGNATURE-----\nTU9DS19TSUdfREFUQQ==\n-----END SIGNATURE-----"
    )
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": study_id,
            "artifact_type": "FORM_1572",
            "filename": "form1572_signed.pdf",
            "content": signed_content,
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )

    # 6. Check completeness again -> status should be SIGNED, and milestone should be COMPLETE!
    comp_resp_signed = client.get(
        f"/api/v1/etmf/completeness?study_id={study_id}&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert comp_resp_signed.status_code == 200
    data_signed = comp_resp_signed.json()
    assert data_signed["is_complete"] is True
    assert "FDA Form 1572" not in data_signed["missing_artifacts"]
    assert data_signed["per_artifact_detail"][0]["status"] == "SIGNED"
    assert data_signed["per_artifact_detail"][0]["version_index"] == 2
