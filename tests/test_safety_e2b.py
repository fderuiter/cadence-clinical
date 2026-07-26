from sae_icsr import (
    ICSRHeader,
    ICSRPatient,
    ICSRReactionEvent,
    ICSRReportIdentifiers,
    ICSRSuspectDrug,
    IndividualCaseSafetyReport,
)

from apps.safety.renderer import render_icsr_to_xml
from apps.safety.validator import validate_icsr_xml


def get_valid_icsr() -> IndividualCaseSafetyReport:
    """Helper to construct a valid, fully populated IndividualCaseSafetyReport."""
    header = ICSRHeader(
        sender_organization="SPONSOR_A",
        receiver_organization="FDA",
        transmission_date="2026-07-25T15:00:00Z",
        message_id="MSG-20260725-001",
    )
    report_identifiers = ICSRReportIdentifiers(
        worldwide_unique_case_id="US-SPONSOR_A-2026000001",
        local_report_id="LOC-1234",
        first_sender_type="SPONSOR",
    )
    patient = ICSRPatient(
        patient_id="SUBJ-001",
        sex="female",
        age=45.5,
        age_unit="years",
        birth_date="1981-01-15",
    )
    reactions = [
        ICSRReactionEvent(
            reaction_term="Anaphylactic shock",
            seriousness_death=False,
            seriousness_life_threatening=True,
            seriousness_hospitalization="yes",
        )
    ]
    suspect_drugs = [
        ICSRSuspectDrug(
            drug_name="Cadence-Trial-Drug",
            active_substance_name="Cadencium",
            dosage_text="10mg QD",
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


def test_valid_icsr_rendering_and_validation():
    """Verify that a valid ICSR produces XML that passes structural validation."""
    icsr = get_valid_icsr()
    xml_content = render_icsr_to_xml(icsr)

    assert "<?xml" in xml_content
    assert 'xmlns="urn:hl7-org:v3"' in xml_content
    assert "<sender_organization>SPONSOR_A</sender_organization>" in xml_content
    assert (
        "<worldwide_unique_case_id>US-SPONSOR_A-2026000001</worldwide_unique_case_id>"
        in xml_content
    )
    assert "<reaction_term>Anaphylactic shock</reaction_term>" in xml_content

    # Validate structural correctness
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is True
    assert "Structure matches official" in msg


def test_icsr_version_and_reason_for_change_rendering():
    """Verify that version index and reason for change are correctly rendered."""
    icsr = get_valid_icsr()
    icsr.version_index = 2
    icsr.reason_for_change = "Follow-up test reason"

    xml_content = render_icsr_to_xml(icsr)
    assert 'version_index="2"' in xml_content
    assert 'reason_for_change="Follow-up test reason"' in xml_content

    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is True


def test_malformed_xml_validation_fails():
    """Verify that malformed/invalid XML fails safely with a clear parsing error."""
    xml_content = "<ichicsr xmlns='urn:hl7-org:v3'><header><message_id>123</message_id></header>"  # Unclosed tag
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "XML parsing error" in msg


def test_invalid_root_tag_fails():
    """Verify that validation fails if root tag is incorrect."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <other_root xmlns="urn:hl7-org:v3">
    </other_root>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "Invalid root element" in msg


def test_invalid_namespace_fails():
    """Verify that validation fails if namespace is incorrect."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:other-namespace">
    </ichicsr>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "Invalid root element" in msg


def test_missing_header_fails():
    """Verify that validation fails when header is missing."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <safety_report>
            <worldwide_unique_case_id>123</worldwide_unique_case_id>
        </safety_report>
    </ichicsr>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'header'" in msg


def test_missing_header_fields_fail():
    """Verify that validation fails when mandatory header fields are empty or missing."""
    fields = [
        "message_id",
        "sender_organization",
        "receiver_organization",
        "transmission_date",
    ]
    for field in fields:
        # Create a header with one empty/missing field
        header_vals = {
            "message_id": "MSG-001",
            "sender_organization": "SENDER",
            "receiver_organization": "RECEIVER",
            "transmission_date": "2026-07-25",
        }
        header_vals[field] = ""  # Empty it

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
                <worldwide_unique_case_id>123</worldwide_unique_case_id>
            </safety_report>
        </ichicsr>"""
        is_valid, msg = validate_icsr_xml(xml_content)
        assert is_valid is False
        assert f"Missing or empty mandatory message identifier '{field}'" in msg


def test_missing_safety_report_fails():
    """Verify that validation fails when safety_report is missing."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>123</message_id>
            <sender_organization>S</sender_organization>
            <receiver_organization>R</receiver_organization>
            <transmission_date>2026-07-25</transmission_date>
        </header>
    </ichicsr>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'safety_report'" in msg


def test_missing_worldwide_unique_case_id_fails():
    """Verify that validation fails when worldwide_unique_case_id is empty or missing."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>123</message_id>
            <sender_organization>S</sender_organization>
            <receiver_organization>R</receiver_organization>
            <transmission_date>2026-07-25</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id> </worldwide_unique_case_id>
        </safety_report>
    </ichicsr>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "Missing or empty mandatory identifier 'worldwide_unique_case_id'" in msg


def test_missing_patient_fails():
    """Verify that validation fails when patient is missing."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>123</message_id>
            <sender_organization>S</sender_organization>
            <receiver_organization>R</receiver_organization>
            <transmission_date>2026-07-25</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>123</worldwide_unique_case_id>
        </safety_report>
    </ichicsr>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'patient'" in msg


def test_missing_patient_fields_fail():
    """Verify that validation fails when patient_id or sex is missing/empty."""
    fields = ["patient_id", "sex"]
    for field in fields:
        patient_vals = {"patient_id": "SUBJ-001", "sex": "F"}
        patient_vals[field] = ""  # Empty it

        xml_content = f"""<?xml version="1.0" encoding="utf-8"?>
        <ichicsr xmlns="urn:hl7-org:v3">
            <header>
                <message_id>123</message_id>
                <sender_organization>S</sender_organization>
                <receiver_organization>R</receiver_organization>
                <transmission_date>2026-07-25</transmission_date>
            </header>
            <safety_report>
                <worldwide_unique_case_id>123</worldwide_unique_case_id>
            </safety_report>
            <patient>
                <patient_id>{patient_vals["patient_id"]}</patient_id>
                <sex>{patient_vals["sex"]}</sex>
            </patient>
        </ichicsr>"""
        is_valid, msg = validate_icsr_xml(xml_content)
        assert is_valid is False
        assert f"Missing or empty mandatory patient attribute '{field}'" in msg


def test_missing_reactions_or_reaction_term_fails():
    """Verify that validation fails when reactions are missing or empty."""
    # 1. Missing reactions element entirely
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>123</message_id>
            <sender_organization>S</sender_organization>
            <receiver_organization>R</receiver_organization>
            <transmission_date>2026-07-25</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>123</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUBJ-001</patient_id>
            <sex>F</sex>
        </patient>
    </ichicsr>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'reactions'" in msg

    # 2. Empty reaction list
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>123</message_id>
            <sender_organization>S</sender_organization>
            <receiver_organization>R</receiver_organization>
            <transmission_date>2026-07-25</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>123</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUBJ-001</patient_id>
            <sex>F</sex>
        </patient>
        <reactions></reactions>
    </ichicsr>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "at least one reaction is required" in msg

    # 3. Empty reaction_term in reaction
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>123</message_id>
            <sender_organization>S</sender_organization>
            <receiver_organization>R</receiver_organization>
            <transmission_date>2026-07-25</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>123</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUBJ-001</patient_id>
            <sex>F</sex>
        </patient>
        <reactions>
            <reaction>
                <reaction_term>  </reaction_term>
            </reaction>
        </reactions>
    </ichicsr>"""
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert (
        "Missing or empty mandatory attribute 'reaction_term' in reaction element at index 0"
        in msg
    )


def test_missing_drugs_or_drug_fields_fails():
    """Verify that validation fails when suspect drugs are missing, empty or incomplete."""
    # Base XML with valid headers, patient, and reaction
    base_xml_prefix = """<?xml version="1.0" encoding="utf-8"?>
    <ichicsr xmlns="urn:hl7-org:v3">
        <header>
            <message_id>123</message_id>
            <sender_organization>S</sender_organization>
            <receiver_organization>R</receiver_organization>
            <transmission_date>2026-07-25</transmission_date>
        </header>
        <safety_report>
            <worldwide_unique_case_id>123</worldwide_unique_case_id>
        </safety_report>
        <patient>
            <patient_id>SUBJ-001</patient_id>
            <sex>F</sex>
        </patient>
        <reactions>
            <reaction>
                <reaction_term>Headache</reaction_term>
            </reaction>
        </reactions>"""

    # 1. Missing suspect_drugs element entirely
    xml_content = base_xml_prefix + "</ichicsr>"
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "Missing mandatory element 'suspect_drugs'" in msg

    # 2. Empty suspect_drugs list
    xml_content = base_xml_prefix + "<suspect_drugs></suspect_drugs></ichicsr>"
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert "at least one suspect drug is required" in msg

    # 3. Empty drug_name in suspect_drug
    xml_content = (
        base_xml_prefix
        + """
        <suspect_drugs>
            <suspect_drug>
                <drug_name> </drug_name>
                <drug_role>SUSPECT</drug_role>
            </suspect_drug>
        </suspect_drugs>
    </ichicsr>"""
    )
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert (
        "Missing or empty mandatory attribute 'drug_name' in suspect_drug element at index 0"
        in msg
    )

    # 4. Empty drug_role in suspect_drug
    xml_content = (
        base_xml_prefix
        + """
        <suspect_drugs>
            <suspect_drug>
                <drug_name>DrugA</drug_name>
                <drug_role></drug_role>
            </suspect_drug>
        </suspect_drugs>
    </ichicsr>"""
    )
    is_valid, msg = validate_icsr_xml(xml_content)
    assert is_valid is False
    assert (
        "Missing or empty mandatory attribute 'drug_role' in suspect_drug element at index 0"
        in msg
    )
