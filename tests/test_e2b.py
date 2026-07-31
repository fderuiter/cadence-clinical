"""Unit tests for E2B(R3) ICSR XML rendering and structural validation.

Requirements: PRD-SYS-001
"""

import pytest
from sae_icsr import (
    ICSRHeader,
    ICSRPatient,
    ICSRReactionEvent,
    ICSRReportIdentifiers,
    ICSRSuspectDrug,
    IndividualCaseSafetyReport,
)

from apps.safety.renderer import render_icsr_to_xml
from apps.safety.validator import validate_icsr_xml as validate_e2b_xml_structure


def get_valid_icsr_payload() -> IndividualCaseSafetyReport:
    """Helper to construct a valid, fully populated IndividualCaseSafetyReport."""
    header = ICSRHeader(
        sender_organization="CADENCE-CLINICAL",
        receiver_organization="PV-GATEWAY",
        transmission_date="2026-08-28T12:00:00Z",
        message_id="MSG-SAE-20260828-001",
    )
    report_identifiers = ICSRReportIdentifiers(
        worldwide_unique_case_id="WW-SPONSOR-2026001",
        local_report_id="LOCAL-RPT-123",
        first_sender_type="SPONSOR",
    )
    patient = ICSRPatient(
        patient_id="SUBJ-101",
        sex="female",
        age=32.0,
        age_unit="years",
        birth_date="1994-06-15",
    )
    reactions = [
        ICSRReactionEvent(
            reaction_term="Anaphylactic Reaction",
            seriousness_death=False,
            seriousness_life_threatening=True,
            seriousness_hospitalization="yes",
        )
    ]
    suspect_drugs = [
        ICSRSuspectDrug(
            drug_name="Cadence-Investigational-Compound",
            active_substance_name="Cadencin",
            dosage_text="25mg BID",
            route_of_administration="ORAL",
            action_taken_with_drug="DRUG WITHDRAWN",
            drug_role="suspect",
        )
    ]

    return IndividualCaseSafetyReport(
        header=header,
        report_identifiers=report_identifiers,
        patient=patient,
        reactions=reactions,
        suspect_drugs=suspect_drugs,
        version_index=1,
    )


def test_valid_icsr_rendering_and_validation_structure() -> None:
    """Verify that a valid ICSR produces XML that passes structural validation.

    Requirements: PRD-SYS-001
    """
    icsr = get_valid_icsr_payload()
    xml_content = render_icsr_to_xml(icsr)

    # Basic substring assertions for XML rendering
    assert "<?xml" in xml_content
    assert 'xmlns="urn:hl7-org:v3"' in xml_content
    assert "<sender_organization>CADENCE-CLINICAL</sender_organization>" in xml_content
    assert (
        "<worldwide_unique_case_id>WW-SPONSOR-2026001</worldwide_unique_case_id>"
        in xml_content
    )
    assert "<reaction_term>Anaphylactic Reaction</reaction_term>" in xml_content

    # Structural validation check using imported validator
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is True
    assert "Structure matches official" in msg


def test_icsr_version_and_reason_for_change_rendering_structure() -> None:
    """Verify version index and reason for change rendering and validation.

    Requirements: PRD-SYS-001
    """
    icsr = get_valid_icsr_payload()
    icsr.version_index = 3
    icsr.reason_for_change = "Follow-up information added on patient recovery status"

    xml_content = render_icsr_to_xml(icsr)
    assert 'version_index="3"' in xml_content
    assert 'reason_for_change="Follow-up information added on patient recovery status"' in xml_content

    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is True


def test_malformed_xml_fails_validation() -> None:
    """Verify that structurally malformed XML fails with a parsing error.

    Requirements: PRD-SYS-001
    """
    xml_content = "<ichicsr xmlns='urn:hl7-org:v3'><header><message_id>123</message_id>"  # Missing closing tags
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "XML parsing error" in msg


def test_invalid_root_tag_fails_validation() -> None:
    """Verify validation fails if root tag is incorrect.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <invalid_root xmlns="urn:hl7-org:v3">
    </invalid_root>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Invalid root element" in msg


def test_invalid_namespace_fails_validation() -> None:
    """Verify validation fails if namespace is incorrect.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:invalid-hl7-namespace">
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Invalid root element" in msg


def test_missing_header_fails_validation() -> None:
    """Verify validation fails when header element is missing.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <safety_report>
            <worldwide_unique_case_id>WW-12345</worldwide_unique_case_id>
        </safety_report>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'header'" in msg


def test_missing_header_fields_fail_validation() -> None:
    """Verify validation fails when mandatory header sub-elements are missing or empty.

    Requirements: PRD-SYS-001
    """
    fields = [
        "message_id",
        "sender_organization",
        "receiver_organization",
        "transmission_date",
    ]
    for field in fields:
        header_vals = {
            "message_id": "MSG-999",
            "sender_organization": "SENDER-A",
            "receiver_organization": "RECEIVER-B",
            "transmission_date": "2026-08-28T12:00:00Z",
        }
        header_vals[field] = ""  # Force empty field

        xml_content = f"""<?xml version="1.0" encoding="utf-8"?>
        <ichicsr xmlns="urn:hl7-org:v3">
            <header>
                <message_id>{header_vals["message_id"]}</message_id>
                <sender_organization>{header_vals["sender_organization"]}</sender_organization>
                <receiver_organization>{header_vals["receiver_organization"]}</receiver_organization>
                <transmission_date>{header_vals["transmission_date"]}</transmission_date>
                <message_type>ICHICSR</message_type>
            </header>
            <safety_report>
                <worldwide_unique_case_id>WW-12345</worldwide_unique_case_id>
            </safety_report>
        </ichicsr>"""
        is_valid, msg = validate_e2b_xml_structure(xml_content)
        assert is_valid is False
        assert f"Missing or empty mandatory message identifier '{field}'" in msg


def test_missing_safety_report_fails_validation() -> None:
    """Verify validation fails when safety_report element is missing.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'safety_report'" in msg


