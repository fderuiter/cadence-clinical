"""Integration test suite qualifying E2B(R3) XML generation and round-trip parsing.

Requirements: PRD-SYS-001
"""

from datetime import UTC, datetime

from execution.safety_models import (
    CausalityEnum,
    SAECaseRecord,
    SeriousnessCriteriaEnum,
)

import packages  # noqa: F401
from apps.execution.exporters.e2b_xml_builder import E2BR3XMLBuilder
from apps.execution.services.e2b_parser import E2BR3Parser


def test_e2b_xml_generation_and_parser_roundtrip() -> None:
    """Validate E2BR3XMLBuilder generates valid XML that parses cleanly via E2BR3Parser.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(UTC).isoformat()
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
