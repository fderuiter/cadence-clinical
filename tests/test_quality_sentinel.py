"""Unit & integration test suite for Protocol Quality Sentinel and site feasibility analyzer.

Requirements: PRD-SYS-001
"""

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.main import app
from apps.designer.services.quality_sentinel import (
    ProtocolQualitySentinel,
    count_syllables_word,
)
from tests.test_synopsis_router import _make_auth_headers

client = TestClient(app)


def test_quality_sentinel_complete_protocol() -> None:
    """Validate evaluating a complete protocol produces a 100.0 quality score.

    Requirements: PRD-SYS-001
    """
    study_payload = {
        "id": "study_complete_001",
        "name": "Complete Study Protocol",
        "studyDesigns": [
            {
                "id": "design_01",
                "objectives": [{"id": "obj_01", "name": "Primary Objective"}],
                "encounters": [{"id": "enc_01"}, {"id": "enc_02"}],
                "activities": [{"id": "act_01"}],
            }
        ],
        "eligibilityCriteria": [
            {"id": "crit_01", "text": "Age >= 18"},
        ],
    }

    sentinel = ProtocolQualitySentinel()
    report = sentinel.evaluate_protocol_quality(study_payload)

    assert report.study_id == "study_complete_001"
    assert report.passed is True
    assert report.quality_score == 100.0
    assert report.patient_burden_index == 5.0  # 2 encounters * 1.5 + 1 act * 2.0 = 5.0
    assert len(report.findings) == 0


def test_quality_sentinel_incomplete_protocol_detects_errors() -> None:
    """Validate incomplete protocol produces ERROR findings and lowers quality score.

    Requirements: PRD-SYS-001
    """
    incomplete_payload = {
        "id": "study_incomplete_002",
        "name": "Incomplete Draft Protocol",
        # Missing studyDesigns, eligibilityCriteria, objectives
    }

    sentinel = ProtocolQualitySentinel()
    report = sentinel.evaluate_protocol_quality(incomplete_payload)

    assert report.study_id == "study_incomplete_002"
    assert report.passed is False
    assert report.quality_score < 100.0
    assert len(report.findings) >= 2  # Structural ERROR + Regulatory WARNING

    finding_ids = [f.rule_id for f in report.findings]
    assert "SENTINEL_STRUCT_01" in finding_ids
    assert "SENTINEL_REG_02" in finding_ids


