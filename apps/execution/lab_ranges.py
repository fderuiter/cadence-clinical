"""Deterministic, side-effect-free lab reference-range selection, indicator evaluation,
and LOINC-aligned mathematical unit conversion engine.

Requirements:
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-SYS-123 / ADR-2199 (Reference Range Governance & Interoperability)
- 21 CFR Part 11 / GxP Compliance
"""

from typing import Any

from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.services.lab_assessment_engine import (
    LOINC_STANDARD_CATALOG,
    LabAssessmentEngine,
    convert_lab_unit,
    evaluate_lab_value,
    get_loinc_catalog,
    resolve_loinc_mapping,
    select_reference_range,
)


def _get_val(obj: object | dict[str, Any], key: str, default: Any = None) -> Any:
    """Helper to fetch an attribute or dict key gracefully."""
    from apps.execution.services.lab_assessment_engine import (
        _get_val as _engine_get_val,
    )

    return _engine_get_val(obj, key, default)


async def recalculate_range_flags(
    session: AsyncSession,
    study_id: str,
    test_code: str,
    background_tasks: Any | None = None,
) -> int:
    """Query and update the reference range indicators for all observations in a study-test cohort."""
    return await LabAssessmentEngine.recalculate_cohort_range_flags(
        session=session,
        study_id=study_id,
        test_code=test_code,
        background_tasks=background_tasks,
    )


__all__ = [
    "LOINC_STANDARD_CATALOG",
    "_get_val",
    "convert_lab_unit",
    "evaluate_lab_value",
    "get_loinc_catalog",
    "recalculate_range_flags",
    "resolve_loinc_mapping",
    "select_reference_range",
]
