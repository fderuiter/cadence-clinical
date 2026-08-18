"""Unit tests for ESignatureVerifier certificate revocation normalization and exact matching.

Requirements: PRD-SYS-001
"""

from datetime import UTC, datetime, timedelta

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID

from packages.compliance.services.esignature_verifier import ESignatureVerifier
from packages.compliance.services.pkcs7_signer import PKCS7Signer


def _create_test_cert_and_key(serial_number: int):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name(
        [
            x509.NameAttribute(NameOID.COMMON_NAME, f"User Serial {serial_number}"),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Cadence Clinical"),
        ]
    )
    now = datetime.now(UTC)
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
    return cert, key


def test_revoked_certs_normalization_helper():
    """Verify that _get_normalized_revoked_certs parses int, decimal str, hex str, 0x hex str,
    fingerprint, and PEM cert inputs correctly into strongly typed sets.

    Requirements: PRD-SYS-001
    """
    cert, _ = _create_test_cert_and_key(serial_number=123456)
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")
    cert_fp = cert.fingerprint(hashes.SHA256()).hex().lower()

    verifier = ESignatureVerifier(
        revoked_certs=[
            100,  # int
            "200",  # decimal str
            "0x1000",  # 0x hex str (4096)
            "A1:B2:C3:D4:E5:F6:12:34:56:78:90:AB:CD:EF:12:34:56:78:90:AB:CD:EF:12:34:56:78:90:AB:CD:EF:12:34",  # fingerprint with colons
            cert_pem,  # full PEM string
            None,
            "",
        ]
    )

    serials_int, serials_str, fingerprints, pems = (
        verifier._get_normalized_revoked_certs()
    )

    assert 100 in serials_int
    assert 200 in serials_int
    assert 4096 in serials_int
    assert 123456 in serials_int  # extracted from cert_pem

    assert "100" in serials_str
    assert "200" in serials_str
    assert "0x1000" in serials_str

    assert (
        "a1b2c3d4e5f61234567890abcdef1234567890abcdef1234567890abcdef1234"  # pragma: allowlist secret
        in fingerprints
    )
    assert cert_fp in fingerprints
    assert cert_pem.strip() in pems


def test_revocation_exact_match_prevents_false_positives():
    """Verify that small integers or short strings in revoked_certs do NOT trigger false positives
    for valid certificates whose PEM blocks happen to contain those substring patterns.

    Requirements: PRD-SYS-001
    """
    cert_valid, key_valid = _create_test_cert_and_key(serial_number=999888777)

    # Register cert in active trust store so it passes self-signed check
    from packages.security.cert_store import get_active_cert_store

    cert_pem_str = cert_valid.public_bytes(serialization.Encoding.PEM).decode("utf-8")
    get_active_cert_store().register_certificate(
        user_id="user_valid", cert_pem=cert_pem_str
    )

    signer = PKCS7Signer(cert=cert_valid, key=key_valid)
    signed_pdf = signer.sign_pdf(b"%PDF-1.4 Test PDF Document")

    # Pass small integers and common base64 strings in revoked_certs
    verifier = ESignatureVerifier(revoked_certs=[1, 2, "1", "12", "MII", "BEGIN"])
    res = verifier.verify_pdf(signed_pdf)

    assert res.is_valid is True
    assert res.status == "VALID"


def test_revocation_blocks_when_serial_matches():
    """Verify that signature verification fails with CERTIFICATE_REVOKED when the certificate's
    serial number matches an entry in revoked_certs (as int or str).

    Requirements: PRD-SYS-001
    """
    cert_rev, key_rev = _create_test_cert_and_key(serial_number=555444333)

    from packages.security.cert_store import get_active_cert_store

    cert_pem_str = cert_rev.public_bytes(serialization.Encoding.PEM).decode("utf-8")
    get_active_cert_store().register_certificate(
        user_id="user_rev", cert_pem=cert_pem_str
    )

    signer = PKCS7Signer(cert=cert_rev, key=key_rev)
    signed_pdf = signer.sign_pdf(b"%PDF-1.4 Test PDF Document")

    # Test integer match
    verifier_int = ESignatureVerifier(revoked_certs=[555444333])
    res_int = verifier_int.verify_pdf(signed_pdf)
    assert res_int.is_valid is False
    assert res_int.status == "CERTIFICATE_REVOKED"

    # Test decimal string match
    verifier_str = ESignatureVerifier(revoked_certs=["555444333"])
    res_str = verifier_str.verify_pdf(signed_pdf)
    assert res_str.is_valid is False
    assert res_str.status == "CERTIFICATE_REVOKED"

    # Test hex string match
    verifier_hex = ESignatureVerifier(revoked_certs=[hex(555444333)])
    res_hex = verifier_hex.verify_pdf(signed_pdf)
    assert res_hex.is_valid is False
    assert res_hex.status == "CERTIFICATE_REVOKED"
