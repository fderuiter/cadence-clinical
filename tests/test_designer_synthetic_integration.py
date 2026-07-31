"""Integration test suite utilizing synthetic, schema-compliant USDM study payloads.

This suite verifies nested narrative blocks, study objectives parsing, multi-node
circular skip logic visibility detection, non-dictionary components, and
invalid original identifiers.

Requirements: PRD-MDR-007, PRD-EDC-003, PRD-SYS-001
"""

import pytest
import usdm_model

from apps.designer.content_assembly import assemble_narrative_sections
from apps.designer.usdm_ingestion import (
    CircularDependencyError,
    validate_usdm_payload,
)


def test_synthetic_payload_narrative_and_eligibility_parsing():
    """Verify that a synthetic, PII-free clinical trial payload is parsed.

    The test confirms nested narrative blocks resolve references and that eligibility
    requirements remain fully intact.

    Requirements: PRD-MDR-007
    """
    synthetic_payload = """
id: 00000000-0000-0000-0000-000000000001
name: Synthetic Clinical Study Protocol
instanceType: Study
versions:
  - id: 00000000-0000-0000-0000-000000000002
    versionIdentifier: "1.0"
    rationale: "Synthetic trial design for validation"
    studyIdentifiers: []
    titles: []
    instanceType: StudyVersion
    narrativeContentItems:
      - id: 00000000-0000-0000-0000-000000000003
        name: "Trial Background"
        text: "This trial intends to evaluate our synthetic molecule in healthy adults. The main goal is <usdm:ref id='00000000-0000-0000-0000-000000000007' attribute='text' klass='Objective'/>. Eligibility includes <usdm:ref id='00000000-0000-0000-0000-000000000008' attribute='description' klass='EligibilityCriterion'/>."
        instanceType: NarrativeContentItem
    studyDesigns:
      - id: 00000000-0000-0000-0000-000000000004
        instanceType: InterventionalStudyDesign
        name: "Synthetic Interventional Design"
        rationale: "Efficacy validation"
        studyPhase:
          id: 00000000-0000-0000-0000-000000000005a
          instanceType: AliasCode
          standardCode:
            id: 00000000-0000-0000-0000-000000000005
            code: "PHASE_I"
            codeSystem: "USDM"
            codeSystemVersion: "1.0"
            decode: "Phase I"
            instanceType: Code
        studyType:
          id: 00000000-0000-0000-0000-000000000006
          code: "INTERVENTIONAL"
          codeSystem: "USDM"
          codeSystemVersion: "1.0"
          decode: "Interventional"
          instanceType: Code
        arms: []
        studyCells: []
        epochs: []
        model:
          id: 00000000-0000-0000-0000-000000000006b
          code: "PARALLEL"
          codeSystem: "USDM"
          codeSystemVersion: "1.0"
          decode: "Parallel"
          instanceType: Code
        population:
          id: 00000000-0000-0000-0000-000000000015
          name: "Healthy volunteers"
          description: "Healthy volunteers aged 18 to 65."
          includesHealthySubjects: true
          instanceType: StudyDesignPopulation
        objectives:
          - id: 00000000-0000-0000-0000-000000000007
            name: "Obj 1"
            text: "To evaluate the safety and tolerability of synthetic compound X."
            level:
              id: 00000000-0000-0000-0000-000000000016
              code: "PRIMARY"
              codeSystem: "USDM"
              codeSystemVersion: "1.0"
              decode: "Primary"
              instanceType: Code
            instanceType: Objective
        eligibilityCriteria:
          - id: 00000000-0000-0000-0000-000000000008
            name: "Synthetic Age Requirement"
            criterionType: "Inclusion"
            description: "Participant must be at least 18 years of age."
            category:
              id: 00000000-0000-0000-0000-000000000017
              code: "ELIGIBILITY"
              codeSystem: "USDM"
              codeSystemVersion: "1.0"
              decode: "Eligibility"
              instanceType: Code
            identifier: "crit-synthetic-1"
            criterionItemId: "crit-synthetic-item-1"
            instanceType: EligibilityCriterion
documentedBy:
  - id: 00000000-0000-0000-0000-000000000009
    name: "Synthetic Protocol Document"
    templateName: "Standard Template"
    instanceType: StudyDefinitionDocument
    language:
      id: 00000000-0000-0000-0000-000000000010
      code: "en"
      codeSystem: "ISO"
      codeSystemVersion: "639"
      decode: "English"
      instanceType: Code
    type:
      id: 00000000-0000-0000-0000-000000000011
      code: "PROTOCOL"
      codeSystem: "USDM"
      codeSystemVersion: "1.0"
      decode: "Protocol"
      instanceType: Code
    versions:
      - id: 00000000-0000-0000-0000-000000000012
        version: "1"
        status:
          id: 00000000-0000-0000-0000-000000000013
          code: "APPROVED"
          codeSystem: "USDM"
          codeSystemVersion: "1.0"
          decode: "Approved"
          instanceType: Code
        contents:
          - id: 00000000-0000-0000-0000-000000000014
            name: "Synthetic Introduction"
            sectionNumber: "1.0"
            sectionTitle: "Introduction Section"
            displaySectionNumber: true
            displaySectionTitle: true
            childIds: ["00000000-0000-0000-0000-000000000003"]
            instanceType: NarrativeContent
        instanceType: StudyDefinitionDocumentVersion
"""
    # 1. Run ingestion validation
    report = validate_usdm_payload(synthetic_payload)
    assert report.validity is True
    assert len(report.errors) == 0

    # 2. Build Study object and run content assembly to verify resolve_text_references
    study_dict = yaml_to_dict_helper(synthetic_payload)
    study = usdm_model.Study(**study_dict)

    sections = assemble_narrative_sections(study)
    assert len(sections) == 1

    intro_sec = sections[0]
    assert intro_sec.section_id == "00000000-0000-0000-0000-000000000014"
    assert len(intro_sec.items) == 1

    narrative_item = intro_sec.items[0]
    resolved_text = narrative_item.text

    # Assert successfully parsed and fully resolved nested blocks
    assert (
        "To evaluate the safety and tolerability of synthetic compound X."
        in resolved_text
    )
    assert "Participant must be at least 18 years of age." in resolved_text


