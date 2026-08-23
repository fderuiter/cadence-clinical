"""Deterministic, side-effect-free lab reference-range selection, indicator evaluation,
and LOINC-aligned mathematical unit conversion engine.

Requirements:
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-SYS-123 / ADR-2199 (Reference Range Governance & Interoperability)
- 21 CFR Part 11 / GxP Compliance
"""

import json
import logging
from collections.abc import Iterable
from typing import Any

logger = logging.getLogger(__name__)

# Standard LOINC Catalog with standard mappings and exact mathematical conversion factors
LOINC_STANDARD_CATALOG: dict[str, dict[str, Any]] = {
    "ALT": {
        "loinc_code": "1742-6",
        "test_name": "Alanine Aminotransferase",
        "standard_unit": "U/L",
        "category": "HEPATIC",
        "conversions": {
            ("U/L", "ukat/L"): (0.01667, 0.0),
            ("ukat/L", "U/L"): (60.0, 0.0),
            ("IU/L", "U/L"): (1.0, 0.0),
            ("U/L", "IU/L"): (1.0, 0.0),
        },
    },
    "AST": {
        "loinc_code": "1920-8",
        "test_name": "Aspartate Aminotransferase",
        "standard_unit": "U/L",
        "category": "HEPATIC",
        "conversions": {
            ("U/L", "ukat/L"): (0.01667, 0.0),
            ("ukat/L", "U/L"): (60.0, 0.0),
            ("IU/L", "U/L"): (1.0, 0.0),
            ("U/L", "IU/L"): (1.0, 0.0),
        },
    },
    "BILI": {
        "loinc_code": "1975-2",
        "test_name": "Total Bilirubin",
        "standard_unit": "umol/L",
        "category": "HEPATIC",
        "molecular_weight": 584.66,
        "conversions": {
            ("mg/dL", "umol/L"): (17.1, 0.0),
            ("umol/L", "mg/dL"): (1.0 / 17.1, 0.0),
            ("mg/dL", "mg/L"): (10.0, 0.0),
            ("mg/L", "mg/dL"): (0.1, 0.0),
            ("mg/L", "umol/L"): (1.71, 0.0),
            ("umol/L", "mg/L"): (1.0 / 1.71, 0.0),
        },
    },
    "TBIL": {
        "loinc_code": "1975-2",
        "test_name": "Total Bilirubin",
        "standard_unit": "umol/L",
        "category": "HEPATIC",
        "conversions": {
            ("mg/dL", "umol/L"): (17.1, 0.0),
            ("umol/L", "mg/dL"): (1.0 / 17.1, 0.0),
        },
    },
    "ALKP": {
        "loinc_code": "6768-6",
        "test_name": "Alkaline Phosphatase",
        "standard_unit": "U/L",
        "category": "HEPATIC",
        "conversions": {
            ("U/L", "ukat/L"): (0.01667, 0.0),
            ("ukat/L", "U/L"): (60.0, 0.0),
            ("IU/L", "U/L"): (1.0, 0.0),
        },
    },
    "CREAT": {
        "loinc_code": "2160-0",
        "test_name": "Serum Creatinine",
        "standard_unit": "umol/L",
        "category": "RENAL",
        "molecular_weight": 113.12,
        "conversions": {
            ("mg/dL", "umol/L"): (88.4, 0.0),
            ("umol/L", "mg/dL"): (1.0 / 88.4, 0.0),
            ("mg/dL", "mmol/L"): (0.0884, 0.0),
            ("mmol/L", "mg/dL"): (1.0 / 0.0884, 0.0),
            ("mg/dL", "mg/L"): (10.0, 0.0),
            ("mg/L", "mg/dL"): (0.1, 0.0),
        },
    },
    "CREATININE": {
        "loinc_code": "2160-0",
        "test_name": "Serum Creatinine",
        "standard_unit": "umol/L",
        "category": "RENAL",
        "conversions": {
            ("mg/dL", "umol/L"): (88.4, 0.0),
            ("umol/L", "mg/dL"): (1.0 / 88.4, 0.0),
            ("mg/dL", "mmol/L"): (0.0884, 0.0),
            ("mmol/L", "mg/dL"): (1.0 / 0.0884, 0.0),
        },
    },
    "BUN": {
        "loinc_code": "3094-0",
        "test_name": "Blood Urea Nitrogen",
        "standard_unit": "mmol/L",
        "category": "RENAL",
        "conversions": {
            ("mg/dL", "mmol/L"): (0.357, 0.0),
            ("mmol/L", "mg/dL"): (1.0 / 0.357, 0.0),
        },
    },
    "EGFR": {
        "loinc_code": "33914-3",
        "test_name": "Glomerular Filtration Rate",
        "standard_unit": "mL/min/1.73m2",
        "category": "RENAL",
        "conversions": {},
    },
    "GLUCOSE": {
        "loinc_code": "2345-7",
        "test_name": "Glucose (Fasting)",
        "standard_unit": "mmol/L",
        "category": "METABOLIC",
        "molecular_weight": 180.16,
        "conversions": {
            ("mg/dL", "mmol/L"): (0.0555, 0.0),
            ("mmol/L", "mg/dL"): (18.0182, 0.0),
            ("g/L", "mmol/L"): (5.551, 0.0),
            ("mg/dL", "g/L"): (0.01, 0.0),
            ("g/L", "mg/dL"): (100.0, 0.0),
        },
    },
    "GLUC": {
        "loinc_code": "2345-7",
        "test_name": "Glucose (Fasting)",
        "standard_unit": "mmol/L",
        "category": "METABOLIC",
        "conversions": {
            ("mg/dL", "mmol/L"): (0.0555, 0.0),
            ("mmol/L", "mg/dL"): (18.0182, 0.0),
        },
    },
    "HEMOGLOBIN": {
        "loinc_code": "718-7",
        "test_name": "Hemoglobin",
        "standard_unit": "g/dL",
        "category": "HEMATOLOGY",
        "conversions": {
            ("g/dL", "g/L"): (10.0, 0.0),
            ("g/L", "g/dL"): (0.1, 0.0),
            ("g/dL", "mmol/L"): (0.6206, 0.0),
            ("mmol/L", "g/dL"): (1.0 / 0.6206, 0.0),
        },
    },
    "HGB": {
        "loinc_code": "718-7",
        "test_name": "Hemoglobin",
        "standard_unit": "g/dL",
        "category": "HEMATOLOGY",
        "conversions": {
            ("g/dL", "g/L"): (10.0, 0.0),
            ("g/L", "g/dL"): (0.1, 0.0),
            ("g/dL", "mmol/L"): (0.6206, 0.0),
            ("mmol/L", "g/dL"): (1.0 / 0.6206, 0.0),
        },
    },
    "PLATELETS": {
        "loinc_code": "777-3",
        "test_name": "Platelets",
        "standard_unit": "10^9/L",
        "category": "HEMATOLOGY",
        "conversions": {
            ("10^3/uL", "10^9/L"): (1.0, 0.0),
            ("10^9/L", "10^3/uL"): (1.0, 0.0),
            ("/uL", "10^9/L"): (0.001, 0.0),
            ("10^9/L", "/uL"): (1000.0, 0.0),
            ("K/uL", "10^9/L"): (1.0, 0.0),
            ("10*3/uL", "10^9/L"): (1.0, 0.0),
        },
    },
    "PLT": {
        "loinc_code": "777-3",
        "test_name": "Platelets",
        "standard_unit": "10^9/L",
        "category": "HEMATOLOGY",
        "conversions": {
            ("10^3/uL", "10^9/L"): (1.0, 0.0),
            ("10^9/L", "10^3/uL"): (1.0, 0.0),
            ("/uL", "10^9/L"): (0.001, 0.0),
            ("10^9/L", "/uL"): (1000.0, 0.0),
            ("K/uL", "10^9/L"): (1.0, 0.0),
        },
    },
    "ANC": {
        "loinc_code": "26499-4",
        "test_name": "Absolute Neutrophil Count",
        "standard_unit": "10^9/L",
        "category": "HEMATOLOGY",
        "conversions": {
            ("10^3/uL", "10^9/L"): (1.0, 0.0),
            ("10^9/L", "10^3/uL"): (1.0, 0.0),
            ("/uL", "10^9/L"): (0.001, 0.0),
            ("10^9/L", "/uL"): (1000.0, 0.0),
            ("K/uL", "10^9/L"): (1.0, 0.0),
        },
    },
    "NEUT": {
        "loinc_code": "26499-4",
        "test_name": "Absolute Neutrophils",
        "standard_unit": "10^9/L",
        "category": "HEMATOLOGY",
        "conversions": {
            ("10^3/uL", "10^9/L"): (1.0, 0.0),
            ("10^9/L", "10^3/uL"): (1.0, 0.0),
            ("/uL", "10^9/L"): (0.001, 0.0),
        },
    },
    "WBC": {
        "loinc_code": "6690-2",
        "test_name": "Leukocytes",
        "standard_unit": "10^9/L",
        "category": "HEMATOLOGY",
        "conversions": {
            ("10^3/uL", "10^9/L"): (1.0, 0.0),
            ("10^9/L", "10^3/uL"): (1.0, 0.0),
            ("/uL", "10^9/L"): (0.001, 0.0),
            ("10^9/L", "/uL"): (1000.0, 0.0),
            ("K/uL", "10^9/L"): (1.0, 0.0),
        },
    },
    "LYMPH": {
        "loinc_code": "731-0",
        "test_name": "Lymphocytes",
        "standard_unit": "10^9/L",
        "category": "HEMATOLOGY",
        "conversions": {
            ("10^3/uL", "10^9/L"): (1.0, 0.0),
            ("10^9/L", "10^3/uL"): (1.0, 0.0),
            ("/uL", "10^9/L"): (0.001, 0.0),
        },
    },
    "POTASSIUM": {
        "loinc_code": "2823-3",
        "test_name": "Potassium",
        "standard_unit": "mmol/L",
        "category": "ELECTROLYTES",
        "conversions": {
            ("mEq/L", "mmol/L"): (1.0, 0.0),
            ("mmol/L", "mEq/L"): (1.0, 0.0),
        },
    },
    "K": {
        "loinc_code": "2823-3",
        "test_name": "Potassium",
        "standard_unit": "mmol/L",
        "category": "ELECTROLYTES",
        "conversions": {
            ("mEq/L", "mmol/L"): (1.0, 0.0),
            ("mmol/L", "mEq/L"): (1.0, 0.0),
        },
    },
    "SODIUM": {
        "loinc_code": "2951-2",
        "test_name": "Sodium",
        "standard_unit": "mmol/L",
        "category": "ELECTROLYTES",
        "conversions": {
            ("mEq/L", "mmol/L"): (1.0, 0.0),
            ("mmol/L", "mEq/L"): (1.0, 0.0),
        },
    },
    "NA": {
        "loinc_code": "2951-2",
        "test_name": "Sodium",
        "standard_unit": "mmol/L",
        "category": "ELECTROLYTES",
        "conversions": {
            ("mEq/L", "mmol/L"): (1.0, 0.0),
            ("mmol/L", "mEq/L"): (1.0, 0.0),
        },
    },
    "CALCIUM": {
        "loinc_code": "17861-6",
        "test_name": "Calcium",
        "standard_unit": "mmol/L",
        "category": "ELECTROLYTES",
        "conversions": {
            ("mg/dL", "mmol/L"): (0.25, 0.0),
            ("mmol/L", "mg/dL"): (4.0, 0.0),
            ("mEq/L", "mmol/L"): (0.5, 0.0),
            ("mmol/L", "mEq/L"): (2.0, 0.0),
        },
    },
    "CA": {
        "loinc_code": "17861-6",
        "test_name": "Calcium",
        "standard_unit": "mmol/L",
        "category": "ELECTROLYTES",
        "conversions": {
            ("mg/dL", "mmol/L"): (0.25, 0.0),
            ("mmol/L", "mg/dL"): (4.0, 0.0),
        },
    },
    "CHOLESTEROL": {
        "loinc_code": "2093-3",
        "test_name": "Total Cholesterol",
        "standard_unit": "mmol/L",
        "category": "METABOLIC",
        "conversions": {
            ("mg/dL", "mmol/L"): (0.02586, 0.0),
            ("mmol/L", "mg/dL"): (1.0 / 0.02586, 0.0),
        },
    },
    "CHOL": {
        "loinc_code": "2093-3",
        "test_name": "Total Cholesterol",
        "standard_unit": "mmol/L",
        "category": "METABOLIC",
        "conversions": {
            ("mg/dL", "mmol/L"): (0.02586, 0.0),
            ("mmol/L", "mg/dL"): (1.0 / 0.02586, 0.0),
        },
    },
    "TRIGLYCERIDES": {
        "loinc_code": "2571-8",
        "test_name": "Triglycerides",
        "standard_unit": "mmol/L",
        "category": "METABOLIC",
        "conversions": {
            ("mg/dL", "mmol/L"): (0.01129, 0.0),
            ("mmol/L", "mg/dL"): (1.0 / 0.01129, 0.0),
        },
    },
    "TRIG": {
        "loinc_code": "2571-8",
        "test_name": "Triglycerides",
        "standard_unit": "mmol/L",
        "category": "METABOLIC",
        "conversions": {
            ("mg/dL", "mmol/L"): (0.01129, 0.0),
            ("mmol/L", "mg/dL"): (1.0 / 0.01129, 0.0),
        },
    },
}


