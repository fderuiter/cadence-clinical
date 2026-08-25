"""Comprehensive Unit & Integration Test Suite for Secure AS2 B2B Gateway & E2B(R3) MDN Parser.

Requirements: PRD-PVG-002, ADR-2199
"""

import uuid

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select, text

from apps.safety.database import db_manager
from apps.safety.domain.as2_models import (
    AckStatus,
    AckType,
    AS2Authority,
    AS2OutboundTransmissionInput,
    AS2PartnerConfig,
    EncryptionAlgorithm,
    MDNDispositionType,
    SignatureAlgorithm,
    TransmissionStatus,
)
from apps.safety.main import app
from apps.safety.models import (
    AS2Transmission,
    AS2TransmissionAuditLog,
    Base,
    write_as2_audit_log,
)
from apps.safety.services.as2_gateway_client import (
    AS2GatewayClient,
)
from apps.safety.services.crypto_smime import (
    calculate_mic,
    decrypt_payload_smime,
    encrypt_payload_smime,
    generate_as2_test_keypair,
    sign_payload_smime,
    verify_signature_smime,
)
from apps.safety.services.mdn_parser import MDNParser
from apps.safety.services.regulatory_ack_parser import RegulatoryAckParser
from packages.testing.security import create_test_auth_headers

pytestmark = pytest.mark.xdist_group("safety_as2_gateway")


@pytest_asyncio.fixture(autouse=True)
async def setup_as2_db():
    """Setup isolated in-memory database for AS2 B2B Gateway tests."""
    db_uri = f"sqlite+aiosqlite:///file:memdb_as2_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
    db_manager.init_db(db_uri, echo=False)

    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def get_auth_headers(
    roles: str = "sponsor_safety_physician",
    user_id: str = "dr_safety_officer_01",
    change_reason: str = "AS2 B2B Regulatory Transmission Operation",
) -> dict[str, str]:
    """Helper to generate internal Gateway HMAC authentication headers."""
    roles_list = [r.strip() for r in roles.split(",") if r.strip()]
    return create_test_auth_headers(
        user_id=user_id,
        roles=roles_list,
        change_reason=change_reason,
    )


SAMPLE_E2B_XML = """<?xml version="1.0" encoding="utf-8"?>
<ichicsr xmlns="urn:hl7-org:v3">
    <header>
        <message_id>MSG-20260822-001</message_id>
        <sender_organization>CADENCE_SPONSOR</sender_organization>
        <receiver_organization>FDA</receiver_organization>
        <transmission_date>2026-08-22T20:00:00Z</transmission_date>
        <message_type>ICHICSR</message_type>
    </header>
    <safety_report>
        <worldwide_unique_case_id>US-CADENCE-2026-00099</worldwide_unique_case_id>
    </safety_report>
    <patient>
        <patient_id>SUBJ-9001</patient_id>
        <sex>female</sex>
    </patient>
    <reactions>
        <reaction>
            <reaction_term>Anaphylaxis</reaction_term>
        </reaction>
    </reactions>
    <suspect_drugs>
        <suspect_drug>
            <drug_name>Cadencium Investigational Product</drug_name>
            <drug_role>SUSPECT</drug_role>
        </suspect_drug>
    </suspect_drugs>
</ichicsr>"""


# =========================================================================
# Unit Tests: S/MIME AES-256 Encryption, SHA-256 Digital Signatures & MIC
# =========================================================================