def test_quality_sentinel_router_endpoint() -> None:
    """Validate POST /api/v1/designer/sentinel/evaluate API endpoint returns report.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(change_reason="Audit protocol quality sentinel")
    response = client.post(
        "/api/v1/designer/sentinel/evaluate",
        json={
            "id": "study_api_eval_003",
            "name": "API Eval Study",
            "studyDesigns": [
                {
                    "id": "design_main",
                    "objectives": [{"id": "obj_1", "name": "Primary"}],
                }
            ],
            "eligibilityCriteria": [{"id": "c1", "text": "Inclusion"}],
        },
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["study_id"] == "study_api_eval_003"
    assert data["passed"] is True
    assert data["quality_score"] == 100.0


# =====================================================================
# ADDED SENTINEL / FEASIBILITY TESTS (PRD-SYS-001)
# =====================================================================


def test_syllable_counter_deterministic() -> None:
    """Validate the deterministic heuristic syllable counter."""
    assert count_syllables_word("age") == 1
    assert count_syllables_word("protocol") == 3
    assert count_syllables_word("eligibility") == 6
    assert count_syllables_word("") == 0


def test_readability_metrics_and_scoring() -> None:
    """Validate Flesch Reading Ease and Flesch-Kincaid calculations on study narrative blocks."""
    payload = {
        "id": "read_study",
        "studyDesigns": [{"id": "d1", "objectives": [{"id": "o1"}]}],
        "eligibilityCriteria": [{"id": "e1", "text": "Inclusion"}],
        "blocks": [
            {
                "id": "b1",
                "properties": {
                    "text": "The patient must sign the informed consent form before any clinical procedures are initiated."
                },
            }
        ],
    }
    sentinel = ProtocolQualitySentinel()
    report = sentinel.evaluate_protocol_quality(payload)

    assert report.readability is not None
    assert report.readability.word_count > 5
    assert report.readability.sentence_count >= 1
    assert report.readability.flesch_reading_ease > 0
    assert report.readability.flesch_kincaid_grade_level >= 0
    assert isinstance(report.readability.interpretation, str)


def test_burden_tracing_with_invasiveness_modifiers() -> None:
    """Validate operational burden trace calculations with high/moderate invasiveness modifiers."""
    payload = {
        "id": "burden_study",
        "studyDesigns": [
            {
                "id": "d1",
                "objectives": [{"id": "o1"}],
                "encounters": [{"id": "visit_01"}, {"id": "visit_02"}],
                "activities": [
                    {
                        "id": "act_01",
                        "name": "Surgical Biopsy",
                    },  # High invasiveness (+10.0)
                    {
                        "id": "act_02",
                        "name": "Blood Phlebotomy",
                    },  # Moderate invasiveness (+3.0)
                    {"id": "act_03", "name": "Standard Vitals"},  # Base procedure (2.0)
                ],
            }
        ],
        "eligibilityCriteria": [{"id": "e1"}],
    }

    sentinel = ProtocolQualitySentinel()
    report = sentinel.evaluate_protocol_quality(payload)

    # Visits: 2 * 1.5 = 3.0
    # Procedures:
    # - Surgical Biopsy: 2.0 (base) + 10.0 (biopsy) = 12.0
    # - Blood Phlebotomy: 2.0 (base) + 3.0 (blood) = 5.0
    # - Standard Vitals: 2.0 (base) = 2.0
    # Total Procedures = 19.0
    # Total Burden = 3.0 (visits) + 19.0 (procedures) = 22.0
    assert report.patient_burden_index == 22.0
    assert report.burden_details is not None
    assert report.burden_details.visit_burden == 3.0
    assert report.burden_details.procedure_burden == 19.0
    assert report.burden_details.total_burden == 22.0

    trace_components = [item.component for item in report.burden_details.trace]
    assert "visits" in trace_components
    assert "procedure: surgical biopsy" in trace_components
    assert "procedure: blood phlebotomy" in trace_components


def test_block_eligibility_soa_inconsistencies() -> None:
    """Validate that quality findings successfully surface block or eligibility inconsistencies with SoA."""
    payload = {
        "id": "incon_study",
        "studyDesigns": [
            {
                "id": "d1",
                "objectives": [{"id": "o1"}],
                "encounters": [{"id": "visit_exist"}],
                "activities": [
                    {
                        "id": "proc_exist",
                        "forms": [{"id": "form1", "fields": [{"id": "field_exist"}]}],
                    }
                ],
            }
        ],
        "eligibilityCriteria": [
            {
                "id": "e1",
                "dsl_source": "eCRF.DM.AGE >= 18",  # AGE is standard, allowed fallback
                "condition": {"type": "constant", "value": True},
            },
            {
                "id": "e2",
                "dsl_source": "eCRF.VS.NONEXISTENT_VAR < 50",  # NONEXISTENT_VAR is undefined -> finding
                "condition": {"type": "constant", "value": True},
            },
        ],
        "blocks": [
            {
                "id": "b1",
                "properties": {
                    "visit_id": "visit_nonexistent",  # Mismatch visit
                    "activity_id": "proc_exist",
                },
            },
            {
                "id": "b2",
                "properties": {
                    "visit_id": "visit_exist",
                    "activity_id": "proc_nonexistent",  # Mismatch procedure
                },
            },
        ],
    }

    sentinel = ProtocolQualitySentinel()
    report = sentinel.evaluate_protocol_quality(payload)

    finding_ids = [f.rule_id for f in report.findings]
    assert "SENTINEL_INCON_VISIT" in finding_ids
    assert "SENTINEL_INCON_PROC" in finding_ids
    assert "SENTINEL_INCON_ELIG" in finding_ids


def test_amendment_impact_and_cost_estimation() -> None:
    """Validate comparing study payload with frozen parent version projection calculates costs & burden change."""
    from apps.designer.db import MOCK_STUDY_PROJECTIONS_BY_VERSION

    # Mock frozen parent projection state in database memory
    parent_key = "study_amend_999:1.0"
    MOCK_STUDY_PROJECTIONS_BY_VERSION[parent_key] = {
        "id": "study_amend_999",
        "current_version": "1.0",
        "visits": [{"visit_id": "v1"}],
        "activities": [{"activity_id": "p1"}],
        "forms": [{"id": "f1"}],  # 1 form
    }

    # Amended payload
    amended_payload = {
        "id": "study_amend_999",
        "current_version": "2.0",
        "parent_version": "1.0",
        "studyDesigns": [
            {
                "id": "d1",
                "objectives": [{"id": "o1"}],
                "encounters": [{"id": "v1"}, {"id": "v2"}],
                "activities": [
                    {
                        "id": "p1",
                        "forms": [
                            {"id": "f1"},
                            {"id": "f2"},
                            {"id": "f3"},
                        ],  # 3 forms (+2 added)
                    }
                ],
            }
        ],
        "eligibilityCriteria": [{"id": "e1"}],
    }

    sentinel = ProtocolQualitySentinel()
    report = sentinel.evaluate_protocol_quality(amended_payload)

    assert report.amendment_impact is not None
    assert report.amendment_impact.base_version == "1.0"
    assert report.amendment_impact.amended_version == "2.0"
    assert report.amendment_impact.added_forms_count == 2
    # Added forms: 2 * $300.0 = $600.0
    # Base Overhead: $5000.0
    # Total Cost = $5600.0
    assert report.amendment_impact.estimated_cost_usd == 5600.0
    assert isinstance(report.amendment_impact.explanation, str)


def test_pluggable_fixture_patient_attrition() -> None:
    """Validate evaluating eligibility ASTs sequentially against built-in patient fixtures builds attrition."""
    # Criteria condition nodes:
    # Criterion 1: AGE >= 18
    # Criterion 2: LIVER_DISEASE == False
    payload = {
        "id": "attrition_study",
        "studyDesigns": [{"id": "d1", "objectives": [{"id": "o1"}]}],
        "eligibilityCriteria": [
            {
                "id": "crit_age",
                "criterion_type": "inclusion",
                "description": "Age must be at least 18",
                "dsl_source": "eCRF.DM.AGE >= 18",
                "expected_outcome": True,
                "condition": {
                    "type": "comparison",
                    "operator": ">=",
                    "operands": [
                        {
                            "type": "field_ref",
                            "field_ref": {
                                "raw_reference": "eCRF.DM.AGE",
                                "domain": "DM",
                                "variable": "AGE",
                            },
                        },
                        {"type": "constant", "value": 18},
                    ],
                },
            },
            {
                "id": "crit_liver",
                "criterion_type": "exclusion",
                "description": "Exclude patients with liver disease",
                "dsl_source": "eCRF.MH.LIVER_DISEASE == True",
                "expected_outcome": False,  # Expect False to pass/meet exclusion
                "condition": {
                    "type": "comparison",
                    "operator": "==",
                    "operands": [
                        {
                            "type": "field_ref",
                            "field_ref": {
                                "raw_reference": "eCRF.MH.LIVER_DISEASE",
                                "domain": "MH",
                                "variable": "LIVER_DISEASE",
                            },
                        },
                        {"type": "constant", "value": True},
                    ],
                },
            },
        ],
    }

    sentinel = ProtocolQualitySentinel()
    report = sentinel.evaluate_protocol_quality(payload)

    assert report.feasibility is not None
    assert report.feasibility.starting_cohort_size == 5
    # Total fixtures = 5
    # Step 1 (Age >= 18): PT_03 (Charlie, age 16) fails. 4 pass, 1 fails. Remaining = 4.
    # Step 2 (Exclude liver disease): Diana (PT_04, has_liver_disease=True) fails. 3 pass, 1 fails. Remaining = 3.
    assert len(report.feasibility.attrition_steps) == 2

    step1 = report.feasibility.attrition_steps[0]
    assert step1.criterion_id == "crit_age"
    assert step1.passed_count == 4
    assert step1.failed_count == 1
    assert step1.remaining_count == 4
    assert step1.attrition_rate == 20.0  # 1/5 lost = 20%

    step2 = report.feasibility.attrition_steps[1]
    assert step2.criterion_id == "crit_liver"
    assert step2.passed_count == 3
    assert step2.failed_count == 1
    assert step2.remaining_count == 3
    assert step2.attrition_rate == 25.0  # 1/4 lost = 25%

    assert report.feasibility.final_eligible_count == 3
    assert report.feasibility.overall_eligibility_rate == 60.0  # 3/5 = 60%