def resolve_loinc_mapping(
    test_code: str, loinc_code: str | None = None
) -> dict[str, Any] | None:
    """Resolves LOINC dictionary metadata for a given lab test code."""
    code_u = test_code.strip().upper()
    if code_u in LOINC_STANDARD_CATALOG:
        return LOINC_STANDARD_CATALOG[code_u]

    if loinc_code:
        loinc_clean = loinc_code.strip()
        for _, v in LOINC_STANDARD_CATALOG.items():
            if v.get("loinc_code") == loinc_clean:
                return v
    return None


def get_loinc_catalog() -> list[dict[str, Any]]:
    """Returns the standardized LOINC catalog items list."""
    items = []
    seen = set()
    for code, entry in LOINC_STANDARD_CATALOG.items():
        loinc_code = entry["loinc_code"]
        if loinc_code not in seen:
            seen.add(loinc_code)
            items.append(
                {
                    "test_code": code,
                    "test_name": entry["test_name"],
                    "loinc_code": loinc_code,
                    "standard_unit": entry["standard_unit"],
                    "category": entry.get("category", "GENERAL"),
                    "molecular_weight": entry.get("molecular_weight"),
                }
            )
    return items


def _get_val(obj: object | dict[str, Any], key: str, default: Any = None) -> Any:
    """Helper to fetch an attribute or dict key gracefully."""
    if isinstance(obj, dict):
        val = obj.get(key)
        if val is not None:
            return val
        synonyms = {
            "low_bound": ["range_low", "low", "ref_low", "reference_range_low"],
            "high_bound": ["range_high", "high", "ref_high", "reference_range_high"],
            "range_low": ["low_bound", "low", "ref_low"],
            "range_high": ["high_bound", "high", "ref_high"],
            "critical_low": ["crit_low", "panic_low"],
            "critical_high": ["crit_high", "panic_high"],
            "source": ["lab_source"],
            "lab_source": ["source"],
            "sex_applicability": ["sex", "gender"],
            "sex": ["sex_applicability", "gender"],
            "ethnicity_applicability": ["ethnicity", "race_ethnicity"],
            "ethnicity": ["ethnicity_applicability", "race_ethnicity"],
        }
        if key in synonyms:
            for syn in synonyms[key]:
                if syn in obj and obj[syn] is not None:
                    return obj[syn]
        return default
    return getattr(obj, key, default)


