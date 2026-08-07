"""Unit test suite for ICH E2B(R3) XML safety report parser.

Requirements: PRD-SYS-001
"""

from execution.safety_models import CausalityEnum, SeriousnessCriteriaEnum

import packages  # noqa: F401
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
