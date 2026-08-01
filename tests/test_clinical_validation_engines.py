"""
Tests for Unified Structured Clinical Validation Engines.
Covers asymmetric signature verification, signature parsing, stripped-block validations,
deterministic date-shifting, exact numeric age capping, and study pseudonym prefixes.
"""

import base64
import hashlib
import json

import pytest
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ec, rsa

from packages.deid.transforms import (
    cap_age_numeric,
    get_subject_date_shift,
    pseudonymize_subject_id,
    shift_date_by_subject,
)
from packages.security.crypto_verifier import (
    SignatureVerificationRequest,
    parse_signature_format,
    strip_and_canonicalize_json,
    strip_xml_signatures,
    verify_electronic_signature,
)
from packages.security.signing import asymmetric_sign


@pytest.fixture
def rsa_keys():
    """Generates RSA key pair PEM strings."""
    private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    private_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")
    public_pem = (
        private_key.public_key()
        .public_bytes(
            encoding=serialization.Encoding.PEM,
            format=serialization.PublicFormat.SubjectPublicKeyInfo,
        )
        .decode("utf-8")
    )
    return private_pem, public_pem


@pytest.fixture
def ec_keys():
    """Generates ECDSA key pair PEM strings."""
    private_key = ec.generate_private_key(ec.SECP256R1())
    private_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")
    public_pem = (
        private_key.public_key()
        .public_bytes(
            encoding=serialization.Encoding.PEM,
            format=serialization.PublicFormat.SubjectPublicKeyInfo,
        )
        .decode("utf-8")
    )
    return private_pem, public_pem


# ==============================================================================
# 1. UNIFIED ASYMMETRIC SIGNATURE ENGINE TESTS
# ==============================================================================


def test_unified_api_rsa_verification(rsa_keys):
    """Verify that unified API successfully validates RSA signatures.

    Requirements: PRD-SYS-001
    """
    private_pem, public_pem = rsa_keys
    payload = b"clinical-trial-audit-log-001"
    payload_hash = hashlib.sha256(payload).hexdigest()

    # Sign using RSA
    signature_b64 = asymmetric_sign(payload, private_pem)

    # Verify using verify_electronic_signature
    req = SignatureVerificationRequest(
        payload_hash=payload_hash,
        signature_bytes_b64=signature_b64,
        signer_id="RSA_SIGNER_001",
        signing_reason="AUTHORS_APPROVAL",
        public_key_pem=public_pem,
    )

    result = verify_electronic_signature(req)
    assert result.is_valid is True
    assert result.error_code is None
    assert result.signer_id == "RSA_SIGNER_001"


def test_unified_api_ecdsa_verification(ec_keys):
    """Verify that unified API successfully validates ECDSA signatures.

    Requirements: PRD-SYS-001
    """
    private_pem, public_pem = ec_keys
    payload = b"clinical-trial-audit-log-002"
    payload_hash = hashlib.sha256(payload).hexdigest()

    # Sign using ECDSA
    signature_b64 = asymmetric_sign(payload, private_pem)

    # Verify using verify_electronic_signature
    req = SignatureVerificationRequest(
        payload_hash=payload_hash,
        signature_bytes_b64=signature_b64,
        signer_id="ECDSA_SIGNER_001",
        signing_reason="AUTHORS_APPROVAL",
        public_key_pem=public_pem,
    )

    result = verify_electronic_signature(req)
    assert result.is_valid is True
    assert result.error_code is None
    assert result.signer_id == "ECDSA_SIGNER_001"


def test_unified_api_signature_mismatch(rsa_keys):
    """Verify that unified API returns error on tampered payload or signature mismatch."""
    private_pem, public_pem = rsa_keys
    payload = b"clinical-trial-audit-log-001"
    tampered_hash = hashlib.sha256(b"clinical-trial-audit-log-tampered").hexdigest()

    signature_b64 = asymmetric_sign(payload, private_pem)

    req = SignatureVerificationRequest(
        payload_hash=tampered_hash,
        signature_bytes_b64=signature_b64,
        signer_id="RSA_SIGNER_001",
        signing_reason="AUTHORS_APPROVAL",
        public_key_pem=public_pem,
    )

    result = verify_electronic_signature(req)
    assert result.is_valid is False
    assert result.error_code == "SIGNATURE_MISMATCH"


def test_signature_parsing_formats():
    """Verify standard signature parsing handles multiple formats without crashing."""
    raw_sig = "a1b2c3d4"
    raw_sig_b64 = base64.b64encode(b"a1b2c3d4").decode("utf-8")

    # 1. Raw Bytes / Base64
    assert parse_signature_format(raw_sig_b64) == raw_sig_b64

    # 2. PEM Blocks
    pem_block = f"-----BEGIN SIGNATURE-----\n{raw_sig_b64}\n-----END SIGNATURE-----"
    assert parse_signature_format(pem_block) == raw_sig_b64

    # 3. XML Tags (<Signature> or <SignatureValue>)
    xml_sig_1 = f"<SignatureValue>{raw_sig_b64}</SignatureValue>"
    assert parse_signature_format(xml_sig_1) == raw_sig_b64

    xml_sig_2 = f"<Signature>{raw_sig_b64}</Signature>"
    assert parse_signature_format(xml_sig_2) == raw_sig_b64

    # 4. JSON Metadata
    json_sig_1 = json.dumps({"signature": raw_sig_b64})
    assert parse_signature_format(json_sig_1) == raw_sig_b64

    json_sig_2 = json.dumps({"signature_bytes_b64": raw_sig_b64})
    assert parse_signature_format(json_sig_2) == raw_sig_b64

    # 5. Hex conversion fallback
    hex_sig = raw_sig.encode("utf-8").hex()
    assert parse_signature_format(hex_sig) == raw_sig_b64