def test_circular_skip_logic_triggers_exception():
    """Verify that multi-node dependency loops trigger validation exceptions.

    Requirements: PRD-EDC-003
    """
    circular_payload = """
id: 00000000-0000-0000-0000-000000000001
name: Circular Rules Study
instanceType: Study
versions:
  - id: 00000000-0000-0000-0000-000000000002
    versionIdentifier: "1.0"
    rationale: "Initial Version"
    instanceType: StudyVersion
    studyDesigns:
      - id: 00000000-0000-0000-0000-000000000003
        instanceType: InterventionalStudyDesign
        name: "Design"
        studyCells: []
        rationale: "None"
        epochs: []
        arms: []
        activities:
          - id: 00000000-0000-0000-0000-000000000004
            name: Field 1
            instanceType: Activity
            rules:
              - id: rule-1
                type: skip_logic
                target_field: 00000000-0000-0000-0000-000000000004
                action: hide
                condition:
                  type: field_ref
                  field_ref:
                    field_id: 00000000-0000-0000-0000-000000000005
          - id: 00000000-0000-0000-0000-000000000005
            name: Field 2
            instanceType: Activity
            rules:
              - id: rule-2
                type: skip_logic
                target_field: 00000000-0000-0000-0000-000000000005
                action: hide
                condition:
                  type: field_ref
                  field_ref:
                    field_id: 00000000-0000-0000-0000-000000000004
"""
    with pytest.raises(CircularDependencyError) as exc_info:
        validate_usdm_payload(circular_payload, raise_on_error=True)

    assert "Circular skip-logic dependency detected" in str(exc_info.value)


def test_ingestion_logic_catches_non_dictionary_and_invalid_identifiers():
    """Verify that non-dictionary components and invalid identifiers are caught.

    Requirements: PRD-SYS-001
    """
    invalid_payload_spaces = """
id: "study synthetic space id"
name: Invalid Payload Study
instanceType: Study
versions:
  - id: "00000000-0000-0000-0000-000000000002"
    versionIdentifier: "1.0"
    rationale: "Initial Version"
    instanceType: StudyVersion
    studyDesigns:
      - "non-dictionary design component"
"""
    report = validate_usdm_payload(invalid_payload_spaces)
    assert report.validity is False

    # Assert invalid original identifier error is reported
    assert any("Invalid original identifier" in err.reason for err in report.errors)
    assert any("study synthetic space id" in err.reason for err in report.errors)

    # Assert non-dictionary component is reported
    assert any("Expected dictionary component" in err.reason for err in report.errors)
    assert any("studyDesigns[0]" in err.field for err in report.errors)