def test_calculate_mic_canonical_formatting():
    """Verify AS2 Message Integrity Check (MIC) computation and canonical RFC formatting.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    payload = "Hello World\nLine 2\n"
    mic_256 = calculate_mic(payload, algorithm="sha-256")

    assert ", sha-256" in mic_256
    b64_hash = mic_256.split(",")[0].strip()
    assert len(b64_hash) > 20

    # Ensure CRLF line canonicalization produces identical MIC regardless of input line endings
    payload_crlf = "Hello World\r\nLine 2\r\n"
    assert calculate_mic(payload, algorithm="sha-256") == calculate_mic(
        payload_crlf, algorithm="sha-256"
    )


def test_smime_sha256_digital_signature_and_verification():
    """Verify S/MIME SHA-256 RSA digital signing and tamper detection.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    cert_pem, key_pem = generate_as2_test_keypair(common_name="Sponsor Signer")

    signed_mime_bytes, headers = sign_payload_smime(
        payload=SAMPLE_E2B_XML,
        private_key=key_pem,
        certificate=cert_pem,
    )

    assert b"multipart/signed" in headers["Content-Type"].encode("utf-8")
    assert b"smime.p7s" in signed_mime_bytes

    # 1. Successful verification against original cert
    is_valid, extracted, err = verify_signature_smime(
        signed_mime_bytes, trusted_cert=cert_pem
    )
    assert is_valid is True
    assert err is None
    assert b"US-CADENCE-2026-00099" in extracted

    # 2. Tampered content detection
    tampered_bytes = signed_mime_bytes.replace(
        b"US-CADENCE-2026-00099", b"US-FORGED-2026-99999"
    )
    is_tampered_valid, _, err_tampered = verify_signature_smime(
        tampered_bytes, trusted_cert=cert_pem
    )
    assert is_tampered_valid is False
    assert "verification failed" in (err_tampered or "").lower()

    # 3. Verification with untrusted wrong cert
    wrong_cert, _ = generate_as2_test_keypair(common_name="Attacker Cert")
    is_wrong_valid, _, err_wrong = verify_signature_smime(
        signed_mime_bytes, trusted_cert=wrong_cert
    )
    assert is_wrong_valid is False


def test_smime_aes256_hybrid_encryption_and_decryption():
    """Verify S/MIME AES-256 payload encryption with recipient RSA certificate and private key decryption.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    recipient_cert, recipient_key = generate_as2_test_keypair(
        common_name="FDA ESG Gateway"
    )

    # Encrypt S/MIME payload
    encrypted_bytes, headers = encrypt_payload_smime(
        payload=SAMPLE_E2B_XML,
        recipient_certificate=recipient_cert,
        algorithm="AES-256-GCM",
    )

    assert "application/pkcs7-mime" in headers["Content-Type"]
    assert (
        b"US-CADENCE-2026-00099" not in encrypted_bytes
    )  # Ciphertext must not reveal plaintext

    # Decrypt with recipient's private key
    decrypted_bytes = decrypt_payload_smime(
        encrypted_mime_data=encrypted_bytes,
        recipient_private_key=recipient_key,
    )
    assert decrypted_bytes.decode("utf-8") == SAMPLE_E2B_XML

    # Decryption with wrong private key must fail securely
    _, wrong_key = generate_as2_test_keypair(common_name="Unauthorized Key")
    with pytest.raises(ValueError, match="Failed to decrypt"):
        decrypt_payload_smime(
            encrypted_mime_data=encrypted_bytes,
            recipient_private_key=wrong_key,
        )


def test_smime_full_pipeline_sign_then_encrypt():
    """Verify full S/MIME B2B packaging pipeline: Sign -> Encrypt -> Decrypt -> Verify Signature.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    sender_cert, sender_key = generate_as2_test_keypair(
        common_name="Cadence Sponsor Gateway"
    )
    fda_cert, fda_key = generate_as2_test_keypair(
        common_name="FDA ESG Regulatory Gateway"
    )

    # 1. Sign
    signed_bytes, _ = sign_payload_smime(
        payload=SAMPLE_E2B_XML,
        private_key=sender_key,
        certificate=sender_cert,
    )

    # 2. Encrypt
    enc_bytes, _ = encrypt_payload_smime(
        payload=signed_bytes,
        recipient_certificate=fda_cert,
    )

    # 3. Decrypt by FDA ESG
    decrypted_signed_bytes = decrypt_payload_smime(
        encrypted_mime_data=enc_bytes,
        recipient_private_key=fda_key,
    )

    # 4. Verify Signature by FDA ESG
    is_valid, extracted_xml, err = verify_signature_smime(
        signed_mime_data=decrypted_signed_bytes,
        trusted_cert=sender_cert,
    )
    assert is_valid is True
    assert err is None
    assert b"US-CADENCE-2026-00099" in extracted_xml


# =========================================================================
# Unit Tests: MDN (Message Disposition Notification) Parser & Verifier
# =========================================================================


