"""Unit test suite for X.509 certificate store and CRL revocation verification service.

Requirements: PRD-SYS-001
"""

from datetime import UTC, datetime, timedelta

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID

import packages  # noqa: F401
from packages.security.cert_store import CertificateStoreService


def _generate_test_certificate() -> str:
    """Generate temporary self-signed X.509 certificate PEM string for testing."""
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name(
        [
            x509.NameAttribute(NameOID.COMMON_NAME, "Dr. Test User"),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Cadence Clinical"),
        ]
    )
    now = datetime.now(UTC)
    cert = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(key.public_key())
        .serial_number(123456789)
        .not_valid_before(now - timedelta(days=1))
        .not_valid_after(now + timedelta(days=365))
        .sign(key, hashes.SHA256())
    )
    return cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")


def test_register_and_verify_valid_certificate() -> None:
    """Validate registering valid X.509 certificate and status verification.

    Requirements: PRD-SYS-001
    """
    cert_pem = _generate_test_certificate()
    service = CertificateStoreService()

    record = service.register_certificate(user_id="user_cert_01", cert_pem=cert_pem)
    assert record["user_id"] == "user_cert_01"
    assert record["serial_number"] == hex(123456789)[2:]

    is_valid, status = service.verify_certificate_status(cert_pem)
    assert is_valid is True
    assert status == "VALID"


def test_revoke_certificate_status_check() -> None:
    """Validate revoking certificate adds serial number to CRL and fails verification.

    Requirements: PRD-SYS-001
    """
    cert_pem = _generate_test_certificate()
    service = CertificateStoreService()
    record = service.register_certificate(user_id="user_cert_02", cert_pem=cert_pem)

    serial_hex = record["serial_number"]

    # Revoke certificate
    service.revoke_certificate(cert_serial=serial_hex, reason="Key compromise reported")

    is_valid, status = service.verify_certificate_status(cert_pem)
    assert is_valid is False
    assert "REVOKED: Key compromise reported" in status