def test_missing_worldwide_unique_case_id_fails_validation() -> None:
    """Verify validation fails when worldwide_unique_case_id is empty or missing.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>   </worldwide_unique_case_id>
        </safety_report>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Missing or empty mandatory identifier 'worldwide_unique_case_id'" in msg


def test_missing_patient_fails_validation() -> None:
    """Verify validation fails when patient block is missing entirely.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>WW-001</worldwide_unique_case_id>
        </safety_report>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'patient'" in msg


def test_missing_patient_fields_fail_validation() -> None:
    """Verify validation fails when mandatory patient attributes (patient_id, sex) are empty.

    Requirements: PRD-SYS-001
    """
    fields = ["patient_id", "sex"]
    for field in fields:
        patient_vals = {"patient_id": "SUBJ-999", "sex": "M"}
        patient_vals[field] = ""  # Empty it

        xml_content = f"""<?xml version="1.0" encoding="utf-8"?>
        <ichicsr xmlns="urn:hl7-org:v3">
            <header>
                <message_id>MSG-001</message_id>
                <sender_organization>SENDER</sender_organization>
                <receiver_organization>RECEIVER</receiver_organization>
                <transmission_date>2026-08-28T12:00:00Z</transmission_date>
            </header>
            <safety_report>
                <worldwide_unique_case_id>WW-001</worldwide_unique_case_id>
            </safety_report>
            <patient>
                <patient_id>{patient_vals["patient_id"]}</patient_id>
                <sex>{patient_vals["sex"]}</sex>
            </patient>
        </ichicsr>"""
        is_valid, msg = validate_e2b_xml_structure(xml_content)
        assert is_valid is False
        assert f"Missing or empty mandatory patient attribute '{field}'" in msg


def test_missing_reactions_block_fails_validation() -> None:
    """Verify validation fails when reactions block is missing entirely.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>WW-001</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUB-123</patient_id>
            <sex>M</sex>
        </patient>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'reactions'" in msg


def test_empty_reactions_fails_validation() -> None:
    """Verify validation fails when reaction list is present but empty.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>WW-001</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUB-123</patient_id>
            <sex>M</sex>
        </patient>
        <reactions></reactions>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "at least one reaction is required" in msg


def test_empty_reaction_term_fails_validation() -> None:
    """Verify validation fails when reaction_term is missing or empty inside a reaction.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>WW-001</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUB-123</patient_id>
            <sex>M</sex>
        </patient>
        <reactions>
            <reaction>
                <reaction_term>  </reaction_term>
            </reaction>
        </reactions>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Missing or empty mandatory attribute 'reaction_term'" in msg


def test_missing_suspect_drugs_block_fails_validation() -> None:
    """Verify validation fails when suspect drugs block is missing entirely.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>WW-001</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUB-123</patient_id>
            <sex>M</sex>
        </patient>
        <reactions>
            <reaction>
                <reaction_term>Nausea</reaction_term>
            </reaction>
        </reactions>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'suspect_drugs'" in msg


def test_empty_suspect_drugs_fails_validation() -> None:
    """Verify validation fails when suspect drug list is present but empty.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>WW-001</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUB-123</patient_id>
            <sex>M</sex>
        </patient>
        <reactions>
            <reaction>
                <reaction_term>Nausea</reaction_term>
            </reaction>
        </reactions>
        <suspect_drugs></suspect_drugs>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "at least one suspect drug is required" in msg


def test_empty_drug_name_fails_validation() -> None:
    """Verify validation fails when drug_name is missing or empty inside suspect_drug.

    Requirements: PRD-SYS-001
    """
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>MSG-001</message_id>
            <sender_organization>SENDER</sender_organization>
            <receiver_organization>RECEIVER</receiver_organization>
            <transmission_date>2026-08-28T12:00:00Z</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>WW-001</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUB-123</patient_id>
            <sex>M</sex>
        </patient>
        <reactions>
            <reaction>
                <reaction_term>Nausea</reaction_term>
            </reaction>
        </reactions>
        <suspect_drugs>
            <suspect_drug>
                <drug_name>   </drug_name>
                <drug_role>SUSPECT</drug_role>
            </suspect_drug>
        </suspect_drugs>
    </ichicsr>"""
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    assert is_valid is False
    assert "Missing or empty mandatory attribute 'drug_name'" in msg