def test_mdn_parser_valid_synchronous_receipt():
    """Verify automated parsing and MIC verification of a valid synchronous MDN.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    orig_msg_id = "<20260822-MSG001@cadence.clinical>"
    expected_mic = calculate_mic(SAMPLE_E2B_XML)

    raw_mdn_bytes, _ = MDNParser.generate_mock_mdn(
        original_message_id=orig_msg_id,
        received_mic=expected_mic,
        sender_as2_id="CADENCE-SPONSOR",
        receiver_as2_id="FDA-ESG",
        disposition_type=MDNDispositionType.PROCESSED,
    )

    result = MDNParser.parse_raw_mdn(
        mdn_raw=raw_mdn_bytes,
        expected_message_id=orig_msg_id,
        expected_mic=expected_mic,
    )

    assert result.is_valid_mdn is True
    assert result.disposition_type == MDNDispositionType.PROCESSED
    assert result.mic_matches is True
    assert result.original_message_id == "20260822-MSG001@cadence.clinical"
    assert "FDA-ESG" in (result.reporting_ua or "")


def test_mdn_parser_mic_mismatch_detection():
    """Verify that an MDN with a mismatched MIC is rejected for payload corruption / tampering.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    orig_msg_id = "<20260822-MSG002@cadence.clinical>"
    correct_mic = calculate_mic(SAMPLE_E2B_XML)
    corrupted_mic = "CorruptedDigestValue123456789==, sha-256"

    raw_mdn_bytes, _ = MDNParser.generate_mock_mdn(
        original_message_id=orig_msg_id,
        received_mic=corrupted_mic,
    )

    result = MDNParser.parse_raw_mdn(
        mdn_raw=raw_mdn_bytes,
        expected_message_id=orig_msg_id,
        expected_mic=correct_mic,
    )

    assert result.is_valid_mdn is False
    assert result.mic_matches is False
    assert "MIC does not match" in (result.error_details or "")


def test_mdn_parser_signed_mdn_verification():
    """Verify parsing and cryptographic signature verification of signed MDN receipts.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    fda_cert, fda_key = generate_as2_test_keypair(common_name="FDA ESG MDN Signer")
    orig_msg_id = "<20260822-MSG003@cadence.clinical>"
    mic = calculate_mic(SAMPLE_E2B_XML)

    signed_mdn_bytes, _ = MDNParser.generate_mock_mdn(
        original_message_id=orig_msg_id,
        received_mic=mic,
        signing_key_pem=fda_key,
        signing_cert_pem=fda_cert,
    )

    result = MDNParser.parse_raw_mdn(
        mdn_raw=signed_mdn_bytes,
        expected_message_id=orig_msg_id,
        expected_mic=mic,
        authority_cert=fda_cert,
    )

    assert result.is_signed is True
    assert result.signature_valid is True
    assert result.is_valid_mdn is True
    assert result.disposition_type == MDNDispositionType.PROCESSED


def test_mdn_parser_disposition_failure_handling():
    """Verify handling of MDN receipts indicating gateway-level processing errors.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    orig_msg_id = "<20260822-MSG004@cadence.clinical>"
    mic = calculate_mic(SAMPLE_E2B_XML)

    raw_mdn_bytes, _ = MDNParser.generate_mock_mdn(
        original_message_id=orig_msg_id,
        received_mic=mic,
        disposition_type=MDNDispositionType.FAILED,
        error_message="Decompression error: corrupted gzip container",
    )

    result = MDNParser.parse_raw_mdn(
        mdn_raw=raw_mdn_bytes,
        expected_message_id=orig_msg_id,
        expected_mic=mic,
    )

    assert result.disposition_type == MDNDispositionType.FAILED
    assert "Decompression error" in (result.error_details or "")


# =========================================================================
# Unit Tests: Regulatory ACK Parser & Validator (ACK A, ACK B, ACK C)
# =========================================================================


