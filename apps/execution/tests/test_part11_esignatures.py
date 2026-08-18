"""Tests for 21 CFR Part 11 electronic signature tamper detection.

Requirements: PRD-SYS-001
"""

import pytest
from cryptography import x509
from cryptography.hazmat.primitives import serialization

from packages.compliance.services.esignature_verifier import (
    ESignatureVerifier,
    TamperDetectedError,
)
from packages.compliance.services.pkcs7_signer import PKCS7Signer


@pytest.fixture
def test_private_key():
    """Load the test private key from fixtures."""
    with open("tests/fixtures/keys/private_key.pem", "rb") as f:
        return serialization.load_pem_private_key(f.read(), password=None)


@pytest.fixture
def test_x509_cert():
    """Load the test self-signed X.509 certificate from fixtures and register in active trust store."""
    with open("tests/fixtures/keys/certificate.crt", "rb") as f:
        cert_bytes = f.read()
        cert = x509.load_pem_x509_certificate(cert_bytes)
        from packages.security.cert_store import get_active_cert_store

        get_active_cert_store().register_certificate(
            user_id="test_user", cert_pem=cert_bytes.decode("utf-8")
        )
        return cert


def test_valid_part11_signature_verification(test_x509_cert, test_private_key):
    """Verify that a validly signed document successfully passes PKCS#7 verification.

    Requirements: PRD-SYS-001
    """
    signer = PKCS7Signer(cert=test_x509_cert, key=test_private_key)
    verifier = ESignatureVerifier()

    original_pdf = b"%PDF-1.4 sample document containing Subject"
    signed_pdf = signer.sign_pdf(original_pdf)

    res = verifier.verify_pdf(signed_pdf)
    assert res.is_valid is True
    assert res.status == "VALID"


def test_tampered_pdf_fails_verification(test_x509_cert, test_private_key):
    """Verify that any byte alteration in a signed PDF instantly triggers a tamper alarm.

    Requirements: PRD-SYS-001
    """
    signer = PKCS7Signer(cert=test_x509_cert, key=test_private_key)
    verifier = ESignatureVerifier()

    original_pdf = b"%PDF-1.4 sample document containing Subject"
    signed_pdf = signer.sign_pdf(original_pdf)

    # Mutate a byte in the signed PDF payload
    tampered_pdf = signed_pdf.replace(b"Subject", b"S_bject")

    # Pass tampered PDF to ESignatureVerifier.verify_pdf();
    # assert verification throws TamperDetectedError and returns VALID == False
    with pytest.raises(TamperDetectedError) as exc_info:
        verifier.verify_pdf(tampered_pdf)

    assert exc_info.value.is_valid is False
    assert exc_info.value.status == "TAMPERED_INVALID_HASH"


def test_certificate_revocation_verification(test_x509_cert, test_private_key):
    """Verify that a revoked certificate correctly reports status CERTIFICATE_REVOKED.

    Requirements: PRD-SYS-001
    """
    signer = PKCS7Signer(cert=test_x509_cert, key=test_private_key)

    # Register the test certificate's serial number as revoked
    verifier = ESignatureVerifier(revoked_certs={test_x509_cert.serial_number})

    original_pdf = b"%PDF-1.4 sample document containing Subject"
    signed_pdf = signer.sign_pdf(original_pdf)

    res = verifier.verify_pdf(signed_pdf)
    assert res.is_valid is False
    assert res.status == "CERTIFICATE_REVOKED"


@pytest.mark.asyncio
async def test_esignature_tamper_detection_e2e(test_x509_cert, test_private_key):
    """Validate 21 CFR Part 11 eSignature engine detects post-signature document tampering.

    Requirements: PRD-SYS-001
    """
    signer = PKCS7Signer(cert=test_x509_cert, key=test_private_key)
    verifier = ESignatureVerifier()

    # 1. Sign original PDF
    original_pdf = b"%PDF-1.4 sample document content"
    signed_pdf = signer.sign_document(original_pdf)

    # 2. Assert original passes verification
    val_res = verifier.verify_signature(signed_pdf)
    assert val_res.is_valid is True

    # 3. Mutate byte in signed payload
    tampered_pdf = signed_pdf[:100] + b"X" + signed_pdf[101:]
    tampered_res = verifier.verify_signature(tampered_pdf)
    assert tampered_res.is_valid is False
    assert "TAMPER" in tampered_res.failure_reason


