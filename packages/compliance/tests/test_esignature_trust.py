"""Unit test suite for eSignature certificate trust store enforcement.

Requirements: PRD-SYS-053, Trace-14
"""

import random
from datetime import UTC, datetime, timedelta

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID

from packages.compliance.services.esignature_verifier import ESignatureVerifier
from packages.compliance.services.pkcs7_signer import PKCS7Signer
from packages.security.cert_store import get_active_cert_store


def _generate_test_cert_and_key(
    common_name: str = "Test User",
) -> tuple[x509.Certificate, rsa.RSAPrivateKey, str]:
    """Generate self-signed X.509 certificate and private key for testing."""
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name(
        [
            x509.NameAttribute(NameOID.COMMON_NAME, common_name),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Cadence Clinical GxP"),
        ]
    )
    now = datetime.now(UTC)
    serial_number = random.randint(100000000, 999999999)
    cert = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(key.public_key())
        .serial_number(serial_number)
        .not_valid_before(now - timedelta(days=1))
        .not_valid_after(now + timedelta(days=365))
        .sign(key, hashes.SHA256())
    )
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")
    return cert, key, cert_pem


def test_untrusted_self_signed_cert_rejected():
    """Verify that an untrusted self-signed certificate is strictly rejected even if subject has Cadence names.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    cert, key, _ = _generate_test_cert_and_key("Cadence GxP Runner")
    signer = PKCS7Signer(cert=cert, key=key)
    pdf_bytes = signer.sign_pdf(b"%PDF-1.4 sample GxP clinical document")

    verifier = ESignatureVerifier()
    res = verifier.verify_pdf(pdf_bytes)

    assert res.is_valid is False
    assert res.status == "UNTRUSTED_SELF_SIGNED"
    assert (
        "Self-signed certificate is not approved in trust store" in res.failure_reason
    )


def test_trusted_self_signed_cert_accepted():
    """Verify that a self-signed certificate pre-loaded in CertStore successfully passes verification.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    cert, key, cert_pem = _generate_test_cert_and_key("Pre-Approved Officer")
    cert_store = get_active_cert_store()
    cert_store.register_certificate(f"officer_{cert.serial_number}", cert_pem)

    signer = PKCS7Signer(cert=cert, key=key)
    pdf_bytes = signer.sign_pdf(b"%PDF-1.4 sample GxP clinical document")

    verifier = ESignatureVerifier()
    res = verifier.verify_pdf(pdf_bytes)

    assert res.is_valid is True
    assert res.status == "VALID"