def test_ack_b_syntactic_validation_success():
    """Verify parsing and validation of successful ICH E2B(R3) Syntactic Acknowledgement (ACK B).

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    msg_id = "MSG-20260822-001"
    case_id = "US-CADENCE-2026-00099"
    ack_xml = RegulatoryAckParser.generate_mock_ack_b(
        message_id=msg_id,
        worldwide_unique_case_id=case_id,
        is_accepted=True,
    )

    result = RegulatoryAckParser.parse_ack_b(ack_xml)

    assert result.ack_type == AckType.ACK_B
    assert result.is_accepted is True
    assert result.ack_status == AckStatus.ACCEPTED
    assert result.worldwide_unique_case_id == case_id
    assert len(result.errors) == 0


def test_ack_b_syntactic_validation_failure():
    """Verify parsing and error extraction of failed ICH E2B(R3) Syntactic Acknowledgement (ACK B).

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    msg_id = "MSG-20260822-002"
    case_id = "US-CADENCE-2026-00099"
    ack_xml = RegulatoryAckParser.generate_mock_ack_b(
        message_id=msg_id,
        worldwide_unique_case_id=case_id,
        is_accepted=False,
        error_message="Mandatory element 'reaction_term' is empty at XPath //reaction[1]",
    )

    result = RegulatoryAckParser.parse_ack_b(ack_xml)

    assert result.ack_type == AckType.ACK_B
    assert result.is_accepted is False
    assert result.ack_status == AckStatus.SYNTAX_ERROR
    assert len(result.errors) > 0
    assert "reaction_term" in result.errors[0]


def test_ack_c_authority_center_acceptance():
    """Verify parsing of Regulatory Health Authority Center Acceptance / DB Ingestion (ACK C).

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    msg_id = "MSG-20260822-003"
    case_id = "US-CADENCE-2026-00099"
    ctn = "CTN-2026-FDA-998877"

    ack_xml = RegulatoryAckParser.generate_mock_ack_c(
        message_id=msg_id,
        worldwide_unique_case_id=case_id,
        is_accepted=True,
        authority_tracking_number=ctn,
        warning_message="MedDRA LLT term auto-coded to PT version 27.0",
    )

    result = RegulatoryAckParser.parse_ack_c(ack_xml)

    assert result.ack_type == AckType.ACK_C
    assert result.is_accepted is True
    assert result.ack_status == AckStatus.ACCEPTED_WITH_WARNINGS
    assert result.authority_receipt_id == ctn
    assert result.worldwide_unique_case_id == case_id
    assert len(result.warnings) > 0
    assert "MedDRA" in result.warnings[0]


def test_ack_c_authority_center_rejection():
    """Verify parsing of Authority Center Ingestion Rejection (ACK C).

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    msg_id = "MSG-20260822-004"
    case_id = "US-CADENCE-2026-00099"

    ack_xml = RegulatoryAckParser.generate_mock_ack_c(
        message_id=msg_id,
        worldwide_unique_case_id=case_id,
        is_accepted=False,
        error_message="Duplicate ICSR: Case ID already exists in FAERS database",
    )

    result = RegulatoryAckParser.parse_ack_c(ack_xml)

    assert result.ack_type == AckType.ACK_C
    assert result.is_accepted is False
    assert result.ack_status == AckStatus.REJECTED
    assert len(result.errors) > 0
    assert "Duplicate ICSR" in result.errors[0]


# =========================================================================
# Integration Tests: AS2 Gateway Client & Append-Only Transmission Ledger
# =========================================================================


