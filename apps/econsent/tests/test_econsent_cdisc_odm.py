"""Tests for CDISC ODM v1.3.2 / v2.0 XML Generator."""

import defusedxml.ElementTree as ET

from apps.econsent.domain.cdisc_odm import generate_econsent_cdisc_odm_xml


def test_cdisc_odm_generation_structure():
    """Verify CDISC ODM XML elements, metadata version, clinical data, and signature audit items."""
    signatures = [
        {
            "role": "SUBJECT",
            "signer_name": "Alice Cooper",
            "signed_at": "2026-08-15T00:00:00Z",
            "meaning": "I consent to participate in this research",
            "digest_sha256": "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
            "created_by": "patient.alice",
        },
        {
            "role": "INVESTIGATOR",
            "signer_name": "Dr. House, MD",
            "signed_at": "2026-08-15T00:05:00Z",
            "meaning": "Investigator confirmation of consent discussion",
            "digest_sha256": "123456abcdef123456abcdef123456abcdef123456abcdef123456abcdef1234",
            "created_by": "dr.house",
        },
    ]

    granular = [
        {
            "option_code": "OPT_GENOMICS",
            "selected": True,
            "selected_at": "2026-08-15T00:00:00Z",
        },
        {
            "option_code": "OPT_BIOBANK",
            "selected": False,
            "selected_at": "2026-08-15T00:00:00Z",
        },
    ]

    audit_logs = [
        {
            "id": "log-001",
            "timestamp": "2026-08-15T00:00:00Z",
            "actor_id": "patient.alice",
            "actor_role": "patient",
            "action": "CAPTURE_CONSENT",
            "reason_for_change": "Initial sign",
        }
    ]

    xml_str = generate_econsent_cdisc_odm_xml(
        study_id="STUDY-ODM-01",
        subject_pseudonym="SUBJ-ALICE-100",
        template_id="tpl-odm-01",
        template_name="General Oncology Consent",
        protocol_version="v3.0",
        version_index=1,
        signatures=signatures,
        granular_selections=granular,
        audit_logs=audit_logs,
    )

    # Validate well-formed XML
    root = ET.fromstring(xml_str)
    assert root.tag.endswith("ODM")
    assert root.attrib["FileType"] == "Snapshot"
    assert root.attrib["ODMVersion"] == "1.3.2"

    # Verify Study and MetaDataVersion elements
    study = root.find("{http://www.cdisc.org/ns/odm/v1.3}Study")
    assert study is not None
    assert study.attrib["OID"] == "STUDY-ODM-01"

    # Verify ClinicalData and SubjectData
    clinical_data = root.find("{http://www.cdisc.org/ns/odm/v1.3}ClinicalData")
    assert clinical_data is not None
    subj = clinical_data.find("{http://www.cdisc.org/ns/odm/v1.3}SubjectData")
    assert subj is not None
    assert subj.attrib["SubjectKey"] == "SUBJ-ALICE-100"

    # Verify signatures in ItemGroup
    sig_group = clinical_data.find(
        ".//{http://www.cdisc.org/ns/odm/v1.3}ItemGroupData[@ItemGroupOID='IG_CONSENT_SIGNATURES']"
    )
    assert sig_group is not None
    items = sig_group.findall("{http://www.cdisc.org/ns/odm/v1.3}ItemData")
    assert len(items) == 2

    # Verify granular items in ItemGroup
    opt_group = clinical_data.find(
        ".//{http://www.cdisc.org/ns/odm/v1.3}ItemGroupData[@ItemGroupOID='IG_GRANULAR_OPTIONS']"
    )
    assert opt_group is not None
    opt_items = opt_group.findall("{http://www.cdisc.org/ns/odm/v1.3}ItemData")
    assert len(opt_items) == 2
