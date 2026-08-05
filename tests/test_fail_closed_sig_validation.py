"""Unit and integration tests for Production-Gated Fail-Closed Validation.

Covers the following requirements:
- PRD-FAIL-CLOSED-001: Active environment context detection during document ingestion.
- PRD-FAIL-CLOSED-002: Ingestion rejection in production/staging if bypass parameters exist.
- PRD-FAIL-CLOSED-003: Mock signature rejection in production/staging environments.
- PRD-FAIL-CLOSED-004: Standard metadata override and mock signature allowance in non-production environments.
- PRD-FAIL-CLOSED-005: Valid cryptographic signature verification in production.
"""

import base64
import datetime

import pytest
import pytest_asyncio
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from fastapi.testclient import TestClient

from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import Base
from packages.security.cert_store import get_active_cert_store
from tests.test_etmf import get_auth_headers


@pytest.fixture(autouse=True)
def setup_default_env(monkeypatch):
    """Ensure mock signatures are allowed by default in local tests and reset APP_ENV."""
    monkeypatch.setenv("ALLOW_MOCK_SIGNATURES", "1")
    monkeypatch.delenv("APP_ENV", raising=False)


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory eTMF database for signature and routing testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_headers(roles: str = "admin") -> dict:
    """Helper to construct authentication headers with standard justification reason."""
    return get_auth_headers(roles=roles, change_reason="Gated compliance testing")


def generate_test_keys():
    """Generate ephemeral RSA private key and self-signed certificate for testing."""
    private_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
    )
    subject = issuer = x509.Name(
        [
            x509.NameAttribute(x509.NameOID.COMMON_NAME, "test-ca.org"),
        ]
    )
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(private_key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(
            datetime.datetime.now(datetime.UTC) - datetime.timedelta(days=1)
        )
        .not_valid_after(
            datetime.datetime.now(datetime.UTC) + datetime.timedelta(days=365)
        )
        .sign(private_key, hashes.SHA256())
    )
    return private_key, cert


@pytest.mark.asyncio
async def test_production_closed_bypass_rejection(monkeypatch):
    """Verify that document ingestion is rejected in production/staging when bypass/override is requested.

    @req:PRD-FAIL-CLOSED-001
    @req:PRD-FAIL-CLOSED-002
    Requirements: PRD-FAIL-CLOSED-001, PRD-FAIL-CLOSED-002
    """
    client = TestClient(app)
    headers = get_headers()

    # Simulate production environment context
    monkeypatch.setenv("APP_ENV", "production")

    # Ingest a document with bypass/override metadata
    for bypass_meta in [
        {"requires_signature": False},
        {"require_signature": False},
        {"bypass_signatures": "true"},
        {"skip_signature_check": True},
        {"signature_override": "yes"},
    ]:
        resp = client.post(
            "/api/v1/etmf/ingest",
            json={
                "study_id": "study_prod_01",
                "artifact_type": "FORM_1572",
                "filename": "form1572_bypass.pdf",
                "content": "Statement of Investigator Form 1572 content.",
                "mime_type": "application/pdf",
                "metadata_json": bypass_meta,
            },
            headers=headers,
        )
        assert resp.status_code == 422, f"Failed for {bypass_meta}"
        assert (
            "prohibited" in resp.json()["detail"].lower()
            or "bypass" in resp.json()["detail"].lower()
        )


@pytest.mark.asyncio
async def test_production_closed_mock_signature_rejection(monkeypatch):
    """Verify that mock signatures are rejected in production/staging environments.

    @req:PRD-FAIL-CLOSED-001
    @req:PRD-FAIL-CLOSED-003
    Requirements: PRD-FAIL-CLOSED-001, PRD-FAIL-CLOSED-003
    """
    client = TestClient(app)
    headers = get_headers()

    # Simulate staging environment context
    monkeypatch.setenv("APP_ENV", "staging")

    # Content with mock signature block
    content_with_signature = (
        "This is an approved FDA Form 1572 content.\n"
        "-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE\n-----END CERTIFICATE-----\n"
        "-----BEGIN SIGNATURE-----\nTU9DS19TSUdfREFUQQ==\n-----END SIGNATURE-----"
    )

    resp = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_prod_02",
            "artifact_type": "FORM_1572",
            "filename": "form1572_mock.pdf",
            "content": content_with_signature,
            "mime_type": "application/pdf",
        },
        headers=headers,
    )
    assert resp.status_code == 422
    assert "mock signature" in resp.json()["detail"].lower()