def test_signature_parsing_exceptions():
    """Verify that parsing invalid format raises decodable error or value error."""
    with pytest.raises(ValueError):
        parse_signature_format("-----BEGIN SIGNATURE-----\ninvalid-block-no-end")


def test_stripped_block_json_validation():
    """Verify JSON stripped-block payload integrity checks."""
    doc = {
        "report_id": "REP-101",
        "findings": "Normal",
        "sig": "some-signature-b64-value",
        "nested": {"signature": "inner-signature-b64"},
    }
    doc_str = json.dumps(doc)

    # Stripped canonical format should have deleted "sig" and "signature"
    stripped = strip_and_canonicalize_json(doc_str)
    stripped_dict = json.loads(stripped)

    assert "sig" not in stripped_dict
    assert "signature" not in stripped_dict["nested"]
    assert stripped_dict["report_id"] == "REP-101"


def test_stripped_block_xml_validation():
    """Verify XML stripped-block payload integrity checks."""
    xml_doc = (
        "<Document>"
        "<Content>Subject visit verified</Content>"
        "<SignatureValue>xyz</SignatureValue>"
        "<Signature>abc</Signature>"
        "</Document>"
    )

    stripped = strip_xml_signatures(xml_doc)
    assert "<SignatureValue>" not in stripped
    assert "<Signature>" not in stripped
    assert "<Content>" in stripped


def test_unified_api_invalid_inputs():
    """Verify edge cases for unified verification input parameters."""
    # Missing payload_hash
    req = SignatureVerificationRequest(
        payload_hash="",
        signature_bytes_b64="some_sig",
        signer_id="user_1",
        signing_reason="AUTH",
    )
    res = verify_electronic_signature(req)
    assert res.is_valid is False
    assert res.error_code == "MISSING_PAYLOAD"


# ==============================================================================
# 2. CLINICAL DE-IDENTIFICATION ENGINE TESTS
# ==============================================================================


def test_deterministic_per_subject_date_shifting():
    """Verify date-shifting is deterministic and consistent per subject.

    Requirements: PRD-TMF-005
    """
    salt = "gxp-biostat-salt-2026"
    subject_1 = "SUBJ-001"
    subject_2 = "SUBJ-002"

    shift_1 = get_subject_date_shift(subject_1, salt)
    shift_1_again = get_subject_date_shift(subject_1, salt)
    shift_2 = get_subject_date_shift(subject_2, salt)

    # Deterministic consistency
    assert shift_1 == shift_1_again
    # Range limit [-365, 365]
    assert -365 <= shift_1 <= 365
    # Unique across subjects
    assert shift_1 != shift_2


def test_sas_dates_shifting():
    """Verify shifting of standard ISO date strings and numeric SAS dates.

    Requirements: PRD-TMF-005
    """
    salt = "sas-test-salt"
    subject_id = "SUBJ-X"

    offset = get_subject_date_shift(subject_id, salt)

    # Integer SAS Date (e.g., 24110)
    sas_int = 24110
    shifted_int = shift_date_by_subject(sas_int, subject_id, salt)
    assert isinstance(shifted_int, int)
    assert shifted_int == sas_int + offset

    # Float SAS Date (e.g., 24110.5)
    sas_float = 24110.5
    shifted_float = shift_date_by_subject(sas_float, subject_id, salt)
    assert isinstance(shifted_float, float)
    assert shifted_float == sas_float + offset

    # String SAS Date integer
    assert shift_date_by_subject("24110", subject_id, salt) == sas_int + offset

    # String SAS Date float
    assert shift_date_by_subject("24110.5", subject_id, salt) == sas_float + offset

    # ISO date string
    iso_date = "2026-08-01"
    shifted_iso = shift_date_by_subject(iso_date, subject_id, salt)
    assert isinstance(shifted_iso, str)
    assert shifted_iso != iso_date


def test_exact_numeric_and_float_age_capping():
    """Verify that exact ages (ints and floats) above 89 are capped at 89/89.0.

    Requirements: PRD-TMF-005
    """
    # Integers
    assert cap_age_numeric(95) == 89
    assert cap_age_numeric(89) == 89
    assert cap_age_numeric(45) == 45

    # Floats
    assert cap_age_numeric(92.5) == 89.0
    assert cap_age_numeric(89.0) == 89.0
    assert cap_age_numeric(88.9) == 88.9


def test_study_specific_pseudonym_prefixes():
    """Verify that pseudonymization prepends configured study prefixes correctly.

    Requirements: PRD-TMF-005
    """
    salt = "prefix-test-salt"
    subj_id = "SUBJ-123"

    # Prefix provided
    prefix = "STUDY-A-"
    pseudo_prefixed = pseudonymize_subject_id(subj_id, salt, prefix=prefix)
    assert pseudo_prefixed.startswith(prefix)

    # Verify deterministic value length
    pseudo_hash_part = pseudo_prefixed[len(prefix) :]
    assert len(pseudo_hash_part) == 64

    # No prefix provided
    pseudo_normal = pseudonymize_subject_id(subj_id, salt)
    assert len(pseudo_normal) == 64
    assert pseudo_normal == pseudo_hash_part
