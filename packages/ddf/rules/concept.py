"""Terminology Concept Resolution for Digital Data Flow.

Resolves clinical concept IDs and terminology codes consistently across
Designer inverse mapping and Execution compilation pipelines.
"""

from typing import Any

# Default fallback terminology mapping for standard clinical domains
DEFAULT_MOCK_TERMINOLOGY: dict[str, Any] = {
    "sys_bp": {"code": "SYSBP", "decode": "Systolic Blood Pressure"},
    "dia_bp": {"code": "DIABP", "decode": "Diastolic Blood Pressure"},
    "heart_rate": {"code": "HR", "decode": "Heart Rate"},
    "temp": {"code": "TEMP", "decode": "Body Temperature"},
    "weight": {"code": "WEIGHT", "decode": "Body Weight"},
    "screening": {"code": "SCRN", "decode": "Screening Visit"},
    "baseline": {"code": "BASE", "decode": "Baseline Visit"},
    "treatment": {"code": "TRT", "decode": "Treatment Visit"},
    "follow_up": {"code": "FUP", "decode": "Follow-Up Visit"},
}


def resolve_concept_id(
    concept_dict: dict[str, Any] | None,
    terminology_cache: dict[str, Any] | None = None,
) -> str | None:
    """Resolves a terminology concept ID given a concept dict with a 'code'.

    Searches first within the provided terminology cache, falling back to
    standard mock terminology, and then defaulting to the code itself.
    """
    if not concept_dict or not isinstance(concept_dict, dict):
        return None
    code = concept_dict.get("code")
    if not code:
        return None

    # Try _original_id first
    if "_original_id" in concept_dict and concept_dict["_original_id"]:
        return str(concept_dict["_original_id"])

    # Search provided cache
    cache = (
        terminology_cache if terminology_cache is not None else DEFAULT_MOCK_TERMINOLOGY
    )
    for k, details in cache.items():
        if isinstance(details, dict) and details.get("code") == code:
            return k

    return str(code)
