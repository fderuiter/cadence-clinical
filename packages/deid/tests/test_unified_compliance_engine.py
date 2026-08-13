from packages.deid.detector import DeidDetector, redact_text
from packages.deid.models import ComplianceProfile, DetectorCategory
from packages.deid.ner_scrubber import PHINameEntityScrubber


def test_new_hipaa_categories_redaction():
    """
    Validate health plan beneficiary, license, vehicle, and device numbers are detected and redacted.
    """
    detector = DeidDetector()

    # 1. Health plan beneficiary
    text_hpb = "The Medicare beneficiary ID is HICN-987654321A."
    results = detector.detect(text_hpb, profile=ComplianceProfile.HIPAA)
    hpb_matches = [
        r for r in results if r.category == DetectorCategory.HEALTH_PLAN_BENEFICIARY
    ]
    assert len(hpb_matches) == 1
    assert "HICN-987654321A" in hpb_matches[0].value

    redacted = redact_text(text_hpb, results)
    assert "[HEALTH_PLAN_BENEFICIARY]" in redacted
    assert "HICN-987654321A" not in redacted

    # 2. Certificate/license number
    text_lic = "State License Number: LIC-123456789."
    results_lic = detector.detect(text_lic, profile=ComplianceProfile.HIPAA)
    lic_matches = [
        r for r in results_lic if r.category == DetectorCategory.CERTIFICATE_LICENSE
    ]
    assert len(lic_matches) == 1
    assert "LIC-123456789" in lic_matches[0].value

    redacted_lic = redact_text(text_lic, results_lic)
    assert "[CERTIFICATE_LICENSE]" in redacted_lic
    assert "LIC-123456789" not in redacted_lic

    # 3. Vehicle identifiers
    text_vin = "The investigator noted vehicle VIN: 1HGCR2F83HA123456."
    results_vin = detector.detect(text_vin, profile=ComplianceProfile.HIPAA)
    vin_matches = [
        r for r in results_vin if r.category == DetectorCategory.VEHICLE_IDENTIFIERS
    ]
    assert len(vin_matches) == 1
    assert "1HGCR2F83HA123456" in vin_matches[0].value

    redacted_vin = redact_text(text_vin, results_vin)
    assert "[VEHICLE_IDENTIFIERS]" in redacted_vin
    assert "1HGCR2F83HA123456" not in redacted_vin

    # 4. Device serial number
    text_dev = "The patient's device serial is UDI: SN-998822."
    results_dev = detector.detect(text_dev, profile=ComplianceProfile.HIPAA)
    dev_matches = [
        r for r in results_dev if r.category == DetectorCategory.DEVICE_SERIAL
    ]
    assert len(dev_matches) == 1
    assert "SN-998822" in dev_matches[0].value

    redacted_dev = redact_text(text_dev, results_dev)
    assert "[DEVICE_SERIAL]" in redacted_dev
    assert "SN-998822" not in redacted_dev


def test_leading_non_word_phone_email_redaction():
    """
    Validate phone numbers starting with leading plus signs or parenthesis and emails are fully redacted.
    """
    scrubber = PHINameEntityScrubber()

    text = "Please reach us at +(555) 019-9999 or call +1-555-123-4567 or write to -patient@hospital.org."
    redacted = scrubber.scrub_phi(text)

    # Check that leading symbols are not exposed
    assert "+(" not in redacted
    assert "+1-" not in redacted
    assert "-p" not in redacted
    assert "[REDACTED_PHONE]" in redacted
    assert "[REDACTED_EMAIL]" in redacted


def test_trailing_punctuation_clinical_identifiers():
    """
    Validate patient identifiers with trailing hyphens or slashes are redacted completely.
    """
    scrubber = PHINameEntityScrubber()

    # Trailing hyphen
    text_hyphen = "Subject MRN is MRN-123456- and SSN is 123-45-6789/."
    redacted = scrubber.scrub_phi(text_hyphen)

    assert "MRN-123456-" not in redacted
    assert "123-45-6789/" not in redacted
    assert "[REDACTED_MRN]" in redacted
    assert "[REDACTED_SSN]" in redacted
    # Ensure there are no leftover trailing symbols
    assert "- and" not in redacted
    assert "/." not in redacted


def test_embedded_custom_terms_redaction():
    """
    Validate custom literal names are matched and redacted when embedded inside other complex clinical text.
    """
    detector = DeidDetector()

    text = "The target patient is JohnDoe, affiliated with AlanTuring-group."
    results = detector.detect(
        text, profile=ComplianceProfile.HIPAA, custom_terms=["John", "Alan Turing"]
    )

    # "John" embedded inside "JohnDoe" should match
    customs = [r for r in results if r.category == DetectorCategory.CUSTOM]
    assert len(customs) >= 1
    assert "John" in [c.value for c in customs]

    redacted = redact_text(text, results)
    assert "John" not in redacted
    assert "[CUSTOM]Doe" in redacted


def test_legacy_passthrough_parity():
    """
    Validate that the legacy scrubber behaves as a pure pass-through producing identical redacted outputs.
    """
    detector = DeidDetector()
    scrubber = PHINameEntityScrubber()

    text = "Subject MRN is MRN-123456. License Number: LIC-98765. Phone is +1 (555) 019-9999."

    results = detector.detect(text, profile=ComplianceProfile.HIPAA)
    core_redacted = redact_text(text, results)

    legacy_redacted = scrubber.scrub_phi(text)

    # Replace category upper names with REDACTED_ equivalents to compare
    norm_core = (
        core_redacted.replace("[MEDICAL_RECORD_ACCOUNT]", "[REDACTED_MRN]")
        .replace("[CERTIFICATE_LICENSE]", "[REDACTED_CERTIFICATE_LICENSE]")
        .replace("[TELEPHONE_FAX]", "[REDACTED_PHONE]")
    )

    assert norm_core == legacy_redacted