def select_reference_range(
    ranges: Iterable[object | dict[str, Any]],
    study_id: str,
    test_code: str,
    normalized_unit: str,
    lab_source: str,
    sex: str | None,
    age: float | None,
    site_id: str | None = None,
    ethnicity: str | None = None,
) -> object | dict[str, Any] | None:
    """Selects an active LabReferenceRange based on study, test code, normalized unit,
    lab source, site, sex, ethnicity, and age, following a stable multi-dimensional
    specificity and tie-breaking policy.

    Args:
        ranges (Iterable[Any]): Iterable of LabReferenceRange objects or dictionaries.
        study_id (str): The unique clinical trial study identifier.
        test_code (str): The laboratory test code (e.g. 'WBC').
        normalized_unit (str): The normalized unit of measurement (e.g. '10^9/L').
        lab_source (str): The lab source of the observation ('CENTRAL' or 'LOCAL').
        sex (Optional[str]): The subject's sex/gender ('M', 'F', 'U', or None).
        age (Optional[float]): The subject's age in completed years at observation.
        site_id (Optional[str]): The clinical site identifier (optional, used for local ranges).
        ethnicity (Optional[str]): The subject's ethnicity or race (e.g. 'HISPANIC', 'ASIAN', 'ALL').

    Returns:
        Any: The best-matched LabReferenceRange object or dict, or None if no match is found.
    """
    candidates = []

    # Clean input sex using basic normalization
    from apps.execution.demographics import normalize_gender

    norm_sex = normalize_gender(sex, preserve_custom=True) if sex is not None else "U"
    norm_ethnicity = (
        str(ethnicity).strip().upper() if ethnicity and str(ethnicity).strip() else None
    )

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
        r_sex = _get_val(r, "sex_applicability")
        if r_sex:
            r_sex = str(r_sex).strip().upper()

        sex_score = 0
        if norm_sex and norm_sex != "U":
            if r_sex == norm_sex:
                sex_score = 2  # Exact match for M, F, or custom sex (e.g. "OTHER")
            elif r_sex in ("ALL", None, "", "U"):
                sex_score = 1  # Fallback to ALL/generic
        else:
            # If subject's sex is unknown/None/U, only match ALL/generic ranges
            if r_sex in ("ALL", None, "", "U"):
                sex_score = 1

        if sex_score == 0:
            continue

        # 5. Calculate Ethnicity Specificity Score
        r_ethnicity = _get_val(r, "ethnicity_applicability") or _get_val(r, "ethnicity")
        if r_ethnicity:
            r_ethnicity = str(r_ethnicity).strip().upper()

        ethnicity_score = 0
        if norm_ethnicity and norm_ethnicity not in ("ALL", "U", "UNKNOWN"):
            if r_ethnicity == norm_ethnicity:
                ethnicity_score = 2  # Exact ethnicity match
            elif r_ethnicity in ("ALL", None, "", "U", "UNKNOWN"):
                ethnicity_score = 1  # Generic/ALL fallback
        else:
            if r_ethnicity in ("ALL", None, "", "U", "UNKNOWN"):
                ethnicity_score = 1

        if ethnicity_score == 0:
            continue

        # 6. Calculate Age Specificity Score
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

        age_span = (
            (age_high_val - age_low_val)
            if (age_low_val is not None and age_high_val is not None)
            else float("inf")
        )

        r_low_bound = _get_val(r, "low_bound")
        low_bound_val = float(r_low_bound) if r_low_bound is not None else float("-inf")

        r_high_bound = _get_val(r, "high_bound")
        high_bound_val = (
            float(r_high_bound) if r_high_bound is not None else float("inf")
        )

        sort_key = (
            -site_score,
            -sex_score,
            -ethnicity_score,
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
    value: float | None,
    reference_range: object | dict[str, Any] | None,
) -> tuple[str | None, bool, str | None]:
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