@pytest.mark.asyncio
async def test_as2_client_outbound_transmission_with_sync_mdn():
    """Verify full end-to-end outbound transmission, S/MIME encryption, and sync MDN processing in ledger.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    fda_cert, fda_key = generate_as2_test_keypair(common_name="FDA ESG Mock Server")

    # Mock HTTP client returning valid synchronous MDN
    async def mock_handler(request: httpx.Request) -> httpx.Response:
        msg_id_hdr = request.headers.get("Message-ID", "<test@cadence>")

        # Decrypt payload as FDA ESG
        decrypted = decrypt_payload_smime(
            request.content, recipient_private_key=fda_key
        )
        # Verify signature
        is_valid, extracted_xml, _ = verify_signature_smime(decrypted)
        assert is_valid is True

        mic = calculate_mic(extracted_xml.decode("utf-8"))
        mdn_bytes, mdn_headers = MDNParser.generate_mock_mdn(
            original_message_id=msg_id_hdr,
            received_mic=mic,
            receiver_as2_id="FDA-ESG",
            sender_as2_id="CADENCE-SPONSOR",
        )
        return httpx.Response(200, content=mdn_bytes, headers=mdn_headers)

    transport = httpx.MockTransport(mock_handler)
    mock_http_client = httpx.AsyncClient(transport=transport)

    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        client = AS2GatewayClient(
            session=session,
            http_client=mock_http_client,
        )
        # Set FDA partner cert to test cert
        client.partner_configs[AS2Authority.FDA_ESG] = AS2PartnerConfig(
            as2_id="FDA-ESG",
            url="https://esg.fda.gov/as2",
            encryption_certificate_pem=fda_cert,
            signing_certificate_pem=fda_cert,
            request_signed_mdn=True,
            encryption_algorithm=EncryptionAlgorithm.AES_256_GCM,
            signature_algorithm=SignatureAlgorithm.SHA_256_RSA,
        )

        input_data = AS2OutboundTransmissionInput(
            case_id="ICSR-2026-0001",
            study_id="STUDY-ONCO-101",
            icsr_xml_payload=SAMPLE_E2B_XML,
            target_authority=AS2Authority.FDA_ESG,
        )

        result = await client.send_e2b_payload(
            input_data=input_data,
            user_id="dr_safety_officer_01",
            change_reason="Outbound SUSAR Submission to FDA ESG",
        )

        assert result.status == TransmissionStatus.ACK_A_RECEIVED
        assert result.mdn_parsed is not None
        assert result.mdn_parsed.is_valid_mdn is True
        assert result.mdn_parsed.mic_matches is True

        # Verify DB Ledger Record
        stmt = select(AS2Transmission).where(
            AS2Transmission.transmission_id == result.transmission_id
        )
        res = await session.execute(stmt)
        tx = res.scalars().first()
        assert tx is not None
        assert tx.status == TransmissionStatus.ACK_A_RECEIVED.value
        assert tx.ack_a_status == AckStatus.ACCEPTED.value
        assert tx.worldwide_unique_case_id == "US-CADENCE-2026-00099"
        assert tx.created_by == "dr_safety_officer_01"

        # Verify Audit Log Events
        audit_stmt = select(AS2TransmissionAuditLog).where(
            AS2TransmissionAuditLog.transmission_id == tx.id
        )
        audit_res = await session.execute(audit_stmt)
        logs = audit_res.scalars().all()
        assert len(logs) >= 3
        actions = [log.action for log in logs]
        assert "AS2_TRANSMISSION_INITIATED" in actions
        assert "AS2_PAYLOAD_DISPATCHED" in actions
        assert "MDN_VERIFIED_ACK_A" in actions


@pytest.mark.asyncio
async def test_as2_client_inbound_ack_b_and_ack_c_lifecycle():
    """Verify progressive multi-tier lifecycle transitions: ACK A -> ACK B -> ACK C.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        # Seed transmission record
        tx = AS2Transmission(
            transmission_id="TX-TEST-LIFECYCLE-001",
            case_id="ICSR-2026-0002",
            study_id="STUDY-ONCO-101",
            worldwide_unique_case_id="US-CADENCE-2026-00099",
            message_id="<20260822-MSG999@cadence.clinical>",
            as2_from="CADENCE-SPONSOR",
            as2_to="FDA-ESG",
            target_authority=AS2Authority.FDA_ESG.value,
            environment="PRODUCTION",
            status=TransmissionStatus.ACK_A_RECEIVED.value,
            payload_digest_sha256="abc123",
            mic="mic123, sha-256",
            encryption_algorithm="AES-256-GCM",
            signature_algorithm="SHA-256-RSA",
            outbound_payload_size=1024,
            outbound_xml=SAMPLE_E2B_XML,
            ack_a_status=AckStatus.ACCEPTED.value,
            created_by="system",
            reason_for_change="Seed transmission for lifecycle test",
            version_index=1,
        )
        session.add(tx)
        await session.flush()

        client = AS2GatewayClient(session=session)

        # 1. Process ACK B (Technical Validation)
        ack_b_xml = RegulatoryAckParser.generate_mock_ack_b(
            message_id=tx.message_id,
            worldwide_unique_case_id=tx.worldwide_unique_case_id,
            is_accepted=True,
        )
        ack_b_res = await client.process_inbound_ack(
            ack_payload=ack_b_xml,
            ack_type=AckType.ACK_B,
            user_id="gateway_worker",
            change_reason="Process Inbound FDA Core 2 ACK",
        )
        assert ack_b_res.is_accepted is True
        assert tx.status == TransmissionStatus.ACK_B_RECEIVED.value
        assert tx.ack_b_status == AckStatus.ACCEPTED.value

        # 2. Process ACK C (Authority Business Acceptance)
        ack_c_xml = RegulatoryAckParser.generate_mock_ack_c(
            message_id=tx.message_id,
            worldwide_unique_case_id=tx.worldwide_unique_case_id,
            is_accepted=True,
            authority_tracking_number="CTN-FDA-2026-554433",
        )
        ack_c_res = await client.process_inbound_ack(
            ack_payload=ack_c_xml,
            ack_type=AckType.ACK_C,
            user_id="gateway_worker",
            change_reason="Process Inbound FDA Center Acceptance",
        )
        assert ack_c_res.is_accepted is True
        assert tx.status == TransmissionStatus.ACK_C_RECEIVED.value
        assert tx.ack_c_status == AckStatus.ACCEPTED.value
        assert tx.ack_c_details["receipt_id"] == "CTN-FDA-2026-554433"


