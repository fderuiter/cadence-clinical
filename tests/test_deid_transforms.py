"""
Tests for de-identification transforms and signed redaction manifests.
"""

import pytest
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa

from packages.deid.detector import DeidDetector
from packages.deid.manifest import (
    build_redaction_manifest,
    sign_manifest_asymmetric,
    sign_manifest_symmetric,
    verify_manifest_asymmetric,
    verify_manifest_symmetric,
)
from packages.deid.models import ComplianceProfile, DetectionResult, DetectorCategory
from packages.deid.transforms import (
    apply_deid_transforms,
    cap_age_string,
    pseudonymize_value,
    shift_date_string,
)


@pytest.fixture
def rsa_keypair():
    """Generates a temporary RSA keypair for asymmetric signing tests."""
    private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    private_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.TraditionalOpenSSL,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")

    public_key = private_key.public_key()
    public_pem = public_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    ).decode("utf-8")

    return private_pem, public_pem


def test_pseudonymize_value_deterministic():
    """Test that pseudonyms are deterministic and secure."""
    secret = "my-secure-salt-12345"
    val = "john.doe@example.com"

    p1 = pseudonymize_value(val, secret)
    p2 = pseudonymize_value(val, secret)
    p3 = pseudonymize_value(val, "different-salt")
    p4 = pseudonymize_value("other.user@example.com", secret)

    # Deterministic
    assert p1 == p2
    assert len(p1) == 64  # Hex-encoded SHA-256 is 64 characters

    # Unique across salt/secrets and inputs
    assert p1 != p3
    assert p1 != p4


def test_shift_date_string():
    """Test date shifting across common formats."""
    # Default shift 365 days
    assert shift_date_string("2026-07-30", 365) == "2027-07-30"

    # Custom shift
    assert shift_date_string("2026-07-30", -10) == "2026-07-20"

    # Preserving different date formats
    assert shift_date_string("2026/07/30", 5) == "2026/08/04"
    assert shift_date_string("07/30/2026", 1) == "07/31/2026"
    assert shift_date_string("15-Jan-2026", 10) == "25-Jan-2026"
    assert shift_date_string("Jan 15, 2026", 10) == "Jan 25, 2026"
    assert shift_date_string("Jan 15 2026", 10) == "Jan 25 2026"

    # Handles invalid date gracefully
    assert shift_date_string("not-a-date", 10) == "[DATE_INVALID]"


def test_cap_age_string():
    """Test age capping above 89."""
    # Above cap
    assert cap_age_string("age 95", 89) == "age 89+"
    assert cap_age_string("92 years old", 89) == "89+ years old"
    assert cap_age_string("aged 102", 89) == "aged 89+"
    assert cap_age_string("91yo", 89) == "89+yo"

    # Below or equal to cap
    assert cap_age_string("age 45", 89) == "age 45"
    assert cap_age_string("89 years old", 89) == "89 years old"

    # No numeric age
    assert cap_age_string("old age", 89) == "old age"


def test_apply_deid_transforms_right_to_left():
    """Verify that multiple transforms apply correctly from right to left preserving offsets."""
    text = "Subject John (age: 95) visited on 2026-07-30. Contact: john@example.com"

    # We manually construct detection results with their original offsets
    results = [
        DetectionResult(
            category=DetectorCategory.CUSTOM, start=8, end=12, value="John"
        ),
        DetectionResult(
            category=DetectorCategory.AGE, start=14, end=21, value="age: 95"
        ),
        DetectionResult(
            category=DetectorCategory.DATES, start=33, end=43, value="2026-07-30"
        ),
        DetectionResult(
            category=DetectorCategory.EMAIL, start=54, end=72, value="john@example.com"
        ),
    ]

    # Configuration strategies
    strategies = {
        DetectorCategory.CUSTOM: "pseudonymize",
        DetectorCategory.AGE: "age_cap",
        DetectorCategory.DATES: "date_shift",
        DetectorCategory.EMAIL: "mask",
    }

    redacted_text, redaction_record = apply_deid_transforms(
        text,
        results,
        strategies=strategies,
        salt="test-salt",
        shift_days=10,
        age_cap=89,
    )

    # Verify that redacted text contains transformed strings
    expected_john_pseudo = pseudonymize_value("John", "test-salt")
    assert expected_john_pseudo in redacted_text
    assert "age: 89+" in redacted_text
    assert "2026-08-09" in redacted_text
    assert "[EMAIL]" in redacted_text
    assert "john@example.com" not in redacted_text

    # Verify the redaction record does not leak original values
    assert len(redaction_record) == 4
    for r in redaction_record:
        # Check that no original values are stored in the record
        assert not hasattr(r, "value")
        assert not hasattr(r, "original_value")
        # Ensure category, strategy, and replacements are correct
        if r.category == DetectorCategory.CUSTOM:
            assert r.strategy == "pseudonymize"
            assert r.replacement == expected_john_pseudo
        elif r.category == DetectorCategory.AGE:
            assert r.strategy == "age_cap"
            assert r.replacement == "age: 89+"