@pytest.mark.asyncio
async def test_local_environment_allows_bypass_and_mock(monkeypatch):
    """Verify that local development and test environments allow bypass overrides and mock signatures.

    @req:PRD-FAIL-CLOSED-001
    @req:PRD-FAIL-CLOSED-004
    Requirements: PRD-FAIL-CLOSED-001, PRD-FAIL-CLOSED-004
    """
    client = TestClient(app)
    headers = get_headers()

    # Explicitly set a non-production test environment
    monkeypatch.setenv("APP_ENV", "development")

    # 1. Non-production should allow metadata bypass
    resp_bypass = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_local_01",
            "artifact_type": "FORM_1572",
            "filename": "form1572_bypass.pdf",
            "content": "Unsigned investigator qualification document",
            "mime_type": "application/pdf",
            "metadata_json": {"requires_signature": False},
        },
        headers=headers,
    )
    assert resp_bypass.status_code == 201

    # 2. Non-production should allow mock signature
    content_with_mock = (
        "Signed investigator qualification document\n"
        "-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE\n-----END CERTIFICATE-----\n"
        "-----BEGIN SIGNATURE-----\nTU9DS19TSUdfREFUQQ==\n-----END SIGNATURE-----"
    )
    resp_mock = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_local_01",
            "artifact_type": "FORM_1572",
            "filename": "form1572_mock_signed.pdf",
            "content": content_with_mock,
            "mime_type": "application/pdf",
        },
        headers=headers,
    )
    assert resp_mock.status_code == 201
    doc_id = resp_mock.json()["document_id"]

    # Retrieve details to check approval status
    resp_doc = client.get(f"/api/v1/etmf/documents/{doc_id}", headers=headers)
    assert resp_doc.status_code == 200
    assert resp_doc.json()["approval_status"] == "APPROVED"


@pytest.mark.asyncio
async def test_production_enforces_valid_cryptographic_signatures(monkeypatch):
    """Verify that production allows and correctly validates valid cryptographic signatures.

    @req:PRD-FAIL-CLOSED-001
    @req:PRD-FAIL-CLOSED-005
    Requirements: PRD-FAIL-CLOSED-001, PRD-FAIL-CLOSED-005
    """
    client = TestClient(app)
    headers = get_headers()

    # Simulate production environment context
    monkeypatch.setenv("APP_ENV", "production")

    private_key, cert = generate_test_keys()
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    # Register certificate in the trust store
    store = get_active_cert_store()
    store.register_certificate(user_id="test_user", cert_pem=cert_pem)

    content_data = "Trial records showing investigator eligibility qualification."
    # Sign using PSS padding
    sig_bytes_pss = private_key.sign(
        content_data.encode("utf-8"),
        padding.PSS(
            mgf=padding.MGF1(hashes.SHA256()),
            salt_length=padding.PSS.MAX_LENGTH,
        ),
        hashes.SHA256(),
    )
    sig_b64 = base64.b64encode(sig_bytes_pss).decode("utf-8")

    document_content = (
        f"{content_data}\n"
        f"-----BEGIN CERTIFICATE-----\n{cert_pem.replace('-----BEGIN CERTIFICATE-----', '').replace('-----END CERTIFICATE-----', '').strip()}\n-----END CERTIFICATE-----\n"
        f"-----BEGIN SIGNATURE-----\n{sig_b64}\n-----END SIGNATURE-----"
    )

    # Ingesting the properly cryptographically signed mandatory document in production should succeed
    resp = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_prod_03",
            "artifact_type": "FORM_1572",
            "filename": "form1572_signed.pdf",
            "content": document_content,
            "mime_type": "application/pdf",
        },
        headers=headers,
    )
    assert resp.status_code == 201
    doc_id = resp.json()["document_id"]

    # Retrieve details to check approval status
    resp_doc = client.get(f"/api/v1/etmf/documents/{doc_id}", headers=headers)
    assert resp_doc.status_code == 200
    assert resp_doc.json()["approval_status"] == "APPROVED"
