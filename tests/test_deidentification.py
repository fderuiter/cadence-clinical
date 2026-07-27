"""
Unit tests for the clinical de-identification engine and redacted manifest cryptographic workflows.
"""

import pytest
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa

from packages.deid.detector import DeidDetector, resolve_overlaps
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
def temp_rsa_keypair():
    """Generates an ephemeral RSA keypair for asymmetric manifest signing validation."""
    # @req:PRD-TMF-005
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


def test_detections_all_categories():
    """
    Verify DeidDetector successfully identifies PII/PHI across all supported categories.
    """
    # @req:PRD-TMF-005
    detector = DeidDetector()

    # 1. Email detection
    email_text = (
        "Please write to bob.smith@clinical-research.org or admin-cadence@gmail.com."
    )
    results = detector.detect(email_text, profile=ComplianceProfile.HIPAA)
    emails = [r for r in results if r.category == DetectorCategory.EMAIL]
    assert len(emails) == 2
    assert "bob.smith@clinical-research.org" in [e.value for e in emails]
    assert "admin-cadence@gmail.com" in [e.value for e in emails]

    # 2. Telephone & Fax detection
    phone_text = "Primary phone: +1 (555) 019-9999, secondary fax: 555-4321."
    results = detector.detect(phone_text, profile=ComplianceProfile.HIPAA)
    phones = [r for r in results if r.category == DetectorCategory.TELEPHONE_FAX]
    assert len(phones) == 2
    assert "+1 (555) 019-9999" in [p.value for p in phones]
    assert "555-4321" in [p.value for p in phones]

    # 3. SSN and National ID detection
    ssn_text = "My SSN is 123-45-6789. Alternate ID is PP123456C."
    results = detector.detect(ssn_text, profile=ComplianceProfile.HIPAA)
    ssns = [r for r in results if r.category == DetectorCategory.SSN_NATIONAL_ID]
    assert len(ssns) == 2
    assert "123-45-6789" in [s.value for s in ssns]
    assert "PP123456C" in [s.value for s in ssns]

    # 4. Dates detection
    dates_text = "Admitted 2026-05-15, discharged 08/20/2026. Born on 10-Jan-1992."
    results = detector.detect(dates_text, profile=ComplianceProfile.HIPAA)
    dates = [r for r in results if r.category == DetectorCategory.DATES]
    assert len(dates) == 3
    assert "2026-05-15" in [d.value for d in dates]
    assert "08/20/2026" in [d.value for d in dates]
    assert "10-Jan-1992" in [d.value for d in dates]

    # 5. Zip and geographic codes detection
    zip_text = "Zip codes: 90210, Canadian postal code: K1A 0B1, UK: SW1A 1AA."
    results = detector.detect(zip_text, profile=ComplianceProfile.HIPAA)
    zips = [r for r in results if r.category == DetectorCategory.ZIP_GEOGRAPHIC]
    assert len(zips) == 3
    assert "90210" in [z.value for z in zips]
    assert "K1A 0B1" in [z.value for z in zips]
    assert "SW1A 1AA" in [z.value for z in zips]

    # 6. URL detection
    url_text = (
        "Visit https://cadence-clinical.org/trial-001 or www.nih.gov for documentation."
    )
    results = detector.detect(url_text, profile=ComplianceProfile.HIPAA)
    urls = [r for r in results if r.category == DetectorCategory.URLS]
    assert len(urls) == 2
    assert "https://cadence-clinical.org/trial-001" in [u.value for u in urls]
    assert "www.nih.gov" in [u.value for u in urls]

    # 7. IP and MAC Address detection
    ip_text = "Host IP is 192.168.1.50, IPv6 is 2001:db8::1, MAC is 00:0a:95:9d:68:16."
    results = detector.detect(ip_text, profile=ComplianceProfile.HIPAA)
    ips = [r for r in results if r.category == DetectorCategory.IP_MAC_ADDRESSES]
    assert len(ips) == 3
    assert "192.168.1.50" in [i.value for i in ips]
    assert "2001:db8::1" in [i.value for i in ips]
    assert "00:0a:95:9d:68:16" in [i.value for i in ips]

    # 8. Medical Record Number & Account ID detection
    mrn_text = "MRN code is MRN-123456, electronic record is EHR-987654, and NHS number is 123 456 7890."
    results = detector.detect(mrn_text, profile=ComplianceProfile.HIPAA)
    mrns = [r for r in results if r.category == DetectorCategory.MEDICAL_RECORD_ACCOUNT]
    assert len(mrns) == 3
    assert "MRN-123456" in [m.value for m in mrns]
    assert "EHR-987654" in [m.value for m in mrns]
    assert "123 456 7890" in [m.value for m in mrns]

    # 9. Age detection
    age_text = "Patient is age 95, or age: 104, also check 91 years old or 92-yo."
    results = detector.detect(age_text, profile=ComplianceProfile.HIPAA)
    ages = [r for r in results if r.category == DetectorCategory.AGE]
    assert len(ages) == 4
    assert "age 95" in [a.value for a in ages]
    assert "age: 104" in [a.value for a in ages]
    assert "91 years old" in [a.value for a in ages]
    assert "92-yo" in [a.value for a in ages]

    # 10. Custom literal terms detection
    custom_text = "The investigator is Dr. Alan Turing, initials AT."
    results = detector.detect(
        custom_text,
        profile=ComplianceProfile.HIPAA,
        custom_terms=["Alan Turing", "AT"],
    )
    customs = [r for r in results if r.category == DetectorCategory.CUSTOM]
    assert len(customs) == 2
    assert "Alan Turing" in [c.value for c in customs]
    assert "AT" in [c.value for c in customs]


