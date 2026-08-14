"""Automated eCRF Layout Synthesis Engine for CDISC USDM and CDASH.

Transforms USDM protocol specifications, Activity nodes, and Biomedical Concepts
with Value-Level Metadata (VLM) into production-ready, responsive CDASH eCRF forms,
UI widget representations (text, numeric, select, vas_slider, body_map_74_zone),
and declarative validation / edit check rules.

Requirements: PRD-CRF-004, PRD-DDF-001, PRD-SYS-001, PRD-MDR-007
"""

from __future__ import annotations

import contextlib
import copy
import logging
import re
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field

from apps.designer.domain.cdisc.usdm_models import (
    BiomedicalConceptProperty,
    USDMStudy,
)
from apps.designer.domain.digitization_models import (
    SynthesizedECRFForm,
    USDMProtocolExtractionResponse,
)

logger = logging.getLogger(__name__)


# =========================================================================
# DOMAIN MODELS FOR SYNTHESIZED LAYOUT ELEMENTS
# =========================================================================


class SynthesizedField(BaseModel):
    """Represents a concrete synthesized UI field in an eCRF layout."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", validate_assignment=True
    )

    field_id: str = Field(..., description="Unique field identifier e.g. 'VS_SYSBP'")
    label: str = Field(..., description="Human-readable field label")
    data_type: str = Field(
        ...,
        description="Widget data type e.g. 'integer', 'decimal', 'text', 'choice', 'vas_slider', 'body_map_74_zone', 'boolean'",
    )
    cdash_variable: str | None = Field(
        None, description="CDASH variable mapping e.g. 'VS.SYSBP'"
    )
    mandatory: bool = Field(False, description="Whether field is required")
    range: dict[str, Any] | str | None = Field(
        None, description="Min/max boundary constraints"
    )
    options: list[str] = Field(
        default_factory=list, description="Choice options for select/radio widgets"
    )
    config: dict[str, Any] = Field(
        default_factory=dict, description="Widget-specific configuration parameters"
    )
    grid_span: int = Field(
        12, description="Responsive 12-column grid span (12, 8, 6, 4)"
    )
    unit: str | None = Field(None, description="Physical measurement unit")
    help_text: str | None = Field(
        None, description="Clinical guidance or instruction text"
    )


class SynthesizedRule(BaseModel):
    """Represents a declarative validation or edit check rule synthesized for an eCRF."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", validate_assignment=True
    )

    rule_id: str = Field(
        ..., description="Unique rule identifier e.g. 'CHK_VS_BP_SANITY'"
    )
    type: Literal[
        "cross_field_check",
        "range_check",
        "safety_alert",
        "mandatory_check",
        "skip_logic",
        "consistency_check",
    ] = Field("cross_field_check", description="Rule classification")
    condition: str = Field(
        ...,
        description="Declarative boolean condition expression e.g. 'VS_SYSBP > VS_DIABP'",
    )
    query_message: str = Field(
        ..., description="Clinical query text raised on condition failure"
    )
    target_field: str | None = Field(
        None, description="Primary field targeted by this rule"
    )
    severity: Literal["ERROR", "WARNING", "INFO"] = Field(
        "ERROR", description="Rule violation severity level"
    )


# =========================================================================
# STANDARD CDASH DOMAIN CATALOG
# =========================================================================

