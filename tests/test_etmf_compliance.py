import asyncio
import base64
import datetime

import pytest
import pytest_asyncio
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.x509.oid import NameOID
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.cryptography import (
    extract_signature_from_content,
    requires_signature,
    validate_document_signature,
)
from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import Base, TMFAuditLedgerSeal, TMFAuditLog
from apps.etmf.sealer import (
    execute_etmf_audit_sealing_cycle,
    start_background_etmf_sealer,
    stop_background_etmf_sealer,
    validate_etmf_ledger_integrity,
)
from apps.execution.trial_lock import TrialLockManager
from tests.test_etmf import get_auth_headers


def generate_self_signed_cert() -> tuple[rsa.RSAPrivateKey, x509.Certificate]:
    """Generates a real RSAPrivateKey and self-signed X.509 Certificate for testing."""
    private_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
    )
    subject = issuer = x509.Name(
        [
            x509.NameAttribute(NameOID.COUNTRY_NAME, "US"),
            x509.NameAttribute(NameOID.STATE_OR_PROVINCE_NAME, "CA"),
            x509.NameAttribute(NameOID.LOCALITY_NAME, "San Francisco"),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Cadence Clinical"),
            x509.NameAttribute(NameOID.COMMON_NAME, "cadence.clinical"),
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
            datetime.datetime.now(datetime.UTC) + datetime.timedelta(days=10)
        )
        .sign(private_key, hashes.SHA256())
    )

    return private_key, cert


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eTMF database for unit and integration testing.
    """
    TrialLockManager.reset()
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_signature_requirement_rules():
    """Verify that is_required functions operate correctly based on artifact type and metadata."""
    assert not requires_signature("Approved Protocol")
    assert requires_signature("Signed Approved Protocol")
    assert requires_signature("Protocol with signature")
    assert requires_signature("Approved Protocol", {"requires_signature": True})
    assert requires_signature("Blank CRF", {"require_signature": True})
    assert not requires_signature("Blank CRF", {"require_signature": False})


@pytest.mark.asyncio
async def test_signature_extraction_formats():
    """Verify signature and cert extraction for both PEM and XML formats."""
    # Test PEM blocks
    pem_content = (
        "Document Text Content here.\n"
        "-----BEGIN CERTIFICATE-----\nMOCK_CERT_DATA\n-----END CERTIFICATE-----\n"
        "-----BEGIN SIGNATURE-----\nTU9DS19TSUdfREFUQQ==\n-----END SIGNATURE-----"
    )
    cert, sig, signed = extract_signature_from_content(pem_content)
    assert (
        cert == "-----BEGIN CERTIFICATE-----\nMOCK_CERT_DATA\n-----END CERTIFICATE-----"
    )
    assert sig == b"MOCK_SIG_DATA"
    assert signed == "Document Text Content here."

    # Test XML tags
    xml_content = (
        "<ClinicalDoc>\n"
        "  <Body>XML Content</Body>\n"
        "  <Signature>\n"
        "    <X509Certificate>MOCK_XML_CERT</X509Certificate>\n"
        "    <SignatureValue>TU9DS19YTUxfU0lH</SignatureValue>\n"
        "  </Signature>\n"
        "</ClinicalDoc>"
    )
    cert, sig, signed = extract_signature_from_content(xml_content)
    assert "-----BEGIN CERTIFICATE-----" in cert
    assert "MOCK_XML_CERT" in cert
    assert sig == b"MOCK_XML_SIG"
    assert "<ClinicalDoc>" in signed
    assert "XML Content" in signed
    assert "</ClinicalDoc>" in signed


@pytest.mark.asyncio
async def test_mock_signature_bypass():
    """Test validator behavior when mock certificates are provided."""
    # Valid mock
    is_valid, msg = validate_document_signature(
        "Approved Protocol",
        "Hello\n-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE\n-----END CERTIFICATE-----\n-----BEGIN SIGNATURE-----\nT09DSw==\n-----END SIGNATURE-----",
    )
    assert is_valid
    assert "mock" in msg.lower()

    # Invalid mock
    is_valid, msg = validate_document_signature(
        "Approved Protocol",
        "Hello\n-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE_INVALID\n-----END CERTIFICATE-----\n-----BEGIN SIGNATURE-----\nT09DSw==\n-----END SIGNATURE-----",
    )
    assert not is_valid
    assert "invalid" in msg.lower()


@pytest.mark.asyncio
async def test_actual_cryptographic_verification():
    """Generate keys, sign data, embed into a document, and verify actual cryptographic verification path."""
    private_key, cert = generate_self_signed_cert()
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    content_data = "This is the clinical trial protocol for study 001. Enforces double blind randomized controls."
    sig_bytes = private_key.sign(
        content_data.encode("utf-8"), padding.PKCS1v15(), hashes.SHA256()
    )
    sig_b64 = base64.b64encode(sig_bytes).decode("utf-8")

    # Construct signed document
    document_content = (
        f"{content_data}\n"
        f"-----BEGIN CERTIFICATE-----\n{cert_pem.replace('-----BEGIN CERTIFICATE-----', '').replace('-----END CERTIFICATE-----', '').strip()}\n-----END CERTIFICATE-----\n"
        f"-----BEGIN SIGNATURE-----\n{sig_b64}\n-----END SIGNATURE-----"
    )

    # Validate signature - requires signature since we set it in metadata
    is_valid, msg = validate_document_signature(
        "Approved Protocol", document_content, {"requires_signature": True}
    )
    assert is_valid, f"Signature failed to verify: {msg}"
    assert "successfully verified" in msg.lower()

    # Mismatched/corrupted content validation failure
    mismatched_content = document_content.replace("double blind", "open label")
    is_valid, msg = validate_document_signature(
        "Approved Protocol", mismatched_content, {"requires_signature": True}
    )
    assert not is_valid
    assert "failed" in msg.lower()


@pytest.mark.asyncio
async def test_missing_and_invalid_signature_ingestion():
    """Verify that ingestion fails with 422 for missing required signature and invalid signature."""
    client = TestClient(app)
    headers = get_auth_headers(change_reason="compliance test")

    # 1. Missing required signature
    payload_missing = {
        "study_id": "study_123",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol body without signature.",
        "mime_type": "application/pdf",
        "metadata_json": {"requires_signature": True},
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload_missing, headers=headers)
    assert resp.status_code == 422
    assert "missing required digital signature" in resp.json()["detail"].lower()

    # 2. Invalid/corrupted signature (valid base64 that decodes to 'INVALID')
    payload_invalid = {
        "study_id": "study_123",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol body.\n-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE\n-----END CERTIFICATE-----\n-----BEGIN SIGNATURE-----\nSU5WQUxJRA==\n-----END SIGNATURE-----",
        "mime_type": "application/pdf",
        "metadata_json": {"requires_signature": True},
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload_invalid, headers=headers)
    assert resp.status_code == 422
    assert "invalid mock digital signature" in resp.json()["detail"].lower()


@pytest.mark.asyncio
async def test_audit_logs_group_sealing_and_chaining():
    """Verify compiling, hashing, and sealing of unsealed eTMF audit logs into a Merkle-tree block structure."""
    client = TestClient(app)
    headers = get_auth_headers(change_reason="audit logs test")

    # Ingest a non-signed document to generate audit logs
    payload = {
        "study_id": "study_xyz",
        "artifact_type": "Define-XML Specifications",
        "filename": "define.xml",
        "content": "Define content",
        "mime_type": "application/xml",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert resp.status_code == 201

    # Retrieve logs to make sure we have them
    resp_logs = client.get("/api/v1/etmf/audit-logs", headers=headers)
    assert resp_logs.status_code == 200
    logs = resp_logs.json()
    assert len(logs) >= 2  # INGEST and AUDIT_VIEW

    async with db_manager.get_session_maker()() as session:
        # Check that cryptographic seal is initially null
        stmt_unsealed = select(TMFAuditLog).where(
            TMFAuditLog.cryptographic_seal.is_(None)
        )
        unsealed_count = len((await session.execute(stmt_unsealed)).scalars().all())
        assert unsealed_count > 0

        # Execute audit sealing cycle
        new_block_hash = await execute_etmf_audit_sealing_cycle(session)
        assert new_block_hash is not None
        assert len(new_block_hash) == 64

        # Verify a ledger seal record was inserted
        stmt_seals = select(TMFAuditLedgerSeal).order_by(
            TMFAuditLedgerSeal.block_index.desc()
        )
        seal = (await session.execute(stmt_seals)).scalars().first()
        assert seal is not None
        assert seal.current_block_hash == new_block_hash
        assert seal.previous_block_hash == "0" * 64
        assert seal.sealed_record_count == unsealed_count

        # Check that logs have the cryptographic_seal applied
        stmt_after_sealed = select(TMFAuditLog).where(
            TMFAuditLog.cryptographic_seal == new_block_hash
        )
        sealed_logs = (await session.execute(stmt_after_sealed)).scalars().all()
        assert len(sealed_logs) == unsealed_count

        # Verify that running sealer again with no new records returns None
        assert (await execute_etmf_audit_sealing_cycle(session)) is None


@pytest.mark.asyncio
async def test_tampering_detection_and_lockout_propagation():
    """Verify that manual database modification of a sealed audit log triggers a block verification failure, lock, and read-only state."""
    client = TestClient(app)
    headers = get_auth_headers(change_reason="tampering test")

    # Ingest document and seal it
    resp = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_111",
            "artifact_type": "Define-XML Specifications",
            "filename": "define.xml",
            "content": "Define content",
            "mime_type": "application/xml",
        },
        headers=headers,
    )
    assert resp.status_code == 201

    session_maker = db_manager.get_session_maker()

    # Seal the logs
    async with session_maker() as session:
        block_hash = await execute_etmf_audit_sealing_cycle(session)
        assert block_hash is not None

        # Verify initial chain integrity
        assert await validate_etmf_ledger_integrity(session)

    # Let's manually tamper with one of the sealed audit log rows directly in DB!
    async with session_maker() as session:
        # Fetch the sealed log
        stmt = select(TMFAuditLog).where(TMFAuditLog.cryptographic_seal == block_hash)
        log_to_tamper = (await session.execute(stmt)).scalars().first()
        assert log_to_tamper is not None

        # Modify details
        log_to_tamper.details = (
            "TAMPERED: Changing the immutable log action description."
        )
        await session.commit()

    # Verify that ledger integrity validation now fails, raises ValueError, and locks the trial!
    assert not TrialLockManager.is_locked()

    async with session_maker() as session:
        with pytest.raises(ValueError, match="eTMF GxP Data Integrity Breach"):
            await validate_etmf_ledger_integrity(session)

    # Trial must be locked now!
    assert TrialLockManager.is_locked()

    # Subsequent ingest attempts must return 403 Forbidden because trial is locked!
    resp_ingest_fail = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_111",
            "artifact_type": "Define-XML Specifications",
            "filename": "define2.xml",
            "content": "More content",
            "mime_type": "application/xml",
        },
        headers=headers,
    )
    assert resp_ingest_fail.status_code == 403
    assert "locked" in resp_ingest_fail.json()["detail"].lower()


@pytest.mark.asyncio
async def test_background_sealer_lifecycle():
    """Verify background sealer thread starts and stops gracefully without error."""
    session_maker = db_manager.get_session_maker()
    await start_background_etmf_sealer(session_maker, interval=0.1)
    await asyncio.sleep(0.3)
    await stop_background_etmf_sealer()
