import datetime
from datetime import timezone

import pytest
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID
from signature import ApprovalStatus, SignatureManifestation, SigningReason

from packages.security.context import (
    audit_context,
    audit_context_decorator,
    current_signature_context,
)
from packages.security.signing import (
    asymmetric_sign,
    asymmetric_verify,
    capture_certificate_identifiers,
    compute_sha256_hash,
)


@pytest.fixture(scope="module")
def crypto_material():
    """Generates a transient, real RSA private key and a self-signed X.509 certificate for testing."""
    private_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
    )

    subject = issuer = x509.Name(
        [
            x509.NameAttribute(NameOID.COUNTRY_NAME, "US"),
            x509.NameAttribute(NameOID.STATE_OR_PROVINCE_NAME, "California"),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Cadence Clinical"),
            x509.NameAttribute(NameOID.COMMON_NAME, "cadence-clinical.org"),
        ]
    )

    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(private_key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(
            datetime.datetime.now(timezone.utc) - datetime.timedelta(days=1)
        )
        .not_valid_after(
            datetime.datetime.now(timezone.utc) + datetime.timedelta(days=10)
        )
        .add_extension(
            x509.SubjectKeyIdentifier.from_public_key(private_key.public_key()),
            critical=False,
        )
        .sign(private_key, hashes.SHA256())
    )

    private_key_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.TraditionalOpenSSL,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")

    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    return {
        "private_key_pem": private_key_pem,
        "cert_pem": cert_pem,
    }


def test_controlled_enums():
    """Validates controlled signing-reason and approval-status enums."""
    assert SigningReason.AUTHOR == "AUTHOR"
    assert SigningReason.TECHNICAL_QC == "TECHNICAL_QC"
    assert SigningReason.CLINICAL_QC == "CLINICAL_QC"

    assert ApprovalStatus.APPROVED == "APPROVED"
    assert ApprovalStatus.REJECTED == "REJECTED"
    assert ApprovalStatus.PENDING == "PENDING"


def test_sha256_hashing_helper():
    """Validates the pure-python SHA-256 hashing utility."""
    content_str = "trial-protocol-v1"
    content_bytes = b"trial-protocol-v1"

    hash_str = compute_sha256_hash(content_str)
    hash_bytes = compute_sha256_hash(content_bytes)

    assert hash_str == hash_bytes
    assert (
        hash_str
        == "709961da2370f98e2dfe285753a10082e1acc6477c02e7e109ac459ea5a4cab9"  # pragma: allowlist secret
    )


def test_capture_certificate_identifiers(crypto_material):
    """Validates certificate key-identifier and metadata extraction."""
    cert_pem = crypto_material["cert_pem"]
    ids = capture_certificate_identifiers(cert_pem)

    assert "serial_number" in ids
    assert "sha256_fingerprint" in ids
    assert "subject_key_identifier" in ids
    assert len(ids["sha256_fingerprint"]) == 64
    assert len(ids["subject_key_identifier"]) == 40


def test_asymmetric_sign_and_verify(crypto_material):
    """Validates asymmetric cryptographic signing and certificate-bound verification."""
    data = b"secure-trial-data-block"
    private_key_pem = crypto_material["private_key_pem"]
    cert_pem = crypto_material["cert_pem"]

    signature_b64 = asymmetric_sign(data, private_key_pem)
    assert len(signature_b64) > 0

    # Verify signature with cert_pem
    is_valid = asymmetric_verify(data, signature_b64, cert_pem)
    assert is_valid is True

    # Verification with altered data must fail
    is_valid_altered = asymmetric_verify(b"altered-data", signature_b64, cert_pem)
    assert is_valid_altered is False

    # Verification with altered signature must fail
    is_valid_bad_sig = asymmetric_verify(data, "invalid_sig_b64", cert_pem)
    assert is_valid_bad_sig is False


def test_signature_manifestation_lifecycle(crypto_material):
    """Validates the model serialization, signing, and verification lifecycle.
    @req: Trace-13
    """
    content = "clinical-trial-observation-42"
    content_hash = compute_sha256_hash(content)

    manifest = SignatureManifestation(
        signer_id="usr_007",
        timestamp=datetime.datetime.now(timezone.utc),
        signing_reason=SigningReason.INVESTIGATOR_SIGNATURE,
        ip_address="192.168.1.50",
        user_agent="Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
        sha256_hash=content_hash,
    )

    # Initially verified should fail because signature/cert are not set
    assert manifest.verify() is False

    # Get canonical bytes for signing
    canonical_bytes = manifest.get_canonical_bytes()
    assert b"usr_007" in canonical_bytes
    assert b"INVESTIGATOR_SIGNATURE" in canonical_bytes
    assert content_hash.encode("utf-8") in canonical_bytes

    # Sign canonical bytes
    private_key_pem = crypto_material["private_key_pem"]
    cert_pem = crypto_material["cert_pem"]
    sig_b64 = asymmetric_sign(canonical_bytes, private_key_pem)

    ids = capture_certificate_identifiers(cert_pem)

    # Bind cryptographic primitives
    manifest.signature = sig_b64
    manifest.certificate_pem = cert_pem
    manifest.key_identifier = ids["subject_key_identifier"]

    # Now verify
    assert manifest.verify() is True

    # Tampering with any model attribute must invalidate the verification
    manifest_tampered = manifest.model_copy()
    manifest_tampered.signer_id = "malicious_user"
    assert manifest_tampered.verify() is False


def test_signature_context_propagation():
    """Validates propagating signature context through async audit context."""
    dummy_manifestation = {
        "signer_id": "usr_999",
        "signing_reason": "CLINICAL_QC",
        "ip_address": "10.0.0.1",
    }

    assert current_signature_context.get() is None

    with audit_context(signature_context=dummy_manifestation):
        assert current_signature_context.get() == dummy_manifestation
        # Non-leaking verification: context matches dummy dict
        assert current_signature_context.get()["signer_id"] == "usr_999"

    # Context is cleaned up after exit
    assert current_signature_context.get() is None


@pytest.mark.asyncio
async def test_async_signature_context_decorator():
    """Validates decorating asynchronous functions with audit contexts including signature."""
    dummy_manifestation = {
        "signer_id": "usr_888",
        "signing_reason": "TECHNICAL_QC",
        "ip_address": "10.0.0.2",
    }

    def get_sig(*args, **kwargs):
        return dummy_manifestation

    @audit_context_decorator(signature_context_getter=get_sig)
    async def async_operation():
        return current_signature_context.get()

    result = await async_operation()
    assert result == dummy_manifestation
    assert current_signature_context.get() is None
