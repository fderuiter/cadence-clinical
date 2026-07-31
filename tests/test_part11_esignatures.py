"""Tests for 21 CFR Part 11 electronic signature tamper detection.

Requirements: PRD-SYS-001
"""

import pytest
from cryptography import x509
from cryptography.hazmat.primitives import serialization

from apps.compliance.services.esignature_verifier import (
    ESignatureVerifier,
    TamperDetectedError,
)
from apps.compliance.services.pkcs7_signer import PKCS7Signer


@pytest.fixture
def test_private_key():
    """Load the test private key from fixtures."""
    with open("tests/fixtures/keys/private_key.pem", "rb") as f:
        return serialization.load_pem_private_key(f.read(), password=None)


@pytest.fixture
def test_x509_cert():
    """Load the test self-signed X.509 certificate from fixtures."""
    with open("tests/fixtures/keys/certificate.crt", "rb") as f:
        return x509.load_pem_x509_certificate(f.read())


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