def test_compliance_profiles():
    """
    Verify ComplianceProfile selections correctly restrict or enable matched categories.
    - HIPAA: all standard clinical categories.
    - EU_CTR: restricted (e.g. EMAIL/DATES/CUSTOM active, but IP_MAC_ADDRESSES/TELEPHONE_FAX disabled).
    """
    # @req:PRD-TMF-005
    detector = DeidDetector()
    text = "Write to doctor@clinic.org, check server at 10.0.0.1, born on 1990-01-01."

    # HIPAA
    hipaa_res = detector.detect(text, profile=ComplianceProfile.HIPAA)
    hipaa_categories = {r.category for r in hipaa_res}
    assert DetectorCategory.EMAIL in hipaa_categories
    assert DetectorCategory.IP_MAC_ADDRESSES in hipaa_categories
    assert DetectorCategory.DATES in hipaa_categories

    # EU_CTR
    ctr_res = detector.detect(text, profile=ComplianceProfile.EU_CTR)
    ctr_categories = {r.category for r in ctr_res}
    assert DetectorCategory.EMAIL in ctr_categories
    assert DetectorCategory.DATES in ctr_categories
    assert DetectorCategory.IP_MAC_ADDRESSES not in ctr_categories


def test_overlap_resolution_comprehensive():
    """
    Verify overlapping detection results are deterministically resolved following strict priority rules:
      1. start offset ascending
      2. end offset descending (longer/wider intervals processed first)
      3. category name alphabetically
      4. value length descending
    """
    # @req:PRD-TMF-005
    results = [
        # Overlapping set 1: nested/wider overlap
        DetectionResult(category="custom", start=5, end=15, value="John Smith"),
        DetectionResult(
            category="email", start=5, end=25, value="John Smith@example.com"
        ),
        DetectionResult(category="urls", start=16, end=25, value="example.com"),
        # Overlapping set 2: partial offset overlap (prioritizes earliest start)
        DetectionResult(category="custom", start=30, end=38, value="123-4567"),
        DetectionResult(category="telephone_fax", start=35, end=45, value="4567-8901"),
        # Overlapping set 3: tie-breakers with identical range
        DetectionResult(category="zip_geographic", start=50, end=55, value="90210"),
        DetectionResult(category="custom", start=50, end=55, value="90210"),
    ]

    resolved = resolve_overlaps(results)

    # Validate output
    # Set 1: John Smith@example.com (start=5, end=25) is the widest. It should override "John Smith" and "example.com".
    assert any(r.value == "John Smith@example.com" for r in resolved)
    assert not any(r.value == "John Smith" for r in resolved)
    assert not any(r.value == "example.com" for r in resolved)

    # Set 2: 123-4567 starts earlier at 30, so it gets processed and accepted first. 4567-8901 (starts 35) is rejected due to overlap.
    assert any(r.value == "123-4567" for r in resolved)
    assert not any(r.value == "4567-8901" for r in resolved)

    # Set 3: Category alphabetically (custom before zip_geographic)
    ties = [r for r in resolved if r.start == 50 and r.end == 55]
    assert len(ties) == 1
    assert ties[0].category == "custom"


