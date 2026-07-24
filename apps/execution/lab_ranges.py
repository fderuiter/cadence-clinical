"""
Deterministic, side-effect-free lab reference-range selection and indicator engine.

This engine matches clinical laboratory observations to configured reference ranges
based on Study, Test Code, Normalized Unit, Lab Source, Site, Sex, and Age. It then
evaluates normalized numeric values against the matched range's normal and critical
boundaries to derive clinical indicators.

Specificity/Tie-Breaking Policy:
- Exact local site and applicable sex/age rules must win over generic alternatives.
- Specificity is computed multi-dimensionally:
    1. Site specificity: Exact local site (score 3) > Generic local (score 2) > Central (score 1)
    2. Sex specificity: Exact sex match (score 2) > Generic/ALL sex (score 1)
    3. Age specificity: Exact age range with both bounds (score 3) > Single bound (score 2) > No bounds (score 1)
- If any score is 0, the rule is incompatible and discarded.
- Equal-specificity rules are resolved deterministically using a stable tie-breaking key
  based on narrowest age span, age boundaries, normal boundaries, and record identifier.

Boundary Inclusion Policy:
- Normal boundaries (low_bound, high_bound) are inclusive.
- Critical boundaries (critical_low, critical_high) are exclusive.
- Thus, the indicators are derived as follows:
    - LOW LOW: value < critical_low (if critical_low is present)
    - HIGH HIGH: value > critical_high (if critical_high is present)
    - LOW: low_bound is present and value < low_bound (and not LOW LOW)
    - HIGH: high_bound is present and value > high_bound (and not HIGH HIGH)
    - NORMAL: low_bound <= value <= high_bound (or compatible other cases not falling in LOW/HIGH)
"""

import json
import logging
from typing import Any, Iterable, Optional, Tuple

logger = logging.getLogger(__name__)


def _get_val(obj: Any, key: str, default: Any = None) -> Any:
    """Helper to safely retrieve a value from an object or a dictionary."""
    if isinstance(obj, dict):
        return obj.get(key, default)
    return getattr(obj, key, default)


def select_reference_range(
    ranges: Iterable[Any],
    study_id: str,
    test_code: str,
    normalized_unit: str,
    lab_source: str,
    sex: Optional[str],
    age: Optional[float],
    site_id: Optional[str] = None,
) -> Any:
    """Selects an active LabReferenceRange based on study, test code, normalized unit,
    lab source, site, sex, and age, following a stable multi-dimensional specificity
    and tie-breaking policy.

    Args:
        ranges (Iterable[Any]): Iterable of LabReferenceRange objects or dictionaries.
        study_id (str): The unique clinical trial study identifier.
        test_code (str): The laboratory test code (e.g. 'WBC').
        normalized_unit (str): The normalized unit of measurement (e.g. '10^9/L').
        lab_source (str): The lab source of the observation ('CENTRAL' or 'LOCAL').
        sex (Optional[str]): The subject's sex/gender ('M', 'F', 'U', or None).
        age (Optional[float]): The subject's age in completed years at observation.
        site_id (Optional[str]): The clinical site identifier (optional, used for local ranges).

    Returns:
        Any: The best-matched LabReferenceRange object or dict, or None if no match is found.
    """
    candidates = []

    # Clean input sex using basic normalization
    norm_sex = None
    if sex:
        s_clean = str(sex).strip().upper()
        if s_clean in ("M", "MALE", "BOY", "MAN"):
            norm_sex = "M"
        elif s_clean in ("F", "FEMALE", "GIRL", "WOMAN"):
            norm_sex = "F"
        elif s_clean in ("U", "UNKNOWN"):
            norm_sex = "U"
        else:
            norm_sex = s_clean

    for r in ranges:
        # 1. Skip deleted records
        if _get_val(r, "is_deleted", False):
            continue

        # 2. Match exact study, test code, and normalized unit
        if _get_val(r, "study_id") != study_id:
            continue
        if _get_val(r, "test_code") != test_code:
            continue
        if _get_val(r, "normalized_unit") != normalized_unit:
            continue

        # 3. Calculate Site/Source Specificity Score
        # lab_source can be "LOCAL" or "CENTRAL"
        # LabReferenceRange source can be "LOCAL" or "CENTRAL"
        r_source = _get_val(r, "source")
        r_site_id = _get_val(r, "site_id")

        site_score = 0
        if lab_source == "LOCAL":
            if r_source == "LOCAL":
                if r_site_id and site_id and r_site_id == site_id:
                    site_score = 3  # Exact site match
                elif not r_site_id:
                    site_score = 2  # Generic local match
            elif r_source == "CENTRAL":
                site_score = 1  # Fallback to central
        elif lab_source == "CENTRAL":
            if r_source == "CENTRAL":
                site_score = 1  # Central source match
            # LOCAL ranges do not match for CENTRAL observations
        else:
            # Handle unknown observation sources gracefully
            if r_source == "CENTRAL":
                site_score = 1

        if site_score == 0:
            continue

        # 4. Calculate Sex Specificity Score
        # LabReferenceRange sex_applicability can be "M", "F", "ALL", None, or empty
        r_sex = _get_val(r, "sex_applicability")
        if r_sex:
            r_sex = str(r_sex).strip().upper()

        sex_score = 0
        if norm_sex in ("M", "F"):
            if r_sex == norm_sex:
                sex_score = 2  # Exact sex match
            elif r_sex in ("ALL", None, "", "U"):
                sex_score = 1  # Generic fallback
        else:
            # If subject's sex is unknown/None/U, only match ALL/generic ranges
            if r_sex in ("ALL", None, "", "U"):
                sex_score = 1

        if sex_score == 0:
            continue

        # 5. Calculate Age Specificity Score
        r_age_low = _get_val(r, "age_low")
        r_age_high = _get_val(r, "age_high")

        # Convert to float safely if present
        age_low_val = float(r_age_low) if r_age_low is not None else None
        age_high_val = float(r_age_high) if r_age_high is not None else None

        age_score = 0
        if age is not None:
            # Check if age matches bounds
            matches_low = age_low_val is None or age_low_val <= age
            matches_high = age_high_val is None or age <= age_high_val
            if matches_low and matches_high:
                if age_low_val is not None and age_high_val is not None:
                    age_score = 3  # Both bounds specified
                elif age_low_val is not None or age_high_val is not None:
                    age_score = 2  # Single bound specified
                else:
                    age_score = 1  # No bounds specified
        else:
            # If subject's age is None, they can only match ranges with no age bounds
            if age_low_val is None and age_high_val is None:
                age_score = 1

        if age_score == 0:
            continue

        # Candidate meets all criteria, build stable tie-breaking and sorting details
        r_id = _get_val(r, "id")
        id_str = str(r_id) if r_id is not None else ""

        # Deterministic sorting key for ascending sort (best match will be sorted first/minimum)
        # We negate the scores to ensure higher scores sort first.
        # We compute age span safely, preferring smaller spans (more specific).
        age_span = (
            (age_high_val - age_low_val)
            if (age_low_val is not None and age_high_val is not None)
            else float("inf")
        )

        # Tie-breakers to ensure stability:
        # - age_low descending (negated)
        # - age_high ascending
        # - low_bound ascending (normalized to float for safe comparison)
        r_low_bound = _get_val(r, "low_bound")
        low_bound_val = float(r_low_bound) if r_low_bound is not None else float("-inf")

        r_high_bound = _get_val(r, "high_bound")
        high_bound_val = (
            float(r_high_bound) if r_high_bound is not None else float("inf")
        )

        sort_key = (
            -site_score,
            -sex_score,
            -age_score,
            age_span,
            -age_low_val if age_low_val is not None else float("inf"),
            age_high_val
            if age_high_val is not None
            else float("inf"),  # Safe comparison fallback
            low_bound_val,
            high_bound_val,
            id_str,
        )

        candidates.append((sort_key, r))

    if not candidates:
        return None

    # Sort and return the highest specificity candidate (the one with the minimum key)
    candidates.sort(key=lambda item: item[0])
    return candidates[0][1]