async def recalculate_range_flags(
    session: Any,
    study_id: str,
    test_code: str,
    background_tasks: Any | None = None,
) -> int:
    """Query and update the reference range indicators for all observations in a study-test cohort.

    Fetches all active clinical observations and lab reference ranges for the specified
    test code within the given study. It then resolves each active observation against
    its subject's decrypted demographics, matching a reference range and evaluating the
    lab indicator, out-of-range flag, and normal bounds. It updates modified observations
    and commits.

    Args:
        session: The database session.
        study_id (str): The unique identifier of the study.
        test_code (str): The test parameter code.
        background_tasks: Optional Starlette BackgroundTasks helper.

    Returns:
        int: The number of observations updated.
    """
    from datetime import datetime

    from sqlalchemy import select

    from apps.execution.database.models import (
        ClinicalObservation,
        ClinicalSubject,
        LabReferenceRange,
    )
    from apps.execution.demographics import get_safe_demographics
    from packages.security.context import current_change_reason, current_user_id

    # 1. Fetch active observations
    stmt_obs = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.test_code == test_code,
        ClinicalObservation.is_deleted.is_(False),
    )
    res_obs = await session.execute(stmt_obs)
    observations = res_obs.scalars().all()

    if not observations:
        return 0

    # 2. Fetch active reference ranges
    stmt_ranges = select(LabReferenceRange).where(
        LabReferenceRange.study_id == study_id,
        LabReferenceRange.test_code == test_code,
        LabReferenceRange.is_deleted.is_(False),
    )
    res_ranges = await session.execute(stmt_ranges)
    ranges = res_ranges.scalars().all()

    # 3. Fetch active subjects
    stmt_subjects = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    res_subjects = await session.execute(stmt_subjects)
    subjects = {subj.subject_id: subj for subj in res_subjects.scalars().all()}

    updated_count = 0
    user_id = current_user_id.get()
    change_reason = current_change_reason.get()

    for obs in observations:
        obs_date = obs.observation_date or datetime.now()
        subj = subjects.get(obs.subject_id)
        if subj:
            demo = get_safe_demographics(subj, obs_date, preserve_custom=True)
            gender = demo.get("gender")
            age = demo.get("age")
            ethnicity = demo.get("ethnicity")
        else:
            gender = "U"
            age = None
            ethnicity = None

        matched_range = select_reference_range(
            ranges=ranges,
            study_id=study_id,
            test_code=test_code,
            normalized_unit=obs.normalized_unit,
            lab_source=obs.lab_source or "CENTRAL",
            sex=gender,
            age=age,
            site_id=obs.lab_site_id,
            ethnicity=ethnicity,
        )

        indicator, out_of_range, matched_bounds = evaluate_lab_value(
            obs.normalized_value, matched_range
        )

        changed = (
            obs.lab_indicator != indicator
            or obs.lab_out_of_range != out_of_range
            or obs.matched_normal_bounds != matched_bounds
        )

        if changed:
            obs.lab_indicator = indicator
            obs.lab_out_of_range = out_of_range
            obs.matched_normal_bounds = matched_bounds
            obs.version += 1
            updated_count += 1

            # Dispatch alerts on transitioning to critical indicator
            if indicator in ("LOW LOW", "HIGH HIGH") and background_tasks is not None:
                from apps.execution.notification_events import (
                    dispatch_critical_lab_alerts,
                )

                dispatch_critical_lab_alerts(
                    background_tasks,
                    obs,
                    indicator,
                    user_id,
                    change_reason,
                )

    if updated_count > 0:
        await session.commit()

    return updated_count


