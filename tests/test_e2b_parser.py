"""Unit test suite for ICH E2B(R3) XML safety report parser and generator round-trip.

Requirements: PRD-SYS-001
"""

from datetime import datetime, timezone
from execution.safety_models import (
    CausalityEnum,
    SAECaseRecord,
    SeriousnessCriteriaEnum,
)

import packages  # noqa: F401
from apps.execution.exporters.e2b_xml_builder import E2BR3XMLBuilder
from apps.execution.services.e2b_parser import E2BR3Parser


def test_parse_e2b_xml_valid_payload() -> None:
    """Validate parsing valid ICH E2B(R3) XML string into SAECaseRecord.

    Requirements: PRD-SYS-001
    """
    xml_sample = """<?xml version="1.0" encoding="UTF-8"?>
    <icsr>
        <safety_report_id>US-SPONSOR-2026-00123</safety_report_id>
        <study_id>STUDY_SAFETY_100</study_id>
        <subject_id>SUB_101</subject_id>
        <reaction_pt>Myocardial Infarction</reaction_pt>
        <meddra_code>10028596</meddra_code>
        <onset_date>2026-07-25</onset_date>
        <seriousness_criteria>HOSPITALIZATION</seriousness_criteria>
        <causality>PROBABLE</causality>
        <expedited>true</expedited>
    </icsr>
    """

    parser = E2BR3Parser()
    case = parser.parse_e2b_xml(xml_sample)

    assert case.safety_report_id == "US-SPONSOR-2026-00123"
    assert case.study_id == "STUDY_SAFETY_100"
    assert case.subject_id == "SUB_101"
    assert case.reaction_pt == "Myocardial Infarction"
    assert case.meddra_code == "10028596"
    assert case.onset_date == "2026-07-25"
    assert case.seriousness_criteria == SeriousnessCriteriaEnum.HOSPITALIZATION
    assert case.causality == CausalityEnum.PROBABLE
    assert case.expedited_reporting_required is True


def test_e2b_xml_generation_and_parser_roundtrip() -> None:
    """Validate E2BR3XMLBuilder generates valid XML that parses cleanly via E2BR3Parser.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(timezone.utc).isoformat()
    original_case = SAECaseRecord(
        case_id="sae_rt_01",
        study_id="study_rt_01",
        subject_id="sub_rt_101",
        safety_report_id="US-RT-2026-0001",
        reaction_pt="Acute Kidney Injury",
        meddra_code="10000853",
        onset_date="2026-07-26",
        seriousness_criteria=SeriousnessCriteriaEnum.HOSPITALIZATION,
        causality=CausalityEnum.CERTAIN,
        expedited_reporting_required=True,
        parsed_at=now_iso,
    )

    builder = E2BR3XMLBuilder()
    generated_xml = builder.build_e2b_xml(original_case)

    assert "<?xml version=" in generated_xml
    assert "<safety_report_id>US-RT-2026-0001</safety_report_id>" in generated_xml
    assert "<reaction_pt>Acute Kidney Injury</reaction_pt>" in generated_xml

    # Parse generated XML back to case model
    parser = E2BR3Parser()
    reparsed_case = parser.parse_e2b_xml(generated_xml)

    assert reparsed_case.safety_report_id == original_case.safety_report_id
    assert reparsed_case.study_id == original_case.study_id
    assert reparsed_case.subject_id == original_case.subject_id
    assert reparsed_case.reaction_pt == original_case.reaction_pt
    assert reparsed_case.meddra_code == original_case.meddra_code
    assert reparsed_case.onset_date == original_case.onset_date
    assert reparsed_case.seriousness_criteria == original_case.seriousness_criteria
    assert reparsed_case.causality == original_case.causality
    assert reparsed_case.expedited_reporting_required is True