def test_transforms_all_strategies():
    """
    Verify application of various de-identification strategies (mask, pseudonymize, date_shift, age_cap).
    """
    # @req:PRD-TMF-005
    text = "CRA Bob (age 95) visited on 2026-07-30. Contact at bob@gmail.com."

    results = [
        DetectionResult(category=DetectorCategory.CUSTOM, start=4, end=7, value="Bob"),
        DetectionResult(category=DetectorCategory.AGE, start=9, end=15, value="age 95"),
        DetectionResult(
            category=DetectorCategory.DATES, start=28, end=38, value="2026-07-30"
        ),
        DetectionResult(
            category=DetectorCategory.EMAIL, start=51, end=64, value="bob@gmail.com"
        ),
    ]

    strategies = {
        DetectorCategory.CUSTOM: "pseudonymize",
        DetectorCategory.AGE: "age_cap",
        DetectorCategory.DATES: "date_shift",
        DetectorCategory.EMAIL: "mask",
    }

    redacted_text, records = apply_deid_transforms(
        text=text,
        results=results,
        strategies=strategies,
        salt="secure-salt-xyz",
        shift_days=10,
        age_cap=89,
    )

    # Check replacements in text
    expected_pseudo = pseudonymize_value("Bob", "secure-salt-xyz")
    assert expected_pseudo in redacted_text
    assert "age 89+" in redacted_text
    assert "2026-08-09" in redacted_text  # shifted 10 days
    assert "[EMAIL]" in redacted_text
    assert "bob@gmail.com" not in redacted_text


def test_hmac_pseudonymization_determinism():
    """
    Verify pseudonymize_value behaves deterministically.
    - Given the same value and same salt, it generates the exact same token.
    - Given different values or different salts, it generates distinct secure tokens.
    """
    # @req:PRD-TMF-005
    val_1 = "Alice Smith"
    val_2 = "Bob Jones"
    salt_1 = "trial-salt-1"
    salt_2 = "trial-salt-2"

    p1_a = pseudonymize_value(val_1, salt_1)
    p1_b = pseudonymize_value(val_1, salt_1)
    p2 = pseudonymize_value(val_2, salt_1)
    p3 = pseudonymize_value(val_1, salt_2)

    # Same value, same salt -> Deterministic
    assert p1_a == p1_b
    assert len(p1_a) == 64  # Hex-encoded SHA-256 is 64 characters

    # Different value, same salt -> Unique
    assert p1_a != p2

    # Same value, different salt -> Unique
    assert p1_a != p3


def test_date_shifting_and_edge_cases():
    """
    Verify date parsing, shifting, format preservation, and invalid format handling.
    """
    # @req:PRD-TMF-005
    # Standard ISO format shifting
    assert shift_date_string("2026-05-15", 365) == "2027-05-15"
    assert shift_date_string("2026-05-15", -5) == "2026-05-10"

    # Diverse date formats
    assert shift_date_string("2026/05/15", 10) == "2026/05/25"
    assert shift_date_string("05/15/2026", 10) == "05/25/2026"
    assert shift_date_string("15-May-2026", 5) == "20-May-2026"
    assert shift_date_string("May 15, 2026", 5) == "May 20, 2026"
    assert shift_date_string("May 15 2026", 5) == "May 20 2026"

    # Edge cases: Invalid Date strings return '[DATE_INVALID]'
    assert shift_date_string("invalid-date-string", 10) == "[DATE_INVALID]"
    assert shift_date_string("99-99-9999", 5) == "[DATE_INVALID]"


def test_age_capping_and_edge_cases():
    """
    Verify age capping logic and edge cases.
    - Ages > 89 are capped.
    - Ages <= 89 are left untouched.
    - Handles varied syntax formats gracefully.
    """
    # @req:PRD-TMF-005
    # Capped above 89
    assert cap_age_string("age 95", 89) == "age 89+"
    assert cap_age_string("age of 105", 89) == "age of 89+"
    assert cap_age_string("92 years old", 89) == "89+ years old"
    assert cap_age_string("91yo", 89) == "89+yo"
    assert cap_age_string("91-yo", 89) == "89+-yo"

    # Untouched <= 89
    assert cap_age_string("age 45", 89) == "age 45"
    assert cap_age_string("89 years old", 89) == "89 years old"

    # No numeric value
    assert cap_age_string("very old age", 89) == "very old age"