async def convert_lab_unit(
    session: Any,
    test_code: str,
    from_unit: str,
    to_unit: str,
    value: float,
) -> float:
    """Converts a numeric laboratory value from one unit to another, utilizing
    database-defined LabUnitConversion entries first, then standard LOINC
    mathematical conversions, and falling back to static UCUM conversions.

    Args:
        session (Any): Async SQLAlchemy database session.
        test_code (str): The laboratory test code (e.g. 'HEMOGLOBIN').
        from_unit (str): The starting unit of measurement.
        to_unit (str): The target unit of measurement.
        value (float): The numeric value to convert.

    Returns:
        float: The converted numeric value.
    """
    from sqlalchemy import select

    from apps.execution.database.models import LabUnitConversion
    from apps.execution.ucum import convert_unit

    if from_unit == to_unit:
        return value

    # 1. Check database-defined custom unit conversion
    if session is not None:
        try:
            stmt = select(LabUnitConversion).where(
                LabUnitConversion.test_code == test_code,
                LabUnitConversion.from_unit == from_unit,
                LabUnitConversion.to_unit == to_unit,
                LabUnitConversion.is_deleted.is_(False),
            )
            result = await session.execute(stmt)
            row = result.scalars().first()

            if row is not None:
                factor = float(row.factor)
                offset = float(row.offset) if row.offset is not None else 0.0
                return value * factor + offset
        except Exception as e:
            logger.debug(f"Database unit conversion query skipped/failed: {e}")

    # 2. Check LOINC standard mathematical catalog conversions
    loinc_entry = resolve_loinc_mapping(test_code)
    if loinc_entry and "conversions" in loinc_entry:
        conv_map = loinc_entry["conversions"]
        if (from_unit, to_unit) in conv_map:
            factor, offset = conv_map[(from_unit, to_unit)]
            return value * factor + offset

    # 3. Fallback to generic UCUM conversion
    return convert_unit(value, from_unit, to_unit)
