"""Unified Laboratory Clinical Assessment Engine.

Encapsulates:
1. Multi-dimensional demographic & site reference range selection (Age, Sex, Site, Ethnicity) with tie-breaking.
2. Standard LOINC dictionary metadata mapping and mathematical unit conversions (mg/dL <-> umol/L, conventional to SI).
3. Local laboratory certification / accreditation verification and uncalibrated range safeguards.
4. Comprehensive NCI-CTCAE v5.0 and DAIDS v2.1 toxicity grading (Hepatic, Renal, Hematology, Electrolytes, Metabolic).
5. Hy's Law compound assessment for Drug-Induced Liver Injury (DILI).
6. Acute baseline delta checks (>= 50% deviation).
7. Automated drafting of staged Adverse Event eCRF form submissions, review records, and discrepancy queries.

Requirements:
- ADR-2199 / PRD-SYS-123 (Laboratory Normal Range Governance & Interoperability)
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-PVG-001 (Automated Pharmacovigilance & Safety Escalation)
- PRD-MDR-001 (Metadata Repository & Catalog Normalization)
- PRD-QRY-001 (Automated Discrepancy Query Escalation)
- Trace-1 (Audit Trail & 21 CFR Part 11)
- Trace-15 (Laboratory Data Flow & Reference Ranges)
"""

import json
import logging
import uuid
from collections.abc import Iterable
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    FormSubmission,
    LabReferenceRange,
    LabTestMaster,
    LabUnitConversion,
    LocalLabCertification,
    StagedAdverseEventReview,
    SubjectConsent,
)
from apps.execution.demographics import get_safe_demographics, normalize_gender
from apps.execution.domain.lab_assessment_models import (
    LabAssessmentContext,
    LabAssessmentResult,
)
from apps.execution.domain.lab_transport_models import (
    LocalLabCertificationVerifyResponse,
)
from apps.execution.lab_range_cache import get_active_lab_ranges, lab_range_cache
from apps.execution.notification_events import dispatch_critical_lab_alerts
from apps.execution.services.lab_toxicity_engine import (
    LabToxicityAssessment,
    LabToxicityEngine,
)
from apps.execution.ucum import convert_unit as ucum_convert_unit
from apps.execution.ucum import get_normalized_representation

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


def _get_val(obj: object | dict[str, Any], key: str, default: Any = None) -> Any:
    """Gracefully extracts attribute or dict item supporting synonym aliases."""
    if isinstance(obj, dict):
        val = obj.get(key)
        if val is not None:
            return val
        synonyms: dict[str, list[str]] = {
            "low_bound": [
                "range_low",
                "low",
                "ref_low",
                "reference_range_low",
                "lbornrlo",
                "lbstnrlo",
            ],
            "high_bound": [
                "range_high",
                "high",
                "ref_high",
                "reference_range_high",
                "lbornrhi",
                "lbstnrhi",
            ],
            "range_low": ["low_bound", "low", "ref_low", "reference_range_low"],
            "range_high": ["high_bound", "high", "ref_high", "reference_range_high"],
            "critical_low": ["crit_low", "panic_low", "criticallow"],
            "critical_high": ["crit_high", "panic_high", "criticalhigh"],
            "source": ["lab_source", "source_type"],
            "lab_source": ["source", "source_type"],
            "sex_applicability": ["sex", "gender"],
            "sex": ["sex_applicability", "gender"],
            "ethnicity_applicability": [
                "ethnicity",
                "race_ethnicity",
                "race",
            ],
            "ethnicity": [
                "ethnicity_applicability",
                "race_ethnicity",
                "race",
            ],
            "test_code": ["testcode", "paramcd", "lbtestcd", "code"],
            "test_name": ["testname", "lbtest", "param", "description"],
            "value": ["result", "lbstresn", "lborres", "numeric_value"],
            "unit": ["units", "lbstresu", "lborresu", "uom"],
            "site_id": ["site", "lab_site_id", "sitenumber"],
            "study_id": ["study", "protocol", "protocolid"],
            "subject_id": ["subject", "usubjid", "subjid", "patientid"],
            "visit_id": ["visit", "timepoint", "encounter"],
            "observation_date": ["collection_date", "lbdtc", "date"],
            "raw_abnormal_flag": [
                "abnormal_flag",
                "flag",
                "indicator",
                "lbnrind",
            ],
        }
        if key in synonyms:
            for syn in synonyms[key]:
                if syn in obj and obj[syn] is not None:
                    return obj[syn]
        return default

    val = getattr(obj, key, None)
    if val is not None:
        return val

    # Fallback to model property aliases if object is an ORM or Pydantic entity
    model_synonyms: dict[str, list[str]] = {
        "low_bound": ["reference_range_low", "range_low"],
        "high_bound": ["reference_range_high", "range_high"],
        "critical_low": ["crit_low"],
        "critical_high": ["crit_high"],
        "source": ["lab_source"],
        "lab_source": ["source"],
        "sex_applicability": ["sex", "gender"],
        "sex": ["sex_applicability", "gender"],
        "ethnicity_applicability": ["ethnicity"],
        "ethnicity": ["ethnicity_applicability"],
        "test_code": ["code"],
        "test_name": ["description"],
        "site_id": ["lab_site_id"],
        "raw_abnormal_flag": ["abnormal_flag", "flag", "lab_indicator"],
    }
    if key in model_synonyms:
        for syn in model_synonyms[key]:
            if hasattr(obj, syn):
                s_val = getattr(obj, syn)
                if s_val is not None:
                    return s_val

    return default


