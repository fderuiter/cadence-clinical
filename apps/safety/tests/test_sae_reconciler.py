"""Unit test suite for EDC-to-Safety SAE reconciler engine.

Requirements: PRD-SYS-001
"""

from datetime import UTC, datetime

import packages  # noqa: F401
from apps.execution.services.sae_reconciler import SAEReconciler
from apps.execution.src.domain.safety_models import (
    CausalityEnum,
    SAECaseRecord,
    SeriousnessCriteriaEnum,
)


def test_sae_reconciler_concordant_and_discrepant() -> None:
    """Validate SAE reconciler identifies matching cases and flags date/MedDRA discrepancies.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(UTC).isoformat()

    safety_case_1 = SAECaseRecord(
        case_id="sae_01",
        study_id="study_safety_01",
        subject_id="sub_101",
        safety_report_id="ICSR_101",
        reaction_pt="Myocardial Infarction",
        meddra_code="10028596",
        onset_date="2026-07-20",
        seriousness_criteria=SeriousnessCriteriaEnum.HOSPITALIZATION,
        causality=CausalityEnum.PROBABLE,
        parsed_at=now_iso,
    )

    safety_case_2 = SAECaseRecord(
        case_id="sae_02",
        study_id="study_safety_01",
        subject_id="sub_102",
        safety_report_id="ICSR_102",
        reaction_pt="Anaphylactic Shock",
        meddra_code="10002198",
        onset_date="2026-07-22",
        seriousness_criteria=SeriousnessCriteriaEnum.LIFE_THREATENING,
        causality=CausalityEnum.CERTAIN,
        parsed_at=now_iso,
    )

    edc_events = [
        # Match case 1
        {
            "subject_id": "sub_101",
            "onset_date": "2026-07-20",
            "meddra_code": "10028596",
        },
        # Mismatched onset_date & meddra_code for case 2
        {
            "subject_id": "sub_102",
            "onset_date": "2026-07-21",  # Discrepant date
            "meddra_code": "99999999",  # Discrepant code
        },
    ]

    reconciler = SAEReconciler()
    report = reconciler.reconcile_edc_and_safety(
        edc_events, [safety_case_1, safety_case_2]
    )

    assert report["matched_cases_count"] == 1
    assert report["discrepancies_count"] == 1
    assert report["reconciliation_status"] == "DISCREPANCIES_FLAGGED"

    discrepancy = report["discrepancies"][0]
    assert discrepancy["subject_id"] == "sub_102"
    assert "ONSET_DATE_MISMATCH" in discrepancy["discrepancy_types"]
    assert "MEDDRA_CODE_MISMATCH" in discrepancy["discrepancy_types"]