def test_redaction_manifest_symmetric_tamper_evident():
    """Verify building, symmetric signing, verification, and tamper detection."""
    results = [
        DetectionResult(
            category=DetectorCategory.EMAIL, start=0, end=10, value="test@me.com"
        ),
        DetectionResult(
            category=DetectorCategory.DATES, start=15, end=25, value="2026-07-30"
        ),
    ]

    text = "test@me.com on 2026-07-30"
    _, record = apply_deid_transforms(text, results, default_strategy="mask")

    # Build manifest
    manifest = build_redaction_manifest(
        redaction_record=record,
        operator_identity="Dr. Alice",
        reason="Clinical trial export",
        source_version="v1.0.0",
        target_version="v1.0.0-deid",
    )

    assert manifest.categories_counts == {
        DetectorCategory.EMAIL: 1,
        DetectorCategory.DATES: 1,
    }
    assert manifest.strategies == {
        DetectorCategory.EMAIL: "mask",
        DetectorCategory.DATES: "mask",
    }
    assert manifest.reason == "Clinical trial export"

    # Sign with HMAC
    secret = b"my-manifest-signing-secret-key-101"
    signed_manifest = sign_manifest_symmetric(manifest, secret)
    assert signed_manifest.signature is not None

    # Verification passes
    assert verify_manifest_symmetric(signed_manifest, secret) is True

    # Verification with different secret fails
    assert verify_manifest_symmetric(signed_manifest, b"wrong-secret") is False

    # Tampering with data fails verification
    tampered_manifest = signed_manifest.model_copy(deep=True)
    tampered_manifest.categories_counts[DetectorCategory.EMAIL] = 5
    assert verify_manifest_symmetric(tampered_manifest, secret) is False

    # Tampering with operator identity fails
    tampered_manifest_2 = signed_manifest.model_copy(deep=True)
    tampered_manifest_2.operator_identity = "Malicious Operator"
    assert verify_manifest_symmetric(tampered_manifest_2, secret) is False


def test_redaction_manifest_asymmetric_tamper_evident(rsa_keypair):
    """Verify building, asymmetric signing, verification, and tamper detection."""
    private_pem, public_pem = rsa_keypair

    results = [
        DetectionResult(
            category=DetectorCategory.EMAIL, start=0, end=10, value="test@me.com"
        ),
    ]
    text = "test@me.com"
    _, record = apply_deid_transforms(text, results, default_strategy="mask")

    manifest = build_redaction_manifest(
        redaction_record=record,
        operator_identity="CRA Bob",
        reason="Regulatory audit submission",
        source_version="v2.1",
        target_version="v2.1-redacted",
    )

    # Sign
    signed = sign_manifest_asymmetric(manifest, private_pem)
    assert signed.signature is not None

    # Verify passes
    assert verify_manifest_asymmetric(signed, public_pem) is True

    # Tampering with reason fails verification
    tampered = signed.model_copy(deep=True)
    tampered.reason = "Altered reason"
    assert verify_manifest_asymmetric(tampered, public_pem) is False


def test_empty_reason_raises_validation_error():
    """Verify that build_redaction_manifest raises ValidationError for empty or white-space reason."""
    with pytest.raises(
        ValueError, match="Reason for redaction must be a non-empty string"
    ):
        build_redaction_manifest(
            redaction_record=[],
            operator_identity="Operator",
            reason="   ",
            source_version="v1",
            target_version="v2",
        )


def test_end_to_end_detector_and_transforms():
    """Test full workflow of scanning text with DeidDetector and applying customized transforms."""
    text = (
        "The patient John (aged 95) can be emailed at john@gmail.com on Jan 15, 2026."
    )
    detector = DeidDetector()
    results = detector.detect(
        text, profile=ComplianceProfile.HIPAA, custom_terms=["John"]
    )

    # Ensure John, age, email, and date are detected
    categories = {r.category for r in results}
    assert DetectorCategory.CUSTOM in categories
    assert DetectorCategory.AGE in categories
    assert DetectorCategory.EMAIL in categories
    assert DetectorCategory.DATES in categories

    # Custom strategies
    strategies = {
        DetectorCategory.CUSTOM: "pseudonymize",
        DetectorCategory.AGE: "age_cap",
        DetectorCategory.EMAIL: "mask",
        DetectorCategory.DATES: "date_shift",
    }

    redacted, record = apply_deid_transforms(
        text,
        results,
        strategies=strategies,
        salt="test-salt",
        shift_days=10,
        age_cap=89,
    )

    # Verify correct transforms
    assert "aged 89+" in redacted
    assert "[EMAIL]" in redacted
    assert "Jan 25, 2026" in redacted
    assert "John" not in redacted
    assert "john@gmail.com" not in redacted

    # Record matches categories
    assert len(record) == 4
    categories_redacted = {r.category for r in record}
    assert categories_redacted == {
        DetectorCategory.CUSTOM,
        DetectorCategory.AGE,
        DetectorCategory.EMAIL,
        DetectorCategory.DATES,
    }