def test_usdm_ingestion_parsing_and_structural_failures():
    """Verify all parser exceptions and missing design element validation branches.

    Requirements: PRD-SYS-001
    """
    # 1. Empty payload
    report_empty = validate_usdm_payload("   ")
    assert report_empty.validity is False
    assert "Empty payload" in report_empty.errors[0].reason

    # 2. JSON payload not a dict
    report_json_list = validate_usdm_payload("[]")
    assert report_json_list.validity is False
    assert "JSON payload must be a dictionary" in report_json_list.errors[0].reason

    # 3. YAML payload not a dict
    report_yaml_list = validate_usdm_payload("- hello\n- world")
    assert report_yaml_list.validity is False
    assert "Parsed payload is not a dictionary" in report_yaml_list.errors[0].reason

    # 4. JSON parsing failed syntax error
    report_broken = validate_usdm_payload('{"invalid"::}')
    assert report_broken.validity is False
    assert "Format parsing error" in report_broken.errors[0].reason

    # 5. Invalid regex character in identifier
    report_regex_id = validate_usdm_payload("""
id: "study$invalid@char"
name: Regex Violation Study
instanceType: Study
""")
    assert report_regex_id.validity is False
    assert any(
        "Invalid original identifier" in err.reason for err in report_regex_id.errors
    )

    # 6. Missing mandatory version ID
    report_missing_ver_id = validate_usdm_payload("""
id: 00000000-0000-0000-0000-000000000001
name: Test Missing Version ID
instanceType: Study
versions:
  - versionIdentifier: "1.0"
    rationale: "Initial"
    instanceType: StudyVersion
""")
    assert report_missing_ver_id.validity is False
    assert any(
        "Missing mandatory study version element: 'id'" in err.reason
        for err in report_missing_ver_id.errors
    )

    # 7. Missing mandatory design ID and design name
    report_missing_design = validate_usdm_payload("""
id: 00000000-0000-0000-0000-000000000001
name: Test Missing Design Details
instanceType: Study
versions:
  - id: 00000000-0000-0000-0000-000000000002
    versionIdentifier: "1.0"
    rationale: "Initial"
    instanceType: StudyVersion
    studyDesigns:
      - instanceType: InterventionalStudyDesign
""")
    assert report_missing_design.validity is False
    assert any(
        "Missing mandatory study design element: 'id'" in err.reason
        for err in report_missing_design.errors
    )
    assert any(
        "Missing mandatory study design element: 'name'" in err.reason
        for err in report_missing_design.errors
    )

    # 8. Missing mandatory study arm ID and name
    report_missing_arm = validate_usdm_payload("""
id: 00000000-0000-0000-0000-000000000001
name: Test Missing Arm Details
instanceType: Study
versions:
  - id: 00000000-0000-0000-0000-000000000002
    versionIdentifier: "1.0"
    rationale: "Initial"
    instanceType: StudyVersion
    studyDesigns:
      - id: 00000000-0000-0000-0000-000000000003
        name: "Design"
        instanceType: InterventionalStudyDesign
        arms:
          - instanceType: StudyArm
""")
    assert report_missing_arm.validity is False
    assert any(
        "Missing mandatory study arm element: 'id'" in err.reason
        for err in report_missing_arm.errors
    )
    assert any(
        "Missing mandatory study arm element: 'name'" in err.reason
        for err in report_missing_arm.errors
    )

    # 9. Missing mandatory study epoch ID and name
    report_missing_epoch = validate_usdm_payload("""
id: 00000000-0000-0000-0000-000000000001
name: Test Missing Epoch Details
instanceType: Study
versions:
  - id: 00000000-0000-0000-0000-000000000002
    versionIdentifier: "1.0"
    rationale: "Initial"
    instanceType: StudyVersion
    studyDesigns:
      - id: 00000000-0000-0000-0000-000000000003
        name: "Design"
        instanceType: InterventionalStudyDesign
        epochs:
          - instanceType: StudyEpoch
""")
    assert report_missing_epoch.validity is False
    assert any(
        "Missing mandatory study epoch element: 'id'" in err.reason
        for err in report_missing_epoch.errors
    )
    assert any(
        "Missing mandatory study epoch element: 'name'" in err.reason
        for err in report_missing_epoch.errors
    )