class LabAssessmentEngine:
    """Unified domain engine for laboratory clinical assessment, reference range matching,
    LOINC unit conversion, toxicity grading, Hy's Law compound assessment, acute delta checks,
    and automatic Adverse Event eCRF drafting."""

    @classmethod
    def resolve_loinc_mapping(
        cls, test_code: str, loinc_code: str | None = None
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

    @classmethod
    def get_loinc_catalog(cls) -> list[dict[str, Any]]:
        """Returns the standardized LOINC catalog items list."""
        items: list[dict[str, Any]] = []
        seen: set[str] = set()
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

    @classmethod
    def select_reference_range(
        cls,
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
        """Selects the best-matched active LabReferenceRange based on multi-dimensional
        demographic and operational specificity scoring (Age, Sex, Site, Ethnicity)."""
        candidates = []

        norm_sex = (
            normalize_gender(sex, preserve_custom=True) if sex is not None else "U"
        )
        norm_ethnicity = (
            str(ethnicity).strip().upper()
            if ethnicity and str(ethnicity).strip()
            else None
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
            else:
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
                    sex_score = 2
                elif r_sex in ("ALL", None, "", "U"):
                    sex_score = 1
            else:
                if r_sex in ("ALL", None, "", "U"):
                    sex_score = 1

            if sex_score == 0:
                continue

            # 5. Calculate Ethnicity Specificity Score
            r_ethnicity = _get_val(r, "ethnicity_applicability") or _get_val(
                r, "ethnicity"
            )
            if r_ethnicity:
                r_ethnicity = str(r_ethnicity).strip().upper()

            ethnicity_score = 0
            if norm_ethnicity and norm_ethnicity not in ("ALL", "U", "UNKNOWN"):
                if r_ethnicity == norm_ethnicity:
                    ethnicity_score = 2
                elif r_ethnicity in ("ALL", None, "", "U", "UNKNOWN"):
                    ethnicity_score = 1
            else:
                if r_ethnicity in ("ALL", None, "", "U", "UNKNOWN"):
                    ethnicity_score = 1

            if ethnicity_score == 0:
                continue

            # 6. Calculate Age Specificity Score
            r_age_low = _get_val(r, "age_low")
            r_age_high = _get_val(r, "age_high")

            age_low_val = float(r_age_low) if r_age_low is not None else None
            age_high_val = float(r_age_high) if r_age_high is not None else None

            age_score = 0
            if age is not None:
                matches_low = age_low_val is None or age_low_val <= age
                matches_high = age_high_val is None or age <= age_high_val
                if matches_low and matches_high:
                    if age_low_val is not None and age_high_val is not None:
                        age_score = 3
                    elif age_low_val is not None or age_high_val is not None:
                        age_score = 2
                    else:
                        age_score = 1
            else:
                if age_low_val is None and age_high_val is None:
                    age_score = 1

            if age_score == 0:
                continue

            # Tie-breaking details
            r_id = _get_val(r, "id")
            id_str = str(r_id) if r_id is not None else ""

            age_span = (
                (age_high_val - age_low_val)
                if (age_low_val is not None and age_high_val is not None)
                else float("inf")
            )

            r_low_bound = _get_val(r, "low_bound")
            low_bound_val = (
                float(r_low_bound) if r_low_bound is not None else float("-inf")
            )

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
                age_high_val if age_high_val is not None else float("inf"),
                low_bound_val,
                high_bound_val,
                id_str,
            )

            candidates.append((sort_key, r))

        if not candidates:
            return None

        candidates.sort(key=lambda item: item[0])
        return candidates[0][1]

    @classmethod
    def evaluate_reference_range(
        cls,
        value: float | None,
        reference_range: object | dict[str, Any] | None,
    ) -> tuple[str | None, bool, str | None]:
        """Evaluates numeric lab measurement against matched reference range bounds."""
        if reference_range is None:
            return None, False, None

        low_bound = _get_val(reference_range, "low_bound")
        high_bound = _get_val(reference_range, "high_bound")
        critical_low = _get_val(reference_range, "critical_low")
        critical_high = _get_val(reference_range, "critical_high")

        low_bound_val = float(low_bound) if low_bound is not None else None
        high_bound_val = float(high_bound) if high_bound is not None else None
        critical_low_val = float(critical_low) if critical_low is not None else None
        critical_high_val = float(critical_high) if critical_high is not None else None

        bounds_dict = {
            "low": low_bound_val,
            "high": high_bound_val,
        }
        matched_normal_bounds = json.dumps(bounds_dict)

        if value is None:
            return None, False, matched_normal_bounds

        value_val = float(value)

        # 1. Critical Low (Exclusive: value < critical_low)
        if critical_low_val is not None and value_val < critical_low_val:
            return "LOW LOW", True, matched_normal_bounds

        # 2. Critical High (Exclusive: value > critical_high)
        if critical_high_val is not None and value_val > critical_high_val:
            return "HIGH HIGH", True, matched_normal_bounds

        # 3. Normal Low (Inclusive normal bounds, so out of range is value < low_bound)
        if low_bound_val is not None and value_val < low_bound_val:
            return "LOW", True, matched_normal_bounds

        # 4. Normal High (Inclusive normal bounds, so out of range is value > high_bound)
        if high_bound_val is not None and value_val > high_bound_val:
            return "HIGH", True, matched_normal_bounds

        # 5. Normal
        return "NORMAL", False, matched_normal_bounds

    @classmethod
    async def convert_unit(
        cls,
        test_code: str,
        from_unit: str,
        to_unit: str,
        value: float,
        session: AsyncSession | None = None,
    ) -> float:
        """Converts a numeric laboratory measurement utilizing database rules, LOINC catalog, and UCUM."""
        if from_unit == to_unit:
            return value

        # 1. Check custom database unit conversion record
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
                logger.debug(f"Database unit conversion lookup skipped/failed: {e}")

        # 2. Check LOINC standard mathematical catalog
        loinc_entry = cls.resolve_loinc_mapping(test_code)
        if loinc_entry and "conversions" in loinc_entry:
            conv_map = loinc_entry["conversions"]
            if (from_unit, to_unit) in conv_map:
                factor, offset = conv_map[(from_unit, to_unit)]
                return value * factor + offset

        # 3. Fallback to generic UCUM conversion
        return ucum_convert_unit(value, from_unit, to_unit)

    @classmethod
    def evaluate_toxicity(
        cls,
        test_code: str,
        test_name: str,
        value: float,
        unit: str,
        ref_low: float | None = None,
        ref_high: float | None = None,
        baseline_value: float | None = None,
        grading_scale: str = "CTCAE_V5",
        alt_co_analyte: float | None = None,
        ast_co_analyte: float | None = None,
        bili_co_analyte: float | None = None,
        alkp_co_analyte: float | None = None,
    ) -> LabToxicityAssessment:
        """Evaluates CTCAE v5.0 / DAIDS v2.1 toxicity grading, Hy's Law, and delta alerts."""
        return LabToxicityEngine.evaluate_observation(
            test_code=test_code,
            test_name=test_name,
            value=value,
            unit=unit,
            ref_low=ref_low,
            ref_high=ref_high,
            baseline_value=baseline_value,
            grading_scale=grading_scale,
            alt_co_analyte=alt_co_analyte,
            ast_co_analyte=ast_co_analyte,
            bili_co_analyte=bili_co_analyte,
            alkp_co_analyte=alkp_co_analyte,
        )

    @classmethod
    def check_hys_law(
        cls,
        alt_val: float | None,
        ast_val: float | None,
        bili_val: float | None,
        alkp_val: float | None,
        alt_uln: float = 35.0,
        ast_uln: float = 35.0,
        bili_uln: float = 1.2,
        alkp_uln: float = 120.0,
    ) -> tuple[bool, str]:
        """Evaluates Hy's Law criteria for Drug-Induced Liver Injury (DILI)."""
        return LabToxicityEngine.check_hys_law(
            alt_val=alt_val,
            ast_val=ast_val,
            bili_val=bili_val,
            alkp_val=alkp_val,
            alt_uln=alt_uln,
            ast_uln=ast_uln,
            bili_uln=bili_uln,
            alkp_uln=alkp_uln,
        )

    @classmethod
    def check_delta(
        cls,
        value: float,
        baseline_value: float | None,
        grade: int = 0,
    ) -> tuple[bool, float | None]:
        """Evaluates acute baseline delta deviation (>= 50% deviation)."""
        if not baseline_value or baseline_value <= 0:
            return False, None
        delta_pct = round(((value - baseline_value) / baseline_value) * 100.0, 2)
        is_delta_alert = abs(delta_pct) >= 50.0 and grade >= 2
        return is_delta_alert, delta_pct

    @classmethod
    async def verify_local_lab_certification(
        cls,
        study_id: str,
        site_id: str,
        obs_date: datetime | str,
        session: AsyncSession,
    ) -> LocalLabCertificationVerifyResponse:
        """Verifies active accreditation status and expiration for a local laboratory facility."""
        if isinstance(obs_date, str):
            clean_obs_date = datetime.fromisoformat(obs_date).replace(tzinfo=None)
        elif obs_date.tzinfo is not None:
            clean_obs_date = obs_date.replace(tzinfo=None)
        else:
            clean_obs_date = obs_date

        stmt = select(LocalLabCertification).where(
            LocalLabCertification.study_id == study_id,
            LocalLabCertification.site_id == site_id,
            LocalLabCertification.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        certs = res.scalars().all()

        if not certs:
            return LocalLabCertificationVerifyResponse(
                is_valid=False,
                status="MISSING",
                site_id=site_id,
                study_id=study_id,
                uncalibrated_safeguard_triggered=True,
                message=f"No local lab certification registered for site {site_id}.",
            )

        active_cert = None
        for c in certs:
            if c.status == "ACTIVE" and c.is_accredited:
                if c.effective_date <= clean_obs_date <= c.expiration_date:
                    active_cert = c
                    break

        if active_cert:
            days_left = (active_cert.expiration_date - clean_obs_date).days
            return LocalLabCertificationVerifyResponse(
                is_valid=True,
                status="ACTIVE",
                site_id=site_id,
                study_id=study_id,
                certification_number=active_cert.certification_number,
                accreditation_body=active_cert.accreditation_body,
                expiration_date=active_cert.expiration_date,
                days_until_expiration=days_left,
                uncalibrated_safeguard_triggered=False,
                message=f"Certification is valid ({active_cert.accreditation_body} #{active_cert.certification_number}). {days_left} days until expiration.",
            )

        most_recent = sorted(certs, key=lambda x: x.expiration_date, reverse=True)[0]
        days_expired = (clean_obs_date - most_recent.expiration_date).days
        return LocalLabCertificationVerifyResponse(
            is_valid=False,
            status="EXPIRED" if days_expired > 0 else most_recent.status,
            site_id=site_id,
            study_id=study_id,
            certification_number=most_recent.certification_number,
            accreditation_body=most_recent.accreditation_body,
            expiration_date=most_recent.expiration_date,
            days_until_expiration=-days_expired if days_expired > 0 else 0,
            uncalibrated_safeguard_triggered=True,
            message=f"Local lab certification expired on {most_recent.expiration_date.strftime('%Y-%m-%d')}. Uncalibrated range safeguard active.",
        )

    @classmethod
    async def evaluate_laboratory_result(
        cls,
        observation: Any,
        context: LabAssessmentContext | dict[str, Any] | None = None,
    ) -> LabAssessmentResult:
        """Unified, deep assessment pipeline evaluating reference ranges, unit conversions,
        toxicity grading, Hy's Law, delta checks, and automated AE staging for an observation.

        Args:
            observation: ClinicalObservation ORM entity, RawLabRecord, dict, or observation payload.
            context: LabAssessmentContext or parameters dict containing execution session & subject metadata.

        Returns:
            LabAssessmentResult encapsulating normalized values, matched bounds, grades, and staged entities.
        """
        # 1. Parse context
        if isinstance(context, dict):
            ctx = LabAssessmentContext(**context)
        elif context is None:
            ctx = LabAssessmentContext()
        else:
            ctx = context

        # 2. Extract observation fields gracefully
        test_code_raw = _get_val(observation, "test_code") or _get_val(
            observation, "test"
        )
        test_code = str(test_code_raw).strip() if test_code_raw else "UNKNOWN"

        test_name_raw = _get_val(observation, "test_name") or _get_val(
            observation, "name"
        )
        test_name = (
            str(test_name_raw).strip()
            if test_name_raw and str(test_name_raw).strip()
            else test_code
        )

        val_raw = _get_val(observation, "value")
        val_str_raw = _get_val(observation, "value_string")
        unit_raw = _get_val(observation, "unit")

        value = float(val_raw) if val_raw is not None else None
        value_string = str(val_str_raw) if val_str_raw is not None else None
        unit = str(unit_raw).strip() if unit_raw else None

        obs_date_raw = _get_val(observation, "observation_date")
        if isinstance(obs_date_raw, datetime):
            obs_date = obs_date_raw.replace(tzinfo=None)
        else:
            obs_date = datetime.now(UTC).replace(tzinfo=None)

        lab_source = str(
            _get_val(observation, "lab_source", "CENTRAL") or "CENTRAL"
        ).upper()
        site_id = _get_val(observation, "site_id") or ctx.site_id
        study_id = _get_val(observation, "study_id") or ctx.study_id
        subject_id = _get_val(observation, "subject_id") or ctx.subject_id
        visit_id = _get_val(observation, "visit_id") or ctx.visit_id

        ref_low_obs = _get_val(observation, "reference_range_low")
        ref_high_obs = _get_val(observation, "reference_range_high")
        crit_low_obs = _get_val(observation, "critical_low")
        crit_high_obs = _get_val(observation, "critical_high")
        raw_flag = _get_val(observation, "raw_abnormal_flag")

        # 3. Subject Demographics & Consent Resolution
        subj_db = ctx.subject
        if subj_db is None and ctx.session is not None and subject_id:
            if study_id:
                stmt_subj = select(ClinicalSubject).where(
                    ClinicalSubject.subject_id == subject_id,
                    ClinicalSubject.study_id == study_id,
                    ClinicalSubject.is_deleted.is_(False),
                )
            else:
                stmt_subj = select(ClinicalSubject).where(
                    ClinicalSubject.subject_id == subject_id,
                    ClinicalSubject.is_deleted.is_(False),
                )
            res_subj = await ctx.session.execute(stmt_subj)
            subj_db = res_subj.scalars().first()

        if subj_db and not study_id:
            study_id = subj_db.study_id
        if subj_db and not site_id:
            site_id = subj_db.site_id

        effective_study_id = study_id or "STUDY-DEFAULT"
        effective_site_id = site_id

        # Demographic extraction (Gender, Age, Ethnicity)
        gender = "U"
        age = None
        ethnicity = None

        if ctx.demographics:
            gender = (
                ctx.demographics.get("gender")
                or ctx.demographics.get("sex")
                or ctx.sex
                or "U"
            )
            age = (
                float(ctx.demographics["age"])
                if ctx.demographics.get("age") is not None
                else ctx.age
            )
            ethnicity = ctx.demographics.get("ethnicity") or ctx.ethnicity
        elif subj_db:
            demo = get_safe_demographics(subj_db, obs_date, preserve_custom=True)
            gender = demo.get("gender", "U")
            age = demo.get("age")
            ethnicity = demo.get("ethnicity")
        else:
            gender = ctx.sex or "U"
            age = ctx.age
            ethnicity = ctx.ethnicity

        # Protocol consent version stamping
        protocol_version_tag = ctx.protocol_version_tag
        protocol_version_index = ctx.protocol_version_index
        if (
            protocol_version_tag is None
            and ctx.session is not None
            and subject_id
            and effective_study_id
        ):
            stmt_consent = (
                select(SubjectConsent)
                .where(
                    SubjectConsent.subject_id == subject_id,
                    SubjectConsent.study_id == effective_study_id,
                    SubjectConsent.icf_signed.is_(True),
                )
                .order_by(SubjectConsent.version_index.desc())
            )
            res_consent = await ctx.session.execute(stmt_consent)
            active_consent = res_consent.scalars().first()
            if active_consent:
                protocol_version_tag = active_consent.version_tag
                protocol_version_index = active_consent.version_index

        # 4. Local Lab Certification & Uncalibrated Range Safeguard
        is_uncalibrated_safeguard = False
        safeguard_message = None

        if lab_source == "LOCAL" and effective_site_id and ctx.session is not None:
            cert_res = await cls.verify_local_lab_certification(
                study_id=effective_study_id,
                site_id=effective_site_id,
                obs_date=obs_date,
                session=ctx.session,
            )
            if not cert_res.is_valid:
                is_uncalibrated_safeguard = True
                safeguard_message = cert_res.message

        # 5. Catalog Lookup & Unit Conversion
        target_unit = None
        if ctx.session is not None and effective_study_id:
            stmt_master = select(LabTestMaster).where(
                LabTestMaster.study_id == effective_study_id,
                LabTestMaster.test_code == test_code,
                LabTestMaster.is_deleted.is_(False),
            )
            res_master = await ctx.session.execute(stmt_master)
            master = res_master.scalars().first()
            if master:
                if not test_name or test_name == test_code:
                    test_name = master.test_name
                target_unit = master.normalized_unit

        if not test_name or test_name == test_code:
            loinc_info = cls.resolve_loinc_mapping(test_code)
            if loinc_info:
                test_name = loinc_info.get("test_name", test_code)

        test_name = test_name or test_code

        # Unit Conversion & Normalization
        norm_val = value
        norm_unit = unit

        if value is not None and unit:
            if target_unit and target_unit != unit:
                try:
                    converted_val = await cls.convert_unit(
                        test_code=test_code,
                        from_unit=unit,
                        to_unit=target_unit,
                        value=value,
                        session=ctx.session,
                    )
                    norm_val = converted_val
                    norm_unit = target_unit
                except Exception as conv_err:
                    logger.warning(
                        f"Conversion to target unit '{target_unit}' failed ({conv_err}); using standard UCUM fallback"
                    )
                    norm_val, norm_unit = get_normalized_representation(value, unit)
            else:
                norm_val, norm_unit = get_normalized_representation(value, unit)

        # 6. Reference Range Matching & Evaluation
        if ctx.reference_ranges is not None:
            ranges = ctx.reference_ranges
        elif ctx.session is not None and effective_study_id:
            ranges = await get_active_lab_ranges(
                lab_range_cache, ctx.session, effective_study_id, test_code
            )
        else:
            ranges = []

        matched_range = cls.select_reference_range(
            ranges=ranges,
            study_id=effective_study_id,
            test_code=test_code,
            normalized_unit=norm_unit or "",
            lab_source=lab_source,
            sex=gender,
            age=age,
            site_id=effective_site_id,
            ethnicity=ethnicity,
        )

        ref_low = float(ref_low_obs) if ref_low_obs is not None else None
        ref_high = float(ref_high_obs) if ref_high_obs is not None else None
        critical_low = float(crit_low_obs) if crit_low_obs is not None else None
        critical_high = float(crit_high_obs) if crit_high_obs is not None else None

        if matched_range:
            indicator, out_of_range, matched_bounds = cls.evaluate_reference_range(
                norm_val, matched_range
            )
            r_low = _get_val(matched_range, "low_bound")
            r_high = _get_val(matched_range, "high_bound")
            r_crit_low = _get_val(matched_range, "critical_low")
            r_crit_high = _get_val(matched_range, "critical_high")

            if r_low is not None:
                ref_low = float(r_low)
            if r_high is not None:
                ref_high = float(r_high)
            if r_crit_low is not None:
                critical_low = float(r_crit_low)
            if r_crit_high is not None:
                critical_high = float(r_crit_high)
        else:
            if (
                ref_low_obs is not None
                or ref_high_obs is not None
                or crit_low_obs is not None
                or crit_high_obs is not None
            ):
                ad_hoc_range = {
                    "low_bound": ref_low_obs,
                    "high_bound": ref_high_obs,
                    "critical_low": crit_low_obs,
                    "critical_high": crit_high_obs,
                }
                indicator, out_of_range, matched_bounds = cls.evaluate_reference_range(
                    norm_val, ad_hoc_range
                )
            else:
                indicator = None
                out_of_range = False
                matched_bounds = None

            if raw_flag:
                flag_u = str(raw_flag).strip().upper()
                if flag_u in ("LL", "CRIT", "PANIC", "CRITICAL_LOW"):
                    indicator = "LOW LOW"
                    out_of_range = True
                elif flag_u in ("HH", "CRITICAL_HIGH"):
                    indicator = "HIGH HIGH"
                    out_of_range = True
                elif flag_u in ("L", "LOW") and not indicator:
                    indicator = "LOW"
                    out_of_range = True
                elif flag_u in ("H", "HIGH") and not indicator:
                    indicator = "HIGH"
                    out_of_range = True
                elif flag_u in ("A", "ABNORMAL", "AA") and not indicator:
                    indicator = "ABNORMAL"
                    out_of_range = True

        # 7. Baseline Observation & Acute Delta Checking
        baseline_val = ctx.baseline_value
        if (
            baseline_val is None
            and ctx.session is not None
            and subject_id
            and effective_study_id
        ):
            stmt_baseline = (
                select(ClinicalObservation)
                .where(
                    ClinicalObservation.subject_id == subject_id,
                    ClinicalObservation.study_id == effective_study_id,
                    ClinicalObservation.test_code == test_code,
                    ClinicalObservation.is_deleted.is_(False),
                )
                .order_by(ClinicalObservation.observation_date.asc())
            )
            res_baseline = await ctx.session.execute(stmt_baseline)
            first_obs = res_baseline.scalars().first()
            if first_obs and first_obs.normalized_value is not None:
                baseline_val = float(first_obs.normalized_value)

        # 8. Toxicity Grading (CTCAE v5.0 / DAIDS) & Hy's Law Assessment
        co_analytes = dict(ctx.co_analytes)
        obs_co = _get_val(observation, "co_analytes")
        if isinstance(obs_co, dict):
            co_analytes.update(obs_co)

        grading_scale_str = (
            ctx.grading_scale.value
            if hasattr(ctx.grading_scale, "value")
            else str(ctx.grading_scale)
        )

        tox_assessment = cls.evaluate_toxicity(
            test_code=test_code,
            test_name=test_name,
            value=norm_val if norm_val is not None else (value or 0.0),
            unit=norm_unit or unit or "",
            ref_low=ref_low,
            ref_high=ref_high,
            baseline_value=baseline_val,
            grading_scale=grading_scale_str,
            alt_co_analyte=co_analytes.get("ALT"),
            ast_co_analyte=co_analytes.get("AST"),
            bili_co_analyte=co_analytes.get("BILI") or co_analytes.get("TBIL"),
            alkp_co_analyte=co_analytes.get("ALKP") or co_analytes.get("ALP"),
        )

        is_critical = (
            indicator in ("LOW LOW", "HIGH HIGH")
            or tox_assessment.is_critical_ae_trigger
            or int(tox_assessment.ctcae_grade) >= 3
        )
        is_outlier = out_of_range or int(tox_assessment.ctcae_grade) >= 3

        # 9. Build or update ClinicalObservation ORM entity
        if isinstance(observation, ClinicalObservation):
            obs_record = observation
            obs_record.study_id = effective_study_id
            obs_record.site_id = effective_site_id
            obs_record.test_name = test_name
            obs_record.normalized_value = norm_val
            obs_record.normalized_unit = norm_unit
            obs_record.is_outlier = is_outlier
            obs_record.lab_source = lab_source
            obs_record.lab_site_id = effective_site_id
            obs_record.lab_indicator = indicator
            obs_record.lab_out_of_range = out_of_range
            obs_record.matched_normal_bounds = matched_bounds
            obs_record.reference_range_low = ref_low
            obs_record.reference_range_high = ref_high
            obs_record.protocol_version_tag = protocol_version_tag
            obs_record.protocol_version_index = protocol_version_index
            existing_props = dict(obs_record.additional_properties or {})
            existing_props.update(
                {
                    "ctcae_grade": int(tox_assessment.ctcae_grade),
                    "toxicity_description": tox_assessment.toxicity_description,
                    "grading_scale": tox_assessment.grading_scale,
                    "is_hys_law_warning": tox_assessment.is_hys_law_warning,
                    "is_delta_check_alert": tox_assessment.is_delta_check_alert,
                    "delta_percentage": tox_assessment.delta_percentage,
                    "uncalibrated_safeguard_triggered": is_uncalibrated_safeguard,
                }
            )
            obs_record.additional_properties = existing_props
            obs_record.version += 1
        else:
            obs_record = ClinicalObservation(
                subject_id=subject_id or "UNKNOWN",
                study_id=effective_study_id,
                site_id=effective_site_id,
                visit_id=visit_id,
                domain="LB",
                observation_date=obs_date,
                test_code=test_code,
                test_name=test_name,
                value=value,
                value_string=value_string,
                unit=unit,
                normalized_value=norm_val,
                normalized_unit=norm_unit,
                is_outlier=is_outlier,
                lab_source=lab_source,
                lab_site_id=effective_site_id,
                lab_indicator=indicator,
                lab_out_of_range=out_of_range,
                matched_normal_bounds=matched_bounds,
                reference_range_low=ref_low,
                reference_range_high=ref_high,
                protocol_version_tag=protocol_version_tag,
                protocol_version_index=protocol_version_index,
                additional_properties={
                    "ctcae_grade": int(tox_assessment.ctcae_grade),
                    "toxicity_description": tox_assessment.toxicity_description,
                    "grading_scale": tox_assessment.grading_scale,
                    "is_hys_law_warning": tox_assessment.is_hys_law_warning,
                    "is_delta_check_alert": tox_assessment.is_delta_check_alert,
                    "delta_percentage": tox_assessment.delta_percentage,
                    "uncalibrated_safeguard_triggered": is_uncalibrated_safeguard,
                },
            )

        # 10. Construct Staged AE and Discrepancy Queries
        queries_raised: list[ClinicalQuery] = []
        form_submission_record: FormSubmission | None = None
        staged_review_record: StagedAdverseEventReview | None = None
        ae_payload: dict[str, Any] | None = None
        dispatched_alert: str | None = None

        if is_uncalibrated_safeguard and safeguard_message and ctx.auto_create_queries:
            query_safeguard = ClinicalQuery(
                id=str(uuid.uuid4()),
                study_id=effective_study_id,
                site_id=effective_site_id,
                subject_id=subject_id or "UNKNOWN",
                visit_id=visit_id,
                domain="LB",
                test_code=test_code,
                observation_id=getattr(obs_record, "id", None),
                status="OPEN",
                priority="HIGH",
                query_type="UNCALIBRATED_LOCAL_LAB_WARNING",
                origin="SYSTEM_LAB_INGESTION",
                explanation="Uncalibrated or expired local lab certification safeguard",
                message=safeguard_message,
                created_by=ctx.user_id,
                created_at=datetime.now(UTC).replace(tzinfo=None),
            )
            queries_raised.append(query_safeguard)

        if tox_assessment.is_critical_ae_trigger:
            ae_term = test_name + (
                " (Hy's Law DILI Alert)"
                if tox_assessment.is_hys_law_warning
                else f" Abnormality (Grade {int(tox_assessment.ctcae_grade)})"
            )
            ae_payload = {
                "aeterm": ae_term,
                "aestdat": obs_date.isoformat(),
                "aetoxgr": f"Grade {int(tox_assessment.ctcae_grade)}",
                "aetoxscale": tox_assessment.grading_scale,
                "aeser": "Y"
                if int(tox_assessment.ctcae_grade) >= 4
                or tox_assessment.is_hys_law_warning
                else "N",
                "lab_test_code": test_code,
                "lab_test_name": test_name,
                "lab_value": value,
                "lab_unit": unit,
                "observation_id": getattr(obs_record, "id", None),
                "is_auto_drafted": True,
                "staging_status": "STAGED_FOR_INVESTIGATOR_REVIEW",
                "clinical_significance": "POTENTIAL_LAB_ABNORMALITY_AE",
                "hys_law_dili_flag": tox_assessment.is_hys_law_warning,
                "delta_check_alert": tox_assessment.is_delta_check_alert,
            }

            if ctx.auto_stage_ae:
                form_submission_record = FormSubmission(
                    study_id=effective_study_id,
                    site_id=effective_site_id,
                    subject_id=subject_id or "UNKNOWN",
                    visit_id=visit_id,
                    form_id="AE",
                    status="DRAFT",
                    payload=ae_payload,
                    protocol_version=protocol_version_tag,
                )

                staged_review_record = StagedAdverseEventReview(
                    study_id=effective_study_id,
                    site_id=effective_site_id,
                    subject_id=subject_id or "UNKNOWN",
                    visit_id=visit_id,
                    observation_id=getattr(obs_record, "id", None),
                    form_submission_id=getattr(form_submission_record, "id", None),
                    test_code=test_code,
                    test_name=test_name,
                    lab_value=value or 0.0,
                    unit=unit or "",
                    ctcae_grade=int(tox_assessment.ctcae_grade),
                    grading_scale=tox_assessment.grading_scale,
                    toxicity_term=ae_term,
                    clinical_description=tox_assessment.toxicity_description,
                    is_hys_law_warning=tox_assessment.is_hys_law_warning,
                    is_delta_check_alert=tox_assessment.is_delta_check_alert,
                    delta_percentage=tox_assessment.delta_percentage,
                    review_status="STAGED_FOR_INVESTIGATOR_REVIEW",
                    reason_for_change=ctx.change_reason,
                    created_by=ctx.user_id,
                )

                ae_qtype = (
                    "POTENTIAL_SAE_CRITICAL"
                    if indicator in ("LOW LOW", "HIGH HIGH")
                    else "GRADE_3_4_AE_TRIGGER"
                )
                ae_qexpl = (
                    "Potential SAE critical threshold alert staged for Investigator review"
                    if indicator in ("LOW LOW", "HIGH HIGH")
                    else "Automated CTCAE Grade 3/4 AE Trigger staged for Investigator review"
                )
                query_ae = ClinicalQuery(
                    id=str(uuid.uuid4()),
                    study_id=effective_study_id,
                    site_id=effective_site_id,
                    subject_id=subject_id or "UNKNOWN",
                    visit_id=visit_id,
                    domain="LB",
                    test_code=test_code,
                    observation_id=getattr(obs_record, "id", None),
                    status="OPEN",
                    priority="CRITICAL",
                    query_type=ae_qtype,
                    origin="SYSTEM_TOXICITY_TRIGGER",
                    explanation=ae_qexpl,
                    message=(
                        f"Grade {int(tox_assessment.ctcae_grade)} Laboratory Toxicity Trigger for {test_name}: "
                        f"Value {value} {unit or ''}. {tox_assessment.toxicity_description}. "
                        f"Staged AE eCRF draft auto-created for Investigator review."
                    ),
                    created_by=ctx.user_id,
                    created_at=datetime.now(UTC).replace(tzinfo=None),
                )
                staged_review_record.query_id = query_ae.id
                queries_raised.append(query_ae)

        elif (
            out_of_range or indicator in ("LOW", "HIGH", "LOW LOW", "HIGH HIGH")
        ) and ctx.auto_create_queries:
            is_crit_ind = indicator in ("LOW LOW", "HIGH HIGH")
            query_type = (
                "POTENTIAL_SAE_CRITICAL" if is_crit_ind else "OUT_OF_RANGE_WARNING"
            )
            priority = "CRITICAL" if is_crit_ind else "HIGH"
            query_msg = (
                f"Laboratory observation for test {test_code} ({test_name}) "
                f"with value {value} {unit or ''} is out of normal "
                f"reference range [{matched_bounds or 'N/A'}]. Evaluated indicator: {indicator}."
            )
            query_out = ClinicalQuery(
                id=str(uuid.uuid4()),
                study_id=effective_study_id,
                site_id=effective_site_id,
                subject_id=subject_id or "UNKNOWN",
                visit_id=visit_id,
                domain="LB",
                test_code=test_code,
                observation_id=getattr(obs_record, "id", None),
                status="OPEN",
                priority=priority,
                query_type=query_type,
                origin="SYSTEM_LAB_INGESTION",
                explanation="Automated discrepancy query generated during lab assessment",
                message=query_msg,
                created_by=ctx.user_id,
                created_at=datetime.now(UTC).replace(tzinfo=None),
            )
            queries_raised.append(query_out)

        # 11. Persistence handling if requested
        if ctx.persist_records and ctx.session is not None:
            ctx.session.add(obs_record)
            await ctx.session.flush()

            for q in queries_raised:
                q.observation_id = obs_record.id

            if form_submission_record:
                form_payload = dict(form_submission_record.payload or {})
                form_payload["observation_id"] = obs_record.id
                form_submission_record.payload = form_payload
                ctx.session.add(form_submission_record)
                await ctx.session.flush()

                if staged_review_record:
                    staged_review_record.observation_id = obs_record.id
                    staged_review_record.form_submission_id = form_submission_record.id
                    ctx.session.add(staged_review_record)

            for q in queries_raised:
                ctx.session.add(q)

        # 12. Notification dispatching
        if ctx.background_tasks is not None:
            if tox_assessment.is_critical_ae_trigger:
                alert_tag = f"GRADE_{int(tox_assessment.ctcae_grade)}_AE_TRIGGER"
                dispatch_critical_lab_alerts(
                    ctx.background_tasks,
                    obs_record,
                    alert_tag,
                    ctx.user_id,
                    ctx.change_reason,
                )
                dispatched_alert = alert_tag
            elif indicator in ("LOW LOW", "HIGH HIGH"):
                dispatch_critical_lab_alerts(
                    ctx.background_tasks,
                    obs_record,
                    indicator,
                    ctx.user_id,
                    ctx.change_reason,
                )
                dispatched_alert = indicator

        return LabAssessmentResult(
            test_code=test_code,
            test_name=test_name,
            original_value=value,
            original_value_string=value_string,
            original_unit=unit,
            normalized_value=norm_val,
            normalized_unit=norm_unit,
            matched_reference_range=matched_range,
            matched_normal_bounds=matched_bounds,
            ref_low=ref_low,
            ref_high=ref_high,
            critical_low=critical_low,
            critical_high=critical_high,
            indicator=indicator,
            out_of_range=out_of_range,
            is_critical=is_critical,
            is_outlier=is_outlier,
            ctcae_grade=int(tox_assessment.ctcae_grade),
            grading_scale=tox_assessment.grading_scale,
            toxicity_description=tox_assessment.toxicity_description,
            is_critical_ae_trigger=tox_assessment.is_critical_ae_trigger,
            is_hys_law_warning=tox_assessment.is_hys_law_warning,
            is_delta_check_alert=tox_assessment.is_delta_check_alert,
            delta_percentage=tox_assessment.delta_percentage,
            uncalibrated_safeguard_triggered=is_uncalibrated_safeguard,
            safeguard_message=safeguard_message,
            staged_ae_payload=ae_payload,
            observation_record=obs_record,
            form_submission_record=form_submission_record,
            staged_review_record=staged_review_record,
            queries_raised=queries_raised,
            dispatched_alert_type=dispatched_alert,
        )

    @classmethod
    async def recalculate_cohort_range_flags(
        cls,
        session: AsyncSession,
        study_id: str,
        test_code: str,
        background_tasks: Any | None = None,
    ) -> int:
        """Recalculates reference range indicators across all observations in a study-test cohort."""
        from apps.execution.database.context import (
            current_change_reason,
            current_user_id,
        )

        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.study_id == study_id,
            ClinicalObservation.test_code == test_code,
            ClinicalObservation.is_deleted.is_(False),
        )
        res_obs = await session.execute(stmt_obs)
        observations = res_obs.scalars().all()

        if not observations:
            return 0

        stmt_ranges = select(LabReferenceRange).where(
            LabReferenceRange.study_id == study_id,
            LabReferenceRange.test_code == test_code,
            LabReferenceRange.is_deleted.is_(False),
        )
        res_ranges = await session.execute(stmt_ranges)
        ranges = res_ranges.scalars().all()

        stmt_subjects = select(ClinicalSubject).where(
            ClinicalSubject.study_id == study_id,
            ClinicalSubject.is_deleted.is_(False),
        )
        res_subjects = await session.execute(stmt_subjects)
        subjects = {subj.subject_id: subj for subj in res_subjects.scalars().all()}

        updated_count = 0
        user_id = current_user_id.get() or "system_recalculation"
        change_reason = (
            current_change_reason.get() or "Batch laboratory range recalculation"
        )

        for obs in observations:
            obs_date = obs.observation_date or datetime.now(UTC).replace(tzinfo=None)
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

            matched_range = cls.select_reference_range(
                ranges=ranges,
                study_id=study_id,
                test_code=test_code,
                normalized_unit=obs.normalized_unit or "",
                lab_source=obs.lab_source or "CENTRAL",
                sex=gender,
                age=age,
                site_id=obs.lab_site_id,
                ethnicity=ethnicity,
            )

            indicator, out_of_range, matched_bounds = cls.evaluate_reference_range(
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

                if (
                    indicator in ("LOW LOW", "HIGH HIGH")
                    and background_tasks is not None
                ):
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


# Standalone functional entry points for top-level elegance & backward compatibility
async def evaluate_laboratory_result(
    observation: Any,
    context: LabAssessmentContext | dict[str, Any] | None = None,
) -> LabAssessmentResult:
    """Evaluate a laboratory observation utilizing the unified LabAssessmentEngine."""
    return await LabAssessmentEngine.evaluate_laboratory_result(observation, context)


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
    """Selects an active LabReferenceRange based on multi-dimensional specificity scoring."""
    return LabAssessmentEngine.select_reference_range(
        ranges=ranges,
        study_id=study_id,
        test_code=test_code,
        normalized_unit=normalized_unit,
        lab_source=lab_source,
        sex=sex,
        age=age,
        site_id=site_id,
        ethnicity=ethnicity,
    )


def evaluate_lab_value(
    value: float | None,
    reference_range: object | dict[str, Any] | None,
) -> tuple[str | None, bool, str | None]:
    """Evaluates a numeric laboratory value against a reference range."""
    return LabAssessmentEngine.evaluate_reference_range(value, reference_range)


async def convert_lab_unit(
    session: Any,
    test_code: str,
    from_unit: str,
    to_unit: str,
    value: float,
) -> float:
    """Converts a numeric laboratory value between measurement units."""
    return await LabAssessmentEngine.convert_unit(
        test_code=test_code,
        from_unit=from_unit,
        to_unit=to_unit,
        value=value,
        session=session,
    )


def resolve_loinc_mapping(
    test_code: str, loinc_code: str | None = None
) -> dict[str, Any] | None:
    """Resolves LOINC dictionary metadata for a given lab test code."""
    return LabAssessmentEngine.resolve_loinc_mapping(test_code, loinc_code)


def get_loinc_catalog() -> list[dict[str, Any]]:
    """Returns the standardized LOINC catalog items list."""
    return LabAssessmentEngine.get_loinc_catalog()