def test_esignature_duplicate_serial_rejection(test_x509_cert, test_private_key):
    """Validate that esignature verifier blocks a signature where the certificate's serial number
    matches a registered trusted certificate, but its fingerprint/content does not.

    Requirements: PRD-SYS-001
    """
    from datetime import UTC, datetime, timedelta

    from cryptography import x509
    from cryptography.hazmat.primitives import hashes
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID

    # Generate a forged key and certificate with the SAME serial number as test_x509_cert
    forged_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name(
        [
            x509.NameAttribute(NameOID.COMMON_NAME, "Forged User"),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Cadence Clinical"),
        ]
    )
    now = datetime.now(UTC)
    cert_forged = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(forged_key.public_key())
        .serial_number(test_x509_cert.serial_number)  # duplicate serial number!
        .not_valid_before(now - timedelta(days=1))
        .not_valid_after(now + timedelta(days=365))
        .sign(forged_key, hashes.SHA256())
    )

    # Both share the same serial number
    assert test_x509_cert.serial_number == cert_forged.serial_number

    # Sign document with the forged cert and key
    signer_forged = PKCS7Signer(cert=cert_forged, key=forged_key)
    original_pdf = b"%PDF-1.4 sample document containing Subject"
    signed_pdf = signer_forged.sign_pdf(original_pdf)

    # Verification must reject the signature because the fingerprint of the forged certificate
    # does not match the fingerprint of the registered certificate, even though the serial number is a match.
    verifier = ESignatureVerifier()
    res = verifier.verify_pdf(signed_pdf)
    assert res.is_valid is False
    assert res.status == "UNTRUSTED_SELF_SIGNED"


def test_certificate_revocation_string_serial_formats(test_x509_cert, test_private_key):
    """Verify that a certificate is correctly identified as revoked when serial number is provided
    as string decimal, hex, or 0x-prefixed hex in revoked_certs.

    Requirements: PRD-SYS-001
    """
    signer = PKCS7Signer(cert=test_x509_cert, key=test_private_key)
    original_pdf = b"%PDF-1.4 sample document containing Subject"
    signed_pdf = signer.sign_pdf(original_pdf)

    # Test 1: Decimal string serial number
    str_dec_serial = str(test_x509_cert.serial_number)
    verifier1 = ESignatureVerifier(revoked_certs={str_dec_serial})
    res1 = verifier1.verify_pdf(signed_pdf)
    assert res1.is_valid is False
    assert res1.status == "CERTIFICATE_REVOKED"

    # Test 2: Hex string serial number with 0x
    hex_0x_serial = hex(test_x509_cert.serial_number)
    verifier2 = ESignatureVerifier(revoked_certs={hex_0x_serial})
    res2 = verifier2.verify_pdf(signed_pdf)
    assert res2.is_valid is False
    assert res2.status == "CERTIFICATE_REVOKED"

    # Test 3: Raw clean hex string serial number without 0x
    hex_clean_serial = hex(test_x509_cert.serial_number)[2:]
    verifier3 = ESignatureVerifier(revoked_certs={hex_clean_serial})
    res3 = verifier3.verify_pdf(signed_pdf)
    assert res3.is_valid is False
    assert res3.status == "CERTIFICATE_REVOKED"


def test_valid_certificate_pem_substring_no_false_positive(
    test_x509_cert, test_private_key
):
    """Verify that a document signed by a valid certificate is NOT falsely rejected when revoked_certs
    contains small integer serial numbers that appear as accidental substring matches in the PEM block.

    Requirements: PRD-SYS-001
    """
    signer = PKCS7Signer(cert=test_x509_cert, key=test_private_key)
    original_pdf = b"%PDF-1.4 sample document containing Subject"
    signed_pdf = signer.sign_pdf(original_pdf)

    # revoked_certs contains small integer identifiers that are NOT the cert's serial number
    verifier = ESignatureVerifier(revoked_certs={1, 2, 12, "1", "12", "A", "B", "MII"})
    res = verifier.verify_pdf(signed_pdf)

    assert res.is_valid is True
    assert res.status == "VALID"