def test_usdm_ingestion_additional_validation_coverage():
    """Verify other uncovered branches in usdm_ingestion parsing.

    Requirements: PRD-SYS-001
    """
    # 1. Study root element missing id or name entirely
    report = validate_usdm_payload("""
instanceType: Study
""")
    assert report.validity is False
    assert any(
        "Missing mandatory study root element: 'id'" in err.reason
        for err in report.errors
    )
    assert any(
        "Missing mandatory study root element: 'name'" in err.reason
        for err in report.errors
    )

    # 2. Version and rule elements with non-dictionary elements
    report2 = validate_usdm_payload("""
id: study-1
name: Study 1
instanceType: Study
rules:
  - "non-dict-rule"
versions:
  - "non-dict-version"
""")
    assert report2.validity is False
    assert any(
        "Expected dictionary component at versions[0]" in err.reason
        for err in report2.errors
    )

    # 3. studyDesigns list has a non-dictionary element, triggering nested checks bypass
    report3 = validate_usdm_payload("""
id: study-1
name: Study 1
instanceType: Study
versions:
  - id: ver-1
    versionIdentifier: "1"
    instanceType: StudyVersion
    studyDesigns:
      - "non-dict-design"
""")
    assert report3.validity is False
    assert any(
        "Expected dictionary component at studyDesigns[0]" in err.reason
        for err in report3.errors
    )

    # 4. arms, epochs, encounters, activities list with non-dictionary elements
    report4 = validate_usdm_payload("""
id: study-1
name: Study 1
instanceType: Study
versions:
  - id: ver-1
    versionIdentifier: "1"
    instanceType: StudyVersion
    studyDesigns:
      - id: design-1
        name: Design 1
        instanceType: InterventionalStudyDesign
        arms:
          - "non-dict-arm"
        epochs:
          - "non-dict-epoch"
        encounters:
          - "non-dict-encounter"
        activities:
          - "non-dict-activity"
""")
    assert report4.validity is False
    assert any(
        "Expected dictionary component at arms[0]" in err.reason
        for err in report4.errors
    )
    assert any(
        "Expected dictionary component at epochs[0]" in err.reason
        for err in report4.errors
    )
    assert any(
        "Expected dictionary component at encounters[0]" in err.reason
        for err in report4.errors
    )
    assert any(
        "Expected dictionary component at activities[0]" in err.reason
        for err in report4.errors
    )

    # 5. Stochastic operators in rules condition
    report5 = validate_usdm_payload("""
id: study-1
name: Study 1
instanceType: Study
versions:
  - id: ver-1
    versionIdentifier: "1"
    instanceType: StudyVersion
    studyDesigns:
      - id: design-1
        name: Design 1
        instanceType: InterventionalStudyDesign
        activities:
          - id: act-1
            name: Act 1
            instanceType: Activity
            rules:
              - id: rule-1
                type: skip_logic
                target_field: act-1
                action: hide
                condition:
                  operator: "STOCHASTIC_RANDOM"
""")
    assert report5.validity is False
    assert any(
        "Unsupported or complex operator/function" in err.reason
        for err in report5.errors
    )

    # 6. studyVersions key present (ignored, skips warning)
    report6 = validate_usdm_payload("""
id: study-1
name: Study 1
instanceType: Study
studyVersions: []
""")
    # It will warn about other missing fields/Pydantic validation, but should not have warning for studyVersions
    assert not any("studyVersions" in (warn.field or "") for warn in report6.warnings)


def yaml_to_dict_helper(yaml_str: str) -> dict:
    """Helper to convert YAML string to dict.

    Args:
        yaml_str: The raw YAML string.

    Returns:
        The parsed dict.
    """
    import yaml

    return yaml.safe_load(yaml_str)