def test_manifest_tamper_evident_symmetric():
    """
    Verify symmetric redaction manifests:
    - Building manifest from redaction records.
    - Signing and verifying successfully.
    - Verifying that any change to the signed manifest fields (operator,counts,strategies,etc.) fails validation.
    """
    # @req:PRD-TMF-005
    results = [
        DetectionResult(
            category=DetectorCategory.EMAIL, start=0, end=13, value="alice@ihs.gov"
        ),
    ]
    _, record = apply_deid_transforms("alice@ihs.gov", results, default_strategy="mask")

    manifest = build_redaction_manifest(
        redaction_record=record,
        operator_identity="CRA Smith",
        reason="GDPR export verification",
        source_version="v1",
        target_version="v2",
    )

    secret = b"my-secure-symmetric-hmac-key-999"
    signed_manifest = sign_manifest_symmetric(manifest, secret)
    assert signed_manifest.signature is not None

    # Valid check passes
    assert verify_manifest_symmetric(signed_manifest, secret) is True

    # Modified key fails
    assert verify_manifest_symmetric(signed_manifest, b"wrong-key") is False

    # Tampered fields fail verification
    # 1. Tamper operator
    t_operator = signed_manifest.model_copy(deep=True)
    t_operator.operator_identity = "Malicious Operator"
    assert verify_manifest_symmetric(t_operator, secret) is False

    # 2. Tamper counts
    t_counts = signed_manifest.model_copy(deep=True)
    t_counts.categories_counts["email"] = 10
    assert verify_manifest_symmetric(t_counts, secret) is False

    # 3. Tamper strategies
    t_strategies = signed_manifest.model_copy(deep=True)
    t_strategies.strategies["email"] = "pseudonymize"
    assert verify_manifest_symmetric(t_strategies, secret) is False

    # 4. Tamper source version
    t_source = signed_manifest.model_copy(deep=True)
    t_source.source_version = "v3"
    assert verify_manifest_symmetric(t_source, secret) is False

    # 5. Tamper target version
    t_target = signed_manifest.model_copy(deep=True)
    t_target.target_version = "v4"
    assert verify_manifest_symmetric(t_target, secret) is False


def test_manifest_tamper_evident_asymmetric(temp_rsa_keypair):
    """
    Verify asymmetric redaction manifests:
    - Signing with private key.
    - Verifying with public key.
    - Tampering with any signed fields triggers verification failure.
    """
    # @req:PRD-TMF-005
    private_pem, public_pem = temp_rsa_keypair

    results = [
        DetectionResult(
            category=DetectorCategory.SSN_NATIONAL_ID,
            start=0,
            end=11,
            value="123-45-6789",
        ),
    ]
    _, record = apply_deid_transforms("123-45-6789", results, default_strategy="mask")

    manifest = build_redaction_manifest(
        redaction_record=record,
        operator_identity="CRA Jones",
        reason="Regulatory inspector submission",
        source_version="v2",
        target_version="v3",
    )

    signed = sign_manifest_asymmetric(manifest, private_pem)
    assert signed.signature is not None

    # Verification passes
    assert verify_manifest_asymmetric(signed, public_pem) is True

    # Tamper with justification reason fails
    tampered = signed.model_copy(deep=True)
    tampered.reason = "Unapproved reason"
    assert verify_manifest_asymmetric(tampered, public_pem) is False


def test_source_documents_remain_unchanged():
    """
    Assert that source documents remain completely untouched after detections and transformations.
    """
    # @req:PRD-TMF-005
    source_text = "Highly sensitive PII of patient Bob Smith with email bob@clinic.org."
    detector = DeidDetector()

    # Match
    results = detector.detect(
        source_text, profile=ComplianceProfile.HIPAA, custom_terms=["Bob Smith"]
    )

    # Transform
    redacted_text, record = apply_deid_transforms(
        text=source_text,
        results=results,
        default_strategy="mask",
    )

    # Redacted text should be different
    assert redacted_text != source_text
    assert "Bob Smith" not in redacted_text
    assert "bob@clinic.org" not in redacted_text

    # Original text remains completely intact
    assert (
        source_text
        == "Highly sensitive PII of patient Bob Smith with email bob@clinic.org."
    )


def test_no_raw_matched_values_persisted():
    """
    Assert that no raw matched values are returned or stored in manifest summaries, records or details.
    """
    # @req:PRD-TMF-005
    source_text = "Private patient name is John Doe."
    results = [
        DetectionResult(
            category=DetectorCategory.CUSTOM, start=24, end=32, value="John Doe"
        ),
    ]

    # Apply transform
    _, record = apply_deid_transforms(source_text, results, default_strategy="mask")

    # Assert record items do not store the original "value" field or any raw content in replacement or other fields
    assert len(record) == 1
    item = record[0]
    assert item.category == DetectorCategory.CUSTOM
    assert item.strategy == "mask"
    assert item.replacement == "[CUSTOM]"
    # Crucially, inspect dictionary representation to verify John Doe is completely absent
    item_dict = item.model_dump()
    assert "John Doe" not in str(item_dict)

    # Build manifest
    manifest = build_redaction_manifest(
        redaction_record=record,
        operator_identity="CRA Smith",
        reason="Sanitizing patient name",
        source_version="v1",
        target_version="v2",
    )

    # Check manifest model representation and dictionary representation to verify John Doe is completely absent
    manifest_dict = manifest.model_dump()
    assert "John Doe" not in str(manifest_dict)
