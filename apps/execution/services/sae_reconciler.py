"""Automated EDC-to-Safety (SAE) matching and discrepancy detection engine.

Requirements: PRD-SYS-001
"""

from typing import Any

import packages  # noqa: F401
from apps.execution.domain.safety_models import SAECaseRecord


class SAEReconciler:
    """Engine comparing EDC Adverse Event form data against Safety ICSR cases to detect discrepancies.

    Requirements: PRD-SYS-001
    """

    def reconcile_edc_and_safety(
        self,
        edc_ae_events: list[dict[str, Any]],
        safety_cases: list[SAECaseRecord],
    ) -> dict[str, Any]:
        """Perform reconciliation between EDC AE events and Safety Database ICSR cases.

        Args:
            edc_ae_events: List of EDC AE form submission data dicts.
            safety_cases: List of SAECaseRecord instances from Safety DB.

        Returns:
            Structured reconciliation result report with flagged discrepancies.
        """
        matched_count = 0
        discrepancies: list[dict[str, Any]] = []

        safety_by_sub = {c.subject_id: c for c in safety_cases}

        for ae in edc_ae_events:
            sub_id = ae.get("subject_id")
            if not sub_id or sub_id not in safety_by_sub:
                continue

            scase = safety_by_sub[sub_id]

            # Check onset date match
            ae_date = str(ae.get("onset_date", "")).strip()
            date_match = ae_date == scase.onset_date

            # Check MedDRA code match
            ae_meddra = str(ae.get("meddra_code", "")).strip()
            meddra_match = ae_meddra == scase.meddra_code

            if date_match and meddra_match:
                matched_count += 1
            else:
                discrepancy_types = []
                if not date_match:
                    discrepancy_types.append("ONSET_DATE_MISMATCH")
                if not meddra_match:
                    discrepancy_types.append("MEDDRA_CODE_MISMATCH")

                discrepancies.append(
                    {
                        "subject_id": sub_id,
                        "sae_case_id": scase.case_id,
                        "safety_report_id": scase.safety_report_id,
                        "discrepancy_types": discrepancy_types,
                        "edc_onset_date": ae_date,
                        "safety_onset_date": scase.onset_date,
                        "edc_meddra_code": ae_meddra,
                        "safety_meddra_code": scase.meddra_code,
                    }
                )

        status = "CONCORDANT" if len(discrepancies) == 0 else "DISCREPANCIES_FLAGGED"

        return {
            "matched_cases_count": matched_count,
            "discrepancies_count": len(discrepancies),
            "discrepancies": discrepancies,
            "reconciliation_status": status,
        }