def evaluate_lab_value(
    value: Optional[float],
    reference_range: Any,
) -> Tuple[Optional[str], bool, Optional[str]]:
    """Evaluates a normalized numeric laboratory value against a matched reference range,
    incorporating normal and critical bounds.

    Args:
        value (Optional[float]): The normalized numeric lab value to evaluate.
        reference_range (Any): The matched LabReferenceRange object or dict, or None.

    Returns:
        Tuple[Optional[str], bool, Optional[str]]: A tuple containing:
            - indicator (Optional[str]): "NORMAL", "LOW", "HIGH", "LOW LOW", "HIGH HIGH", or None.
            - out_of_range (bool): True if out of range, False otherwise.
            - matched_normal_bounds (Optional[str]): JSON-serialized normal range bounds e.g. '{"low": 10.0, "high": 40.0}', or None.
    """
    if reference_range is None:
        return None, False, None

    # Extract normal and critical bounds safely
    low_bound = _get_val(reference_range, "low_bound")
    high_bound = _get_val(reference_range, "high_bound")
    critical_low = _get_val(reference_range, "critical_low")
    critical_high = _get_val(reference_range, "critical_high")

    low_bound_val = float(low_bound) if low_bound is not None else None
    high_bound_val = float(high_bound) if high_bound is not None else None
    critical_low_val = float(critical_low) if critical_low is not None else None
    critical_high_val = float(critical_high) if critical_high is not None else None

    # Build the matched normal bounds snapshot JSON string
    bounds_dict = {
        "low": low_bound_val,
        "high": high_bound_val,
    }
    matched_normal_bounds = json.dumps(bounds_dict)

    if value is None:
        return None, False, matched_normal_bounds

    value_val = float(value)

    # 1. Check Critical Low (Exclusive: value < critical_low)
    if critical_low_val is not None and value_val < critical_low_val:
        return "LOW LOW", True, matched_normal_bounds

    # 2. Check Critical High (Exclusive: value > critical_high)
    if critical_high_val is not None and value_val > critical_high_val:
        return "HIGH HIGH", True, matched_normal_bounds

    # 3. Check Normal Low (Inclusive normal bounds, so out of range is value < low_bound)
    if low_bound_val is not None and value_val < low_bound_val:
        return "LOW", True, matched_normal_bounds

    # 4. Check Normal High (Inclusive normal bounds, so out of range is value > high_bound)
    if high_bound_val is not None and value_val > high_bound_val:
        return "HIGH", True, matched_normal_bounds

    # 5. Default/Normal range
    return "NORMAL", False, matched_normal_bounds
