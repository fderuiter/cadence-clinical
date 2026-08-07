"""Unit test suite for SHA-256 + RSA cryptographic signature payload builder.

Requirements: PRD-SYS-001
"""

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa

import packages  # noqa: F401
from packages.security.signature_builder import CryptographicSignatureBuilder


def test_compute_content_digest() -> None:
    """Validate computing deterministic SHA-256 content digest.

    Requirements: PRD-SYS-001
    """
    builder = CryptographicSignatureBuilder()
    content = {"form_id": "form_vs_01", "SYSBP": 120, "DIABP": 80}

    digest_1 = builder.compute_content_digest(content)
    digest_2 = builder.compute_content_digest(content)

    assert isinstance(digest_1, str)
    assert len(digest_1) == 64  # SHA-256 hex length
    assert digest_1 == digest_2


def test_rsa_signature_sign_and_verify() -> None:
    """Validate RSA private key signing and public key signature verification.

    Requirements: PRD-SYS-001
    """
    # Generate RSA 2048 test key pair
    private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    private_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    )
    public_pem = private_key.public_key().public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )

    builder = CryptographicSignatureBuilder()
    content_digest = builder.compute_content_digest(
        {"form_id": "form_01", "data": "valid"}
    )

    payload = builder.build_signature_payload(
        user_id="pi_user_100",
        purpose="Principal Investigator Approval of Casebook",
        content_digest=content_digest,
    )

    # Sign payload
    signature = builder.sign_payload_rsa(payload, private_pem)
    assert isinstance(signature, str)
    assert len(signature) > 0

    # Verify signature passes with original payload
    is_valid = builder.verify_signature_rsa(payload, signature, public_pem)
    assert is_valid is True

    # Verify signature fails with tampered payload
    tampered_payload = dict(payload)
    tampered_payload["user_id"] = "imposter_user_99"
    is_valid_tampered = builder.verify_signature_rsa(
        tampered_payload, signature, public_pem
    )
    assert is_valid_tampered is False