STANDARD_CDASH_CATALOG: dict[str, dict[str, Any]] = {
    "VS": {
        "form_name": "Vital Signs eCRF",
        "items": [
            {
                "field_id": "VS_SYSBP",
                "label": "Systolic Blood Pressure (mmHg)",
                "data_type": "integer",
                "cdash_variable": "VS.SYSBP",
                "mandatory": True,
                "range": {"min": 60, "max": 250},
                "grid_span": 6,
                "unit": "mmHg",
            },
            {
                "field_id": "VS_DIABP",
                "label": "Diastolic Blood Pressure (mmHg)",
                "data_type": "integer",
                "cdash_variable": "VS.DIABP",
                "mandatory": True,
                "range": {"min": 30, "max": 150},
                "grid_span": 6,
                "unit": "mmHg",
            },
            {
                "field_id": "VS_PULSE",
                "label": "Pulse Rate (beats/min)",
                "data_type": "integer",
                "cdash_variable": "VS.PULSE",
                "mandatory": True,
                "range": {"min": 30, "max": 220},
                "grid_span": 4,
                "unit": "beats/min",
            },
            {
                "field_id": "VS_TEMP",
                "label": "Body Temperature (C)",
                "data_type": "decimal",
                "cdash_variable": "VS.TEMP",
                "mandatory": False,
                "range": {"min": 34.0, "max": 43.0},
                "grid_span": 4,
                "unit": "C",
            },
            {
                "field_id": "VS_RESP",
                "label": "Respiratory Rate (breaths/min)",
                "data_type": "integer",
                "cdash_variable": "VS.RESP",
                "mandatory": False,
                "range": {"min": 8, "max": 60},
                "grid_span": 4,
                "unit": "breaths/min",
            },
            {
                "field_id": "VS_WEIGHT",
                "label": "Weight (kg)",
                "data_type": "decimal",
                "cdash_variable": "VS.WEIGHT",
                "mandatory": False,
                "range": {"min": 20.0, "max": 300.0},
                "grid_span": 6,
                "unit": "kg",
            },
            {
                "field_id": "VS_HEIGHT",
                "label": "Height (cm)",
                "data_type": "decimal",
                "cdash_variable": "VS.HEIGHT",
                "mandatory": False,
                "range": {"min": 50.0, "max": 250.0},
                "grid_span": 6,
                "unit": "cm",
            },
            {
                "field_id": "VS_COMM",
                "label": "Vital Signs Comments",
                "data_type": "text",
                "cdash_variable": "VS.VSCOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "EG": {
        "form_name": "12-Lead Electrocardiogram eCRF",
        "items": [
            {
                "field_id": "EG_HR",
                "label": "Heart Rate (bpm)",
                "data_type": "integer",
                "cdash_variable": "EG.EGHR",
                "mandatory": True,
                "range": {"min": 30, "max": 220},
                "grid_span": 4,
                "unit": "bpm",
            },
            {
                "field_id": "EG_PR",
                "label": "PR Interval (ms)",
                "data_type": "integer",
                "cdash_variable": "EG.EGPR",
                "mandatory": True,
                "range": {"min": 80, "max": 400},
                "grid_span": 4,
                "unit": "ms",
            },
            {
                "field_id": "EG_QRS",
                "label": "QRS Duration (ms)",
                "data_type": "integer",
                "cdash_variable": "EG.EGQRS",
                "mandatory": True,
                "range": {"min": 40, "max": 200},
                "grid_span": 4,
                "unit": "ms",
            },
            {
                "field_id": "EG_QT",
                "label": "QT Interval (ms)",
                "data_type": "integer",
                "cdash_variable": "EG.EGQT",
                "mandatory": False,
                "range": {"min": 200, "max": 700},
                "grid_span": 4,
                "unit": "ms",
            },
            {
                "field_id": "EG_QTC",
                "label": "QTc (Fridericia) Interval (ms)",
                "data_type": "integer",
                "cdash_variable": "EG.EGQTC",
                "mandatory": True,
                "range": {"min": 250, "max": 600},
                "grid_span": 6,
                "unit": "ms",
            },
            {
                "field_id": "EG_ORRES",
                "label": "Overall Interpretation",
                "data_type": "choice",
                "cdash_variable": "EG.EGORRES",
                "options": [
                    "Normal",
                    "Abnormal, Not Clinically Significant",
                    "Abnormal, Clinically Significant",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "EG_COMM",
                "label": "ECG Interpretation Comments",
                "data_type": "text",
                "cdash_variable": "EG.EGCOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "LB": {
        "form_name": "Safety Laboratory Chemistry & Hematology",
        "items": [
            {
                "field_id": "LB_HGB",
                "label": "Hemoglobin (g/dL)",
                "data_type": "decimal",
                "cdash_variable": "LB.HGB",
                "mandatory": True,
                "range": {"min": 3.0, "max": 25.0},
                "grid_span": 4,
                "unit": "g/dL",
            },
            {
                "field_id": "LB_WBC",
                "label": "White Blood Cell Count (10^9/L)",
                "data_type": "decimal",
                "cdash_variable": "LB.WBC",
                "mandatory": True,
                "range": {"min": 0.5, "max": 100.0},
                "grid_span": 4,
                "unit": "10^9/L",
            },
            {
                "field_id": "LB_PLT",
                "label": "Platelet Count (10^9/L)",
                "data_type": "integer",
                "cdash_variable": "LB.PLAT",
                "mandatory": True,
                "range": {"min": 10, "max": 1500},
                "grid_span": 4,
                "unit": "10^9/L",
            },
            {
                "field_id": "LB_ALT",
                "label": "Alanine Aminotransferase (ALT) (U/L)",
                "data_type": "integer",
                "cdash_variable": "LB.ALT",
                "mandatory": True,
                "range": {"min": 1, "max": 2000},
                "grid_span": 4,
                "unit": "U/L",
            },
            {
                "field_id": "LB_AST",
                "label": "Aspartate Aminotransferase (AST) (U/L)",
                "data_type": "integer",
                "cdash_variable": "LB.AST",
                "mandatory": False,
                "range": {"min": 1, "max": 2000},
                "grid_span": 4,
                "unit": "U/L",
            },
            {
                "field_id": "LB_CREAT",
                "label": "Serum Creatinine (mg/dL)",
                "data_type": "decimal",
                "cdash_variable": "LB.CREAT",
                "mandatory": True,
                "range": {"min": 0.1, "max": 20.0},
                "grid_span": 4,
                "unit": "mg/dL",
            },
            {
                "field_id": "LB_TBIL",
                "label": "Total Bilirubin (mg/dL)",
                "data_type": "decimal",
                "cdash_variable": "LB.TBIL",
                "mandatory": False,
                "range": {"min": 0.1, "max": 30.0},
                "grid_span": 4,
                "unit": "mg/dL",
            },
            {
                "field_id": "LB_GLUC",
                "label": "Glucose (mg/dL)",
                "data_type": "decimal",
                "cdash_variable": "LB.GLUC",
                "mandatory": False,
                "range": {"min": 20.0, "max": 800.0},
                "grid_span": 4,
                "unit": "mg/dL",
            },
            {
                "field_id": "LB_COMM",
                "label": "Laboratory Assessment Notes",
                "data_type": "text",
                "cdash_variable": "LB.LBCOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "QS": {
        "form_name": "Patient Reported Outcomes & VAS Pain Slider",
        "items": [
            {
                "field_id": "QS_VAS_PAIN",
                "label": "Visual Analog Scale (VAS) Pain Score (0 - 100 mm)",
                "data_type": "vas_slider",
                "cdash_variable": "QS.QSSCAT_PAIN",
                "mandatory": True,
                "grid_span": 8,
                "config": {
                    "min_value": 0,
                    "max_value": 100,
                    "step": 1,
                    "min_label": "No Pain (0 mm)",
                    "max_label": "Worst Possible Pain (100 mm)",
                },
            },
            {
                "field_id": "QS_GLOBAL_SCORE",
                "label": "Global Health Assessment",
                "data_type": "integer",
                "cdash_variable": "QS.QSORRES",
                "mandatory": False,
                "range": {"min": 1, "max": 10},
                "grid_span": 4,
            },
            {
                "field_id": "QS_FATIGUE_VAS",
                "label": "VAS Fatigue Score (0 - 100 mm)",
                "data_type": "vas_slider",
                "cdash_variable": "QS.QSFATIGUE",
                "mandatory": False,
                "grid_span": 8,
                "config": {
                    "min_value": 0,
                    "max_value": 100,
                    "step": 1,
                    "min_label": "No Fatigue",
                    "max_label": "Worst Possible Fatigue",
                },
            },
            {
                "field_id": "QS_COMPL",
                "label": "Was Questionnaire Completed by Subject?",
                "data_type": "choice",
                "cdash_variable": "QS.QSCOMPL",
                "options": [
                    "Completed by Subject",
                    "Completed with Assistance",
                    "Refused",
                    "Missed",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "QS_COMM",
                "label": "Questionnaire Comments",
                "data_type": "text",
                "cdash_variable": "QS.QSCOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "PE": {
        "form_name": "Physical Examination & 74-Zone SNOMED CT Body Map",
        "items": [
            {
                "field_id": "PE_BODY_MAP",
                "label": "74-Zone SNOMED CT Interactive Anatomical Body Map",
                "data_type": "body_map_74_zone",
                "cdash_variable": "PE.PELOC",
                "mandatory": True,
                "grid_span": 12,
                "config": {
                    "zones_total": 74,
                    "snomed_ct_version": "2024-09",
                    "multiselect": True,
                },
            },
            {
                "field_id": "PE_OVERALL_FINDING",
                "label": "Overall Clinical Finding",
                "data_type": "choice",
                "cdash_variable": "PE.PEORRES",
                "options": ["Normal", "Abnormal", "Not Examined"],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "PE_ABN_DESC",
                "label": "Description of Abnormal Findings",
                "data_type": "text",
                "cdash_variable": "PE.PEABNDESC",
                "mandatory": False,
                "grid_span": 12,
            },
            {
                "field_id": "PE_COMM",
                "label": "Physical Examination Notes",
                "data_type": "text",
                "cdash_variable": "PE.PECOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "DM": {
        "form_name": "Demographics & Baseline Characteristics",
        "items": [
            {
                "field_id": "DM_AGE",
                "label": "Age at Screening (Years)",
                "data_type": "integer",
                "cdash_variable": "DM.AGE",
                "mandatory": True,
                "range": {"min": 18, "max": 120},
                "grid_span": 4,
                "unit": "Years",
            },
            {
                "field_id": "DM_SEX",
                "label": "Sex at Birth",
                "data_type": "choice",
                "cdash_variable": "DM.SEX",
                "options": ["M", "F", "UNDIFFERENTIATED"],
                "mandatory": True,
                "grid_span": 4,
            },
            {
                "field_id": "DM_RACE",
                "label": "Race",
                "data_type": "choice",
                "cdash_variable": "DM.RACE",
                "options": [
                    "AMERICAN INDIAN OR ALASKA NATIVE",
                    "ASIAN",
                    "BLACK OR AFRICAN AMERICAN",
                    "NATIVE HAWAIIAN OR OTHER PACIFIC ISLANDER",
                    "WHITE",
                    "MULTIPLE",
                    "OTHER",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "DM_ETHNIC",
                "label": "Ethnicity",
                "data_type": "choice",
                "cdash_variable": "DM.ETHNIC",
                "options": [
                    "HISPANIC OR LATINO",
                    "NOT HISPANIC OR LATINO",
                    "NOT REPORTED",
                    "UNKNOWN",
                ],
                "mandatory": False,
                "grid_span": 6,
            },
            {
                "field_id": "DM_COMM",
                "label": "Demographics Comments",
                "data_type": "text",
                "cdash_variable": "DM.DMCOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "AE": {
        "form_name": "Adverse Events Log",
        "items": [
            {
                "field_id": "AE_TERM",
                "label": "Reported Adverse Event Term",
                "data_type": "text",
                "cdash_variable": "AE.AETERM",
                "mandatory": True,
                "grid_span": 12,
            },
            {
                "field_id": "AE_START_DATE",
                "label": "Adverse Event Start Date",
                "data_type": "text",
                "cdash_variable": "AE.AESTDTC",
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "AE_END_DATE",
                "label": "Adverse Event End Date",
                "data_type": "text",
                "cdash_variable": "AE.AEENDTC",
                "mandatory": False,
                "grid_span": 6,
            },
            {
                "field_id": "AE_SEVERITY",
                "label": "CTCAE Severity Grade",
                "data_type": "choice",
                "cdash_variable": "AE.AESEV",
                "options": [
                    "Grade 1 - Mild",
                    "Grade 2 - Moderate",
                    "Grade 3 - Severe",
                    "Grade 4 - Life-Threatening",
                    "Grade 5 - Death",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "AE_SERIOUS",
                "label": "Is this a Serious Adverse Event (SAE)?",
                "data_type": "boolean",
                "cdash_variable": "AE.AESER",
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "AE_RELATION",
                "label": "Relationship to Study Drug",
                "data_type": "choice",
                "cdash_variable": "AE.AEREL",
                "options": [
                    "Not Related",
                    "Unlikely Related",
                    "Possibly Related",
                    "Probably Related",
                    "Definitely Related",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "AE_ACTION",
                "label": "Action Taken with Study Treatment",
                "data_type": "choice",
                "cdash_variable": "AE.AEACN",
                "options": [
                    "None",
                    "Dose Reduced",
                    "Dose Interrupted",
                    "Drug Withdrawn",
                    "Not Applicable",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "AE_OUTCOME",
                "label": "Outcome of Event",
                "data_type": "choice",
                "cdash_variable": "AE.AEOUT",
                "options": [
                    "Recovered/Resolved",
                    "Recovering/Resolving",
                    "Not Recovered/Not Resolved",
                    "Recovered with Sequelae",
                    "Fatal",
                    "Unknown",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "AE_COMM",
                "label": "Adverse Event Comments",
                "data_type": "text",
                "cdash_variable": "AE.AECOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "CM": {
        "form_name": "Concomitant Medications",
        "items": [
            {
                "field_id": "CM_TRT",
                "label": "Reported Medication / Treatment Name",
                "data_type": "text",
                "cdash_variable": "CM.CMTRT",
                "mandatory": True,
                "grid_span": 12,
            },
            {
                "field_id": "CM_INDC",
                "label": "Indication / Reason for Treatment",
                "data_type": "text",
                "cdash_variable": "CM.CMINDC",
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "CM_ROUTE",
                "label": "Route of Administration",
                "data_type": "choice",
                "cdash_variable": "CM.CMROUTE",
                "options": [
                    "Oral",
                    "Intravenous",
                    "Subcutaneous",
                    "Intramuscular",
                    "Topical",
                    "Inhalation",
                    "Other",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "CM_DOSE",
                "label": "Dose Amount",
                "data_type": "decimal",
                "cdash_variable": "CM.CMDOSE",
                "mandatory": False,
                "grid_span": 4,
            },
            {
                "field_id": "CM_DOSEU",
                "label": "Dose Unit",
                "data_type": "text",
                "cdash_variable": "CM.CMDOSU",
                "mandatory": False,
                "grid_span": 4,
            },
            {
                "field_id": "CM_FREQ",
                "label": "Dosing Frequency",
                "data_type": "choice",
                "cdash_variable": "CM.CMDOSFRQ",
                "options": [
                    "QD (Once Daily)",
                    "BID (Twice Daily)",
                    "TID (Three Times Daily)",
                    "QID (Four Times Daily)",
                    "PRN (As Needed)",
                    "Once",
                    "Other",
                ],
                "mandatory": True,
                "grid_span": 4,
            },
            {
                "field_id": "CM_START_DATE",
                "label": "Medication Start Date",
                "data_type": "text",
                "cdash_variable": "CM.CMSTDTC",
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "CM_END_DATE",
                "label": "Medication End Date",
                "data_type": "text",
                "cdash_variable": "CM.CMENDTC",
                "mandatory": False,
                "grid_span": 6,
            },
            {
                "field_id": "CM_ONGOING",
                "label": "Is Medication Ongoing?",
                "data_type": "boolean",
                "cdash_variable": "CM.CMONGO",
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "CM_COMM",
                "label": "Concomitant Medication Comments",
                "data_type": "text",
                "cdash_variable": "CM.CMCOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "MH": {
        "form_name": "Medical History",
        "items": [
            {
                "field_id": "MH_TERM",
                "label": "Medical Condition / History Term",
                "data_type": "text",
                "cdash_variable": "MH.MHTERM",
                "mandatory": True,
                "grid_span": 12,
            },
            {
                "field_id": "MH_BODSYS",
                "label": "Body System / System Organ Class",
                "data_type": "choice",
                "cdash_variable": "MH.MHBODSYS",
                "options": [
                    "Cardiovascular",
                    "Respiratory",
                    "Gastrointestinal",
                    "Neurological",
                    "Musculoskeletal",
                    "Endocrine/Metabolic",
                    "Genitourinary",
                    "Dermatological",
                    "Hematological/Lymphatic",
                    "Oncological",
                    "Psychiatric",
                    "Other",
                ],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "MH_START_DATE",
                "label": "Condition Onset / Diagnosis Date",
                "data_type": "text",
                "cdash_variable": "MH.MHSTDTC",
                "mandatory": False,
                "grid_span": 6,
            },
            {
                "field_id": "MH_END_DATE",
                "label": "Condition Resolution Date",
                "data_type": "text",
                "cdash_variable": "MH.MHENDTC",
                "mandatory": False,
                "grid_span": 6,
            },
            {
                "field_id": "MH_ONGOING",
                "label": "Is Condition Currently Ongoing / Active?",
                "data_type": "boolean",
                "cdash_variable": "MH.MHONGO",
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "MH_COMM",
                "label": "Medical History Comments",
                "data_type": "text",
                "cdash_variable": "MH.MHCOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
    "IE": {
        "form_name": "Inclusion / Exclusion Criteria Evaluation",
        "items": [
            {
                "field_id": "IE_ALL_MET",
                "label": "Did the subject satisfy all Inclusion and Exclusion criteria?",
                "data_type": "choice",
                "cdash_variable": "IE.IEALLMET",
                "options": ["Yes", "No"],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "IE_DEV_FLAG",
                "label": "Was an Eligibility Deviation or Waiver Granted?",
                "data_type": "choice",
                "cdash_variable": "IE.IEDEVFL",
                "options": ["Yes", "No"],
                "mandatory": True,
                "grid_span": 6,
            },
            {
                "field_id": "IE_FAILED_CRIT",
                "label": "If No, specify failed Criterion Identifiers",
                "data_type": "text",
                "cdash_variable": "IE.IECAT",
                "mandatory": False,
                "grid_span": 12,
            },
            {
                "field_id": "IE_COMM",
                "label": "Eligibility Determination Comments",
                "data_type": "text",
                "cdash_variable": "IE.IECOMM",
                "mandatory": False,
                "grid_span": 12,
            },
        ],
    },
}


# =========================================================================
# WIDGET AND LAYOUT RESOLUTION HELPERS
# =========================================================================


def resolve_widget_representation(
    data_type: str,
    field_id: str = "",
    label: str = "",
    options: list[str] | None = None,
    grid_span: int | None = None,
    range_constraint: dict[str, Any] | str | None = None,
    config: dict[str, Any] | None = None,
) -> tuple[str, int, dict[str, Any]]:
    """Resolves concrete UI widget type, responsive grid span, and config.

    Args:
        data_type: Raw data type or concept property type string.
        field_id: Field identifier.
        label: Field label text.
        options: Choice options list.
        grid_span: Optional user/concept requested column span.
        range_constraint: Optional range limits.
        config: Optional pre-existing widget configuration.

    Returns:
        Tuple of (resolved_widget_type, column_grid_span, widget_config_dict).
    """
    dt_normalized = (data_type or "text").lower().strip()
    cfg = copy.deepcopy(config or {})

    # 1. VAS Slider (0 - 100 mm Visual Analog Scale)
    if "vas" in dt_normalized or "slider" in dt_normalized or "vas" in field_id.lower():
        resolved_type = "vas_slider"
        resolved_span = grid_span if grid_span in (8, 12) else 8
        if "min_value" not in cfg:
            cfg["min_value"] = 0
        if "max_value" not in cfg:
            cfg["max_value"] = 100
        if "step" not in cfg:
            cfg["step"] = 1
        if "min_label" not in cfg:
            cfg["min_label"] = "No Pain (0 mm)"
        if "max_label" not in cfg:
            cfg["max_label"] = "Worst Possible Pain (100 mm)"
        return resolved_type, resolved_span, cfg

    # 2. 74-Zone SNOMED CT Anatomical Body Map
    if (
        "body_map" in dt_normalized
        or "snomed" in dt_normalized
        or "body_map" in field_id.lower()
    ):
        resolved_type = "body_map_74_zone"
        resolved_span = 12
        if "zones_total" not in cfg:
            cfg["zones_total"] = 74
        if "snomed_ct_version" not in cfg:
            cfg["snomed_ct_version"] = "2024-09"
        if "multiselect" not in cfg:
            cfg["multiselect"] = True
        return resolved_type, resolved_span, cfg

    # 3. Categorical / Choices / Select
    if dt_normalized in (
        "choice",
        "select",
        "codelist",
        "radio",
        "categorical",
    ) or bool(options):
        resolved_type = "choice"
        resolved_span = grid_span if grid_span in (4, 6, 8, 12) else 6
        return resolved_type, resolved_span, cfg

    # 4. Numeric (Integer / Decimal / Float)
    if dt_normalized in ("integer", "decimal", "numeric", "float", "number"):
        resolved_type = (
            "decimal" if dt_normalized in ("decimal", "float") else "integer"
        )
        resolved_span = grid_span if grid_span in (4, 6, 8, 12) else 4
        return resolved_type, resolved_span, cfg

    # 5. Boolean
    if dt_normalized in ("boolean", "bool"):
        resolved_type = "boolean"
        resolved_span = grid_span if grid_span in (4, 6, 8, 12) else 6
        return resolved_type, resolved_span, cfg

    # 6. Text / Narrative / Comments (Default)
    resolved_type = "text"
    is_wide = (
        "comm" in field_id.lower()
        or "desc" in field_id.lower()
        or "term" in field_id.lower()
        or "narrative" in label.lower()
    )
    resolved_span = grid_span if grid_span in (4, 6, 8, 12) else (12 if is_wide else 6)
    return resolved_type, resolved_span, cfg


# =========================================================================
# DECLARATIVE VALIDATION & EDIT CHECK RULE SYNTHESIS
# =========================================================================


def synthesize_domain_rules(
    domain: str, items: list[dict[str, Any]]
) -> list[dict[str, Any]]:
    """Synthesizes declarative edit checks and validation rules for a given CDASH domain.

    Args:
        domain: CDASH domain code (e.g. 'VS', 'EG', 'LB', 'QS', 'PE', 'DM', 'AE', 'CM', 'MH', 'IE').
        items: List of field item dictionaries in the form.

    Returns:
        List of validation rule dictionaries.
    """
    domain_upper = domain.upper()
    rules: list[dict[str, Any]] = []
    field_ids = {i.get("field_id", "") for i in items}

    # 1. Vital Signs (VS) Rules
    if domain_upper == "VS":
        if "VS_SYSBP" in field_ids and "VS_DIABP" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_VS_BP_SANITY",
                    "type": "cross_field_check",
                    "condition": "VS_SYSBP > VS_DIABP",
                    "query_message": "Systolic Blood Pressure must be greater than Diastolic Blood Pressure.",
                    "target_field": "VS_SYSBP",
                    "severity": "ERROR",
                }
            )
        if "VS_SYSBP" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_VS_SYSBP_RANGE",
                    "type": "range_check",
                    "condition": "VS_SYSBP >= 40 && VS_SYSBP <= 260",
                    "query_message": "Systolic Blood Pressure is outside physiological boundary limits (40-260 mmHg).",
                    "target_field": "VS_SYSBP",
                    "severity": "WARNING",
                }
            )
        if "VS_DIABP" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_VS_DIABP_RANGE",
                    "type": "range_check",
                    "condition": "VS_DIABP >= 30 && VS_DIABP <= 160",
                    "query_message": "Diastolic Blood Pressure is outside physiological boundary limits (30-160 mmHg).",
                    "target_field": "VS_DIABP",
                    "severity": "WARNING",
                }
            )

    # 2. Electrocardiogram (EG) Rules
    elif domain_upper == "EG":
        if "EG_QTC" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_EG_QTC_ALERT",
                    "type": "safety_alert",
                    "condition": "EG_QTC <= 500",
                    "query_message": "Fridericia QTc exceeds 500 ms; requires immediate investigator review.",
                    "target_field": "EG_QTC",
                    "severity": "ERROR",
                }
            )
        if "EG_QT" in field_ids and "EG_QTC" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_EG_QT_SANITY",
                    "type": "cross_field_check",
                    "condition": "EG_QT <= EG_QTC + 60",
                    "query_message": "Uncorrected QT interval exceeds corrected QTc interval by an improbable margin.",
                    "target_field": "EG_QT",
                    "severity": "WARNING",
                }
            )

    # 3. Laboratory (LB) Rules
    elif domain_upper == "LB":
        if "LB_HGB" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_LB_HGB_CRITICAL",
                    "type": "safety_alert",
                    "condition": "LB_HGB >= 6.5",
                    "query_message": "Hemoglobin below critical alert threshold (6.5 g/dL).",
                    "target_field": "LB_HGB",
                    "severity": "ERROR",
                }
            )
        if "LB_CREAT" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_LB_CREAT_ALERT",
                    "type": "range_check",
                    "condition": "LB_CREAT <= 3.0",
                    "query_message": "Serum Creatinine exceeds elevated threshold (3.0 mg/dL).",
                    "target_field": "LB_CREAT",
                    "severity": "WARNING",
                }
            )

    # 4. Patient Reported Outcomes (QS) Rules
    elif domain_upper == "QS":
        if "QS_VAS_PAIN" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_QS_VAS_SANITY",
                    "type": "range_check",
                    "condition": "QS_VAS_PAIN >= 0 && QS_VAS_PAIN <= 100",
                    "query_message": "VAS Pain Score must be between 0 and 100 mm.",
                    "target_field": "QS_VAS_PAIN",
                    "severity": "ERROR",
                }
            )

    # 5. Physical Examination (PE) Rules
    elif domain_upper == "PE":
        if "PE_OVERALL_FINDING" in field_ids and "PE_ABN_DESC" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_PE_ABN_REQUIRED",
                    "type": "consistency_check",
                    "condition": "PE_OVERALL_FINDING != 'Abnormal' || PE_ABN_DESC != null",
                    "query_message": "Detailed description is required when physical examination finding is abnormal.",
                    "target_field": "PE_ABN_DESC",
                    "severity": "ERROR",
                }
            )

    # 6. Demographics (DM) Rules
    elif domain_upper == "DM":
        if "DM_AGE" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_DM_AGE_SANITY",
                    "type": "range_check",
                    "condition": "DM_AGE >= 18 && DM_AGE <= 120",
                    "query_message": "Subject age must be between 18 and 120 years.",
                    "target_field": "DM_AGE",
                    "severity": "ERROR",
                }
            )

    # 7. Adverse Events (AE) Rules
    elif domain_upper == "AE":
        if "AE_START_DATE" in field_ids and "AE_END_DATE" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_AE_DATES_SANITY",
                    "type": "consistency_check",
                    "condition": "AE_END_DATE == null || AE_END_DATE >= AE_START_DATE",
                    "query_message": "Adverse Event end date cannot be earlier than start date.",
                    "target_field": "AE_END_DATE",
                    "severity": "ERROR",
                }
            )

    # 8. Concomitant Medications (CM) Rules
    elif domain_upper == "CM":
        if "CM_START_DATE" in field_ids and "CM_END_DATE" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_CM_DATES_SANITY",
                    "type": "consistency_check",
                    "condition": "CM_END_DATE == null || CM_END_DATE >= CM_START_DATE",
                    "query_message": "Concomitant medication end date cannot precede start date.",
                    "target_field": "CM_END_DATE",
                    "severity": "ERROR",
                }
            )

    # 9. Medical History (MH) Rules
    elif domain_upper == "MH":
        if "MH_START_DATE" in field_ids and "MH_END_DATE" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_MH_DATES_SANITY",
                    "type": "consistency_check",
                    "condition": "MH_END_DATE == null || MH_END_DATE >= MH_START_DATE",
                    "query_message": "Medical History resolution date cannot precede onset date.",
                    "target_field": "MH_END_DATE",
                    "severity": "ERROR",
                }
            )

    # 10. Inclusion/Exclusion (IE) Rules
    elif domain_upper == "IE":
        if "IE_ALL_MET" in field_ids and "IE_FAILED_CRIT" in field_ids:
            rules.append(
                {
                    "rule_id": "CHK_IE_CONSISTENCY",
                    "type": "consistency_check",
                    "condition": "IE_ALL_MET == 'Yes' || IE_FAILED_CRIT != null",
                    "query_message": "Failed criteria identifiers must be documented when eligibility criteria are not all met.",
                    "target_field": "IE_FAILED_CRIT",
                    "severity": "ERROR",
                }
            )

    return rules


# =========================================================================
# MAIN SYNTHESIS ENGINE
# =========================================================================


def _infer_cdash_domain_from_activity_name(name: str, description: str = "") -> str:
    """Infers CDASH domain code from activity title and clinical description."""
    text = f"{name} {description}".lower()

    if any(k in text for k in ("vital", "blood pressure", "pulse", "temperature")):
        return "VS"
    if any(k in text for k in ("ecg", "electrocardiogram", "qtc", "holter")):
        return "EG"
    if any(
        k in text
        for k in (
            "lab",
            "chemistry",
            "hematology",
            "urinalysis",
            "blood count",
            "panel",
        )
    ):
        return "LB"
    if any(
        k in text
        for k in ("vas", "pain", "questionnaire", "prom", "pro ", "scale", "score")
    ):
        return "QS"
    if any(k in text for k in ("physical exam", "body map", "lesion", "examination")):
        return "PE"
    if any(
        k in text
        for k in ("demograph", "consent", "baseline char", "eligibility consent")
    ):
        return "DM"
    if any(k in text for k in ("adverse", "ctcae", "toxic", "safety event")):
        return "AE"
    if any(k in text for k in ("concomitant", "prior med", "medication")):
        return "CM"
    if any(k in text for k in ("medical history", "past medical", "history")):
        return "MH"
    if any(k in text for k in ("inclusion", "exclusion", "criteria", "eligibility")):
        return "IE"
    if any(
        k in text
        for k in ("dosing", "infusion", "administration", "exposure", "drug intake")
    ):
        return "EX"
    if any(
        k in text for k in ("disposition", "termination", "completion", "withdrawal")
    ):
        return "DS"
    if any(k in text for k in ("accountability", "kit return", "drug return")):
        return "DA"
    if any(k in text for k in ("deviation", "protocol violation")):
        return "DV"
    if any(k in text for k in ("substance", "smoking", "alcohol", "tobacco")):
        return "SU"
    if any(
        k in text
        for k in ("biopsy", "surgery", "imaging", "scan", "mri", "ct scan", "pet")
    ):
        return "PR"

    # Derive 2-letter uppercase acronym fallback
    words = re.findall(r"[A-Za-z]+", name)
    if len(words) >= 2:
        return f"{words[0][0]}{words[1][0]}".upper()
    if len(words) == 1 and len(words[0]) >= 2:
        return words[0][:2].upper()
    return "OT"


def _extract_activities_and_concepts(
    study: USDMStudy
    | dict[str, Any]
    | list[dict[str, Any]]
    | USDMProtocolExtractionResponse,
) -> tuple[list[dict[str, Any]], dict[str, list[BiomedicalConceptProperty]]]:
    """Normalizes input structures into raw activities and associated concept properties."""
    activities: list[dict[str, Any]] = []
    domain_concept_props: dict[str, list[BiomedicalConceptProperty]] = {}

    # Case 1: USDMStudy Instance
    if isinstance(study, USDMStudy):
        for bc in study.biomedical_concepts:
            if bc.cdash_domain:
                dom = bc.cdash_domain.upper()
                domain_concept_props.setdefault(dom, []).extend(bc.properties)

        for design in study.study_designs:
            for bc in design.biomedical_concepts:
                if bc.cdash_domain:
                    dom = bc.cdash_domain.upper()
                    domain_concept_props.setdefault(dom, []).extend(bc.properties)

            for act in design.activities:
                act_dict = act.model_dump()
                domain = act.cdash_domain or _infer_cdash_domain_from_activity_name(
                    act.name, act.description or ""
                )
                act_dict["cdash_domain"] = domain
                activities.append(act_dict)

                for bc in act.biomedical_concepts:
                    dom = (bc.cdash_domain or domain).upper()
                    domain_concept_props.setdefault(dom, []).extend(bc.properties)

        for ver in study.study_versions:
            for design in ver.study_designs:
                for act in design.activities:
                    if not any(a.get("id") == act.id for a in activities):
                        act_dict = act.model_dump()
                        domain = (
                            act.cdash_domain
                            or _infer_cdash_domain_from_activity_name(
                                act.name, act.description or ""
                            )
                        )
                        act_dict["cdash_domain"] = domain
                        activities.append(act_dict)

    # Case 2: USDMProtocolExtractionResponse Instance
    elif isinstance(study, USDMProtocolExtractionResponse):
        for act in study.activities:
            activities.append(
                {
                    "activity_name": act.activity_name,
                    "name": act.activity_name,
                    "cdash_domain": act.cdash_domain,
                    "biomedical_concept_code": act.biomedical_concept_code,
                    "assigned_visit_names": act.assigned_visit_names,
                }
            )

    # Case 3: List of Objects or Dictionaries
    elif isinstance(study, list):
        for item in study:
            if isinstance(item, dict):
                act_name = (
                    item.get("name")
                    or item.get("activity_name")
                    or item.get("id")
                    or "Activity"
                )
                domain = (
                    item.get("cdash_domain")
                    or item.get("cdashDomain")
                    or _infer_cdash_domain_from_activity_name(act_name)
                )
                activities.append(
                    {
                        "activity_name": act_name,
                        "name": act_name,
                        "cdash_domain": domain,
                        "definedProcedures": item.get("definedProcedures", []),
                        "items": item.get("items", []),
                    }
                )
            elif hasattr(item, "activity_name"):
                activities.append(
                    {
                        "activity_name": item.activity_name,
                        "name": item.activity_name,
                        "cdash_domain": getattr(item, "cdash_domain", "OT"),
                    }
                )

    # Case 4: Dictionary (Raw USDM JSON or Protocol Extraction Dict)
    elif isinstance(study, dict):
        # Try validating as USDMStudy
        if (
            "studyDesigns" in study
            or "study_designs" in study
            or "usdmVersion" in study
        ):
            try:
                study_model = USDMStudy.model_validate(study)
                return _extract_activities_and_concepts(study_model)
            except Exception as exc:
                logger.debug(
                    "Dict could not be validated directly as USDMStudy: %s", exc
                )

        # Try validating as USDMProtocolExtractionResponse
        if "activities" in study and ("study_title" in study or "protocol_id" in study):
            try:
                extraction_model = USDMProtocolExtractionResponse.model_validate(study)
                return _extract_activities_and_concepts(extraction_model)
            except Exception as exc:
                logger.debug(
                    "Dict could not be validated directly as USDMProtocolExtractionResponse: %s",
                    exc,
                )

        # Direct extraction from dictionary keys
        raw_acts = study.get("activities") or []
        for raw in raw_acts:
            if isinstance(raw, dict):
                act_name = raw.get("name") or raw.get("activity_name") or "Activity"
                domain = (
                    raw.get("cdash_domain")
                    or raw.get("cdashDomain")
                    or _infer_cdash_domain_from_activity_name(act_name)
                )
                activities.append(
                    {
                        "activity_name": act_name,
                        "name": act_name,
                        "cdash_domain": domain,
                        "definedProcedures": raw.get("definedProcedures", []),
                    }
                )

        # Check for biomedical concepts at root or inside designs
        concepts_list = (
            study.get("biomedicalConcepts") or study.get("biomedical_concepts") or []
        )
        for c in concepts_list:
            if isinstance(c, dict):
                dom = (c.get("cdashDomain") or c.get("cdash_domain") or "").upper()
                props = c.get("properties") or []
                for p in props:
                    if isinstance(p, dict):
                        with contextlib.suppress(Exception):
                            domain_concept_props.setdefault(dom, []).append(
                                BiomedicalConceptProperty.model_validate(p)
                            )

    return activities, domain_concept_props


def synthesize_crf_layout_from_usdm(
    study: USDMStudy
    | dict[str, Any]
    | list[dict[str, Any]]
    | USDMProtocolExtractionResponse,
    catalog: dict[str, dict[str, Any]] | None = None,
) -> list[SynthesizedECRFForm]:
    """Synthesizes responsive CDASH-compliant eCRF forms, UI widgets, and validation rules from USDM.

    Args:
        study: CDISC USDM study model, protocol extraction response, or raw protocol payload.
        catalog: Optional CDASH catalog override dictionary.

    Returns:
        List of SynthesizedECRFForm instances representing production-ready clinical data capture layouts.
    """
    effective_catalog = catalog if catalog is not None else STANDARD_CDASH_CATALOG
    activities, domain_concept_props = _extract_activities_and_concepts(study)

    synthesized_forms: list[SynthesizedECRFForm] = []
    seen_domains: set[str] = set()

    for act in activities:
        act_name = act.get("activity_name") or act.get("name") or "Clinical Assessment"
        domain = (
            act.get("cdash_domain")
            or act.get("cdashDomain")
            or _infer_cdash_domain_from_activity_name(act_name)
        ).upper()

        if domain in seen_domains:
            continue
        seen_domains.add(domain)

        # 1. Resolve Form Items
        form_items: list[dict[str, Any]] = []

        # If Biomedical Concept properties exist for this domain, map them with priority
        if domain in domain_concept_props and domain_concept_props[domain]:
            for prop in domain_concept_props[domain]:
                w_type, w_span, w_config = resolve_widget_representation(
                    data_type=prop.data_type,
                    field_id=prop.id,
                    label=prop.label or prop.name,
                    options=prop.options,
                    grid_span=prop.grid_span,
                    range_constraint=prop.range,
                    config=prop.config,
                )
                form_items.append(
                    {
                        "field_id": prop.id or f"{domain}_{prop.name.upper()}",
                        "label": prop.label or prop.name,
                        "data_type": w_type,
                        "cdash_variable": prop.cdash_variable
                        or f"{domain}.{prop.name.upper()}",
                        "mandatory": prop.mandatory,
                        "range": prop.range,
                        "options": prop.options,
                        "config": w_config,
                        "grid_span": w_span,
                        "unit": prop.unit,
                    }
                )
            form_name = f"{act_name} eCRF"

        # Else resolve from standard CDASH catalog
        elif domain in effective_catalog:
            cat_entry = effective_catalog[domain]
            form_name = cat_entry.get("form_name", f"{domain} eCRF")
            for raw_item in cat_entry.get("items", []):
                w_type, w_span, w_config = resolve_widget_representation(
                    data_type=raw_item.get("data_type", "text"),
                    field_id=raw_item.get("field_id", ""),
                    label=raw_item.get("label", ""),
                    options=raw_item.get("options", []),
                    grid_span=raw_item.get("grid_span"),
                    range_constraint=raw_item.get("range"),
                    config=raw_item.get("config", {}),
                )
                item_dict = copy.deepcopy(raw_item)
                item_dict["data_type"] = w_type
                item_dict["grid_span"] = w_span
                if w_config:
                    item_dict["config"] = w_config
                form_items.append(item_dict)

        # Else fallback for novel/unmapped domain
        else:
            form_name = f"{act_name} eCRF"
            form_items = [
                {
                    "field_id": f"{domain}_PERF",
                    "label": f"Was {act_name} performed?",
                    "data_type": "boolean",
                    "cdash_variable": f"{domain}.{domain}STAT",
                    "mandatory": True,
                    "grid_span": 6,
                },
                {
                    "field_id": f"{domain}_COMM",
                    "label": "Clinical Assessment Findings",
                    "data_type": "text",
                    "cdash_variable": f"{domain}.{domain}COMM",
                    "mandatory": False,
                    "grid_span": 12,
                },
            ]

        # 2. Synthesize Declarative Edit Checks and Validation Rules
        rules = synthesize_domain_rules(domain, form_items)

        # 3. Instantiate and Collect Form
        form = SynthesizedECRFForm(
            form_id=f"FORM_{domain}",
            form_name=form_name,
            cdash_domain=domain,
            items=form_items,
            rules=rules,
        )
        synthesized_forms.append(form)

    return synthesized_forms


class CRFSynthesizer:
    """High-level service interface for automated eCRF layout and rule synthesis."""

    def __init__(
        self, catalog_overrides: dict[str, dict[str, Any]] | None = None
    ) -> None:
        """Initializes the synthesis service.

        Args:
            catalog_overrides: Optional custom CDASH domain specifications.
        """
        self.catalog = copy.deepcopy(STANDARD_CDASH_CATALOG)
        if catalog_overrides:
            self.catalog.update(catalog_overrides)

    def synthesize(
        self,
        study: USDMStudy
        | dict[str, Any]
        | list[dict[str, Any]]
        | USDMProtocolExtractionResponse,
    ) -> list[SynthesizedECRFForm]:
        """Executes automated eCRF layout synthesis.

        Args:
            study: Protocol specification data payload.

        Returns:
            List of compiled SynthesizedECRFForm objects.
        """
        return synthesize_crf_layout_from_usdm(study, catalog=self.catalog)
