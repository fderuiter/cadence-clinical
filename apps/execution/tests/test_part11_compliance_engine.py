"""Tests for 21 CFR Part 11 Electronic Signature Compliance Engine.

Covers all 5 Acceptance Criteria:
1. Blocks and reports any uploaded document containing "MOCK" or "MOCK_SIGNATURE" within the signature payload.
2. Rejects uploads of mandatory regulatory documents when the payload requests to bypass signature checks.
3. Document signatures using legacy PKCS#1 v1.5 padding fail verification and trigger compliance alerts.
4. Signature extraction rejects documents containing duplicate or injected certificate PEM blocks.
5. Signatures verified with self-signed certificates fail validation unless the certificate originates from an approved Certificate Authority in the trust store.
"""

import base64

import pytest
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

from apps.etmf.adapters.cryptography import (
    validate_document_signature,
)
from packages.security.cert_store import get_active_cert_store


@pytest.fixture(autouse=True)
def disable_mock_signatures(monkeypatch):
    monkeypatch.setenv("ALLOW_MOCK_SIGNATURES", "0")


def generate_test_keys():
    """Generate ephemeral RSA private key and self-signed certificate."""
    private_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
    )
    subject = issuer = x509.Name(
        [
            x509.NameAttribute(x509.NameOID.COMMON_NAME, "test-ca.org"),
        ]
    )
    import datetime

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


def test_legacy_padding_pkcs1v15_fails():
    """AC 3: Document signatures using legacy PKCS#1 v1.5 padding fail verification."""
    private_key, cert = generate_test_keys()
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    # Register in trust store to bypass self-signed check first
    store = get_active_cert_store()
    store.register_certificate(user_id="test_user", cert_pem=cert_pem)

    content_data = "Trial records showing drug effectiveness."
    # Sign using legacy PKCS1v15
    sig_bytes_legacy = private_key.sign(
        content_data.encode("utf-8"),
        padding.PKCS1v15(),
        hashes.SHA256(),
    )
    sig_b64 = base64.b64encode(sig_bytes_legacy).decode("utf-8")

    document_content = (
        f"{content_data}\n"
        f"-----BEGIN CERTIFICATE-----\n{cert_pem.replace('-----BEGIN CERTIFICATE-----', '').replace('-----END CERTIFICATE-----', '').strip()}\n-----END CERTIFICATE-----\n"
        f"-----BEGIN SIGNATURE-----\n{sig_b64}\n-----END SIGNATURE-----"
    )

    is_valid, msg = validate_document_signature("Approved Protocol", document_content)
    # Must fail because padding is legacy!
    assert not is_valid
    assert (
        "verification failed" in msg.lower()
        or "tampered" in msg.lower()
        or "invalid signature" in msg.lower()
    )


def test_rsassa_pss_succeeds():
    """Verify that modern RSA-PSS signatures succeed when properly configured and registered."""
    private_key, cert = generate_test_keys()
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    # Register in trust store
    store = get_active_cert_store()
    store.register_certificate(user_id="test_user", cert_pem=cert_pem)

    content_data = "Trial records showing drug effectiveness."
    # Sign using PSS
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

    is_valid, msg = validate_document_signature("Approved Protocol", document_content)
    assert is_valid
    assert "successfully verified" in msg.lower()


def test_mock_signatures_blocked(monkeypatch):
    """AC 1: Blocks and reports any uploaded document containing "MOCK" or "MOCK_SIGNATURE" within the signature payload."""
    monkeypatch.setenv("ALLOW_MOCK_SIGNATURES", "0")
    document_content_1 = (
        "Some report content.\n"
        "-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE_PEM_BODY\n-----END CERTIFICATE-----\n"
        "-----BEGIN SIGNATURE-----\nTU9DS19TSUdfREFUQQ==\n-----END SIGNATURE-----"
    )
    is_valid, msg = validate_document_signature("Approved Protocol", document_content_1)
    assert not is_valid
    assert "mock" in msg.lower()


def test_mandatory_documents_bypass_rejected():
    """AC 2: Rejects uploads of mandatory regulatory documents when the payload requests to bypass signature checks."""
    # FDA Form 1572 with bypass request
    is_valid, msg = validate_document_signature(
        "FDA Form 1572",
        "Some form content.",
        metadata_json={"requires_signature": False},
    )
    assert not is_valid
    assert "bypass" in msg.lower()

    # Financial Disclosure with skip flag
    is_valid_2, msg_2 = validate_document_signature(
        "Financial Disclosure",
        "Some disclosure content.",
        metadata_json={"skip_signature": True},
    )
    assert not is_valid_2
    assert "bypass" in msg_2.lower()


def test_duplicate_certificate_injection_rejected():
    """AC 4: Signature extraction rejects documents containing duplicate or injected certificate PEM blocks."""
    document_content_duplicate = (
        "Important Clinical trial results.\n"
        "-----BEGIN CERTIFICATE-----\nCERT_ONE\n-----END CERTIFICATE-----\n"
        "-----BEGIN CERTIFICATE-----\nCERT_TWO_INJECTED\n-----END CERTIFICATE-----\n"
        "-----BEGIN SIGNATURE-----\nSIGNATURE\n-----END SIGNATURE-----"
    )
    is_valid, msg = validate_document_signature(
        "Approved Protocol", document_content_duplicate
    )
    assert not is_valid
    assert (
        "duplicate" in msg.lower()
        or "structural" in msg.lower()
        or "injected" in msg.lower()
    )


def test_unapproved_self_signed_certificate_fails():
    """AC 5: Signatures verified with self-signed certificates fail validation unless registered in the trust store."""
    private_key, cert = generate_test_keys()
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    # Do NOT register in the trust store
    content_data = "Some dataset observations."
    sig_bytes = private_key.sign(
        content_data.encode("utf-8"),
        padding.PSS(
            mgf=padding.MGF1(hashes.SHA256()),
            salt_length=padding.PSS.MAX_LENGTH,
        ),
        hashes.SHA256(),
    )
    sig_b64 = base64.b64encode(sig_bytes).decode("utf-8")

    document_content = (
        f"{content_data}\n"
        f"-----BEGIN CERTIFICATE-----\n{cert_pem.replace('-----BEGIN CERTIFICATE-----', '').replace('-----END CERTIFICATE-----', '').strip()}\n-----END CERTIFICATE-----\n"
        f"-----BEGIN SIGNATURE-----\n{sig_b64}\n-----END SIGNATURE-----"
    )

    is_valid, msg = validate_document_signature("Approved Protocol", document_content)
    assert not is_valid
    assert "self-signed" in msg.lower() and "not approved" in msg.lower()