@pytest.mark.asyncio
async def test_as2_transmission_audit_log_immutability_invariant():
    """Verify that 21 CFR Part 11 immutability event listeners forbid updates/deletes to AS2TransmissionAuditLog.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        # Create a transmission and an audit log entry
        tx = AS2Transmission(
            transmission_id="TX-IMMUTABLE-001",
            message_id="<msg-imm@cadence>",
            as2_from="SENDER",
            as2_to="RECEIVER",
            target_authority="FDA_ESG",
            status="TRANSMITTED",
            payload_digest_sha256="digest",
            mic="mic, sha-256",
            encryption_algorithm="AES-256-GCM",
            signature_algorithm="SHA-256-RSA",
            outbound_payload_size=500,
            created_by="user_01",
            reason_for_change="Immutable test",
        )
        session.add(tx)
        await session.flush()

        log_entry = await write_as2_audit_log(
            session=session,
            transmission_id=tx.id,
            action="INITIAL_LOG",
            details="Original details",
            created_by="user_01",
        )
        await session.commit()

        # 1. Attempt to UPDATE the audit log record -> MUST raise ValueError
        log_entry.details = "Mutated details"
        with pytest.raises(
            ValueError,
            match="strictly forbidden",
        ):
            await session.flush()

        await session.rollback()

        # 2. Attempt to DELETE the audit log record -> MUST raise ValueError
        session.add(log_entry)
        await session.delete(log_entry)
        with pytest.raises(
            ValueError,
            match="strictly forbidden",
        ):
            await session.flush()


# =========================================================================
# Integration Tests: FastAPI REST Endpoints
# =========================================================================


def test_rest_send_as2_transmission_endpoint():
    """Verify REST API endpoint POST /api/v1/safety/as2/transmissions.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Dispatch expedited SUSAR via REST API")

    payload = {
        "case_id": "CASE-REST-001",
        "study_id": "STUDY-REST-001",
        "icsr_xml_payload": SAMPLE_E2B_XML,
        "target_authority": "FDA_ESG",
        "sender_as2_id": "CADENCE-SPONSOR",
        "receiver_as2_id": "FDA-ESG",
        "environment": "PRODUCTION",
    }

    response = client.post(
        "/api/v1/safety/as2/transmissions", json=payload, headers=headers
    )
    assert response.status_code == 201
    data = response.json()
    assert data["transmission_id"].startswith("TX-")
    assert data["worldwide_unique_case_id"] == "US-CADENCE-2026-00099"
    assert data["target_authority"] == "FDA_ESG"
    assert len(data["audit_events"]) > 0


