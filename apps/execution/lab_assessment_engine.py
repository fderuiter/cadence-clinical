"""Re-export module for unified laboratory clinical assessment engine.

Requirements:
- ADR-2199 / PRD-SYS-123 (Laboratory Normal Range Governance & Interoperability)
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-PVG-001 (Automated Pharmacovigilance & Safety Escalation)
- Trace-1 (Audit Trail & 21 CFR Part 11)
- Trace-15 (Laboratory Data Flow & Reference Ranges)
"""

from apps.execution.domain.lab_assessment_models import (
    LabAssessmentContext,
    LabAssessmentResult,
)
from apps.execution.services.lab_assessment_engine import (
    LOINC_STANDARD_CATALOG,
    LabAssessmentEngine,
    convert_lab_unit,
    evaluate_lab_value,
    evaluate_laboratory_result,
    get_loinc_catalog,
    resolve_loinc_mapping,
    select_reference_range,
)

__all__ = [
    "LOINC_STANDARD_CATALOG",
    "LabAssessmentContext",
    "LabAssessmentEngine",
    "LabAssessmentResult",
    "convert_lab_unit",
    "evaluate_lab_value",
    "evaluate_laboratory_result",
    "get_loinc_catalog",
    "resolve_loinc_mapping",
    "select_reference_range",
]