def test_rest_list_and_get_as2_transmissions():
    """Verify REST API endpoints GET /api/v1/safety/as2/transmissions and GET /transmissions/{id}.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    client = TestClient(app)
    headers = get_auth_headers()

    # 1. Create transmission
    payload = {
        "case_id": "CASE-LIST-001",
        "study_id": "STUDY-100",
        "icsr_xml_payload": SAMPLE_E2B_XML,
        "target_authority": "EMA_GATEWAY",
    }
    create_resp = client.post(
        "/api/v1/safety/as2/transmissions", json=payload, headers=headers
    )
    assert create_resp.status_code == 201
    tx_id = create_resp.json()["transmission_id"]

    # 2. List transmissions
    list_resp = client.get(
        "/api/v1/safety/as2/transmissions?target_authority=EMA_GATEWAY", headers=headers
    )
    assert list_resp.status_code == 200
    items = list_resp.json()
    assert len(items) >= 1
    assert any(i["transmission_id"] == tx_id for i in items)

    # 3. Get individual transmission by ID
    get_resp = client.get(f"/api/v1/safety/as2/transmissions/{tx_id}", headers=headers)
    assert get_resp.status_code == 200
    assert get_resp.json()["transmission_id"] == tx_id


def test_rest_process_inbound_mdn_endpoint():
    """Verify REST API endpoint POST /api/v1/safety/as2/mdn for async callbacks.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    client = TestClient(app)
    headers = get_auth_headers()

    # Create transmission first
    create_resp = client.post(
        "/api/v1/safety/as2/transmissions",
        json={
            "case_id": "CASE-MDN-001",
            "icsr_xml_payload": SAMPLE_E2B_XML,
            "target_authority": "PMDA",
        },
        headers=headers,
    )
    assert create_resp.status_code == 201
    msg_id = create_resp.json()["message_id"]
    mic = create_resp.json()["mic"]

    # Generate mock MDN
    raw_mdn_bytes, _ = MDNParser.generate_mock_mdn(
        original_message_id=msg_id,
        received_mic=mic,
        receiver_as2_id="PMDA-GATEWAY",
    )

    # Submit MDN via REST endpoint
    mdn_resp = client.post(
        "/api/v1/safety/as2/mdn",
        json={"mdn_payload": raw_mdn_bytes.decode("utf-8")},
        headers=headers,
    )
    assert mdn_resp.status_code == 200
    data = mdn_resp.json()
    assert data["is_valid_mdn"] is True
    assert data["mic_matches"] is True
    assert data["disposition_type"] == "PROCESSED"


def test_rest_process_regulatory_ack_endpoint():
    """Verify REST API endpoint POST /api/v1/safety/as2/ack for ACK B & ACK C payloads.

    @req:PRD-PVG-002
    @req:ADR-2199
    @req:PRD-SYS-168
    """
    client = TestClient(app)
    headers = get_auth_headers()

    # Create transmission
    create_resp = client.post(
        "/api/v1/safety/as2/transmissions",
        json={
            "case_id": "CASE-ACK-001",
            "icsr_xml_payload": SAMPLE_E2B_XML,
            "target_authority": "MHRA",
        },
        headers=headers,
    )
    assert create_resp.status_code == 201
    tx_id = create_resp.json()["transmission_id"]
    msg_id = create_resp.json()["message_id"]

    # 1. Post ACK B
    ack_b_xml = RegulatoryAckParser.generate_mock_ack_b(
        message_id=msg_id,
        worldwide_unique_case_id="US-CADENCE-2026-00099",
        is_accepted=True,
    )
    ack_b_resp = client.post(
        "/api/v1/safety/as2/ack",
        json={
            "ack_payload": ack_b_xml,
            "ack_type": "ACK_B",
            "transmission_id": tx_id,
        },
        headers=headers,
    )
    assert ack_b_resp.status_code == 200
    assert ack_b_resp.json()["is_accepted"] is True
    assert ack_b_resp.json()["ack_status"] == "ACCEPTED"

    # 2. Post ACK C
    ack_c_xml = RegulatoryAckParser.generate_mock_ack_c(
        message_id=msg_id,
        worldwide_unique_case_id="US-CADENCE-2026-00099",
        is_accepted=True,
        authority_tracking_number="MHRA-CTN-8877",
    )
    ack_c_resp = client.post(
        "/api/v1/safety/as2/ack",
        json={
            "ack_payload": ack_c_xml,
            "ack_type": "ACK_C",
            "transmission_id": tx_id,
        },
        headers=headers,
    )
    assert ack_c_resp.status_code == 200
    assert ack_c_resp.json()["is_accepted"] is True
    assert ack_c_resp.json()["authority_receipt_id"] == "MHRA-CTN-8877"
