"""CDISC Dataset-JSON 1.1 / 2.0 Serializer & Conformance Validator.

Provides full serialization of CDISC SDTM and ADaM datasets into official
Dataset-JSON 1.1 / 2.0 format with strict structural and referential validation.

Requirements: PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import json
from datetime import UTC, datetime
from typing import Any

from apps.interop.domain.cdr.models import (
    DatasetJSONClinicalData,
    DatasetJSONDocument,
    DatasetJSONItem,
    DatasetJSONItemGroup,
)

# Standard SDTM & ADaM Variable Metadata Specifications
STANDARD_VARIABLE_SPECS: dict[str, dict[str, Any]] = {
    "DM": {
        "label": "Demographics",
        "keys": ["STUDYID", "USUBJID"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("SUBJID", "Subject Identifier", "string", 20, "$20.", None, True),
            (
                "RFSTDTC",
                "Subject Reference Start Date/Time",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            (
                "RFENDTC",
                "Subject Reference End Date/Time",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            ("BRTHDTC", "Date of Birth", "string", 10, "ISO8601", None, False),
            ("AGE", "Age", "integer", 3, "3.", None, False),
            ("AGEU", "Age Units", "string", 10, "$10.", None, False),
            ("SEX", "Sex", "string", 2, "$2.", None, True),
            ("RACE", "Race", "string", 80, "$80.", None, True),
            ("ETHNIC", "Ethnic Group", "string", 40, "$40.", None, False),
            ("ARMCD", "Planned Arm Code", "string", 20, "$20.", None, False),
            ("ARM", "Description of Planned Arm", "string", 60, "$60.", None, False),
            ("ACTARMCD", "Actual Arm Code", "string", 20, "$20.", None, False),
            ("ACTARM", "Description of Actual Arm", "string", 60, "$60.", None, False),
            ("COUNTRY", "Country", "string", 3, "$3.", None, False),
            ("DMDTC", "Date/Time of Collection", "string", 19, "ISO8601", None, False),
        ],
    },
    "AE": {
        "label": "Adverse Events",
        "keys": ["STUDYID", "USUBJID", "AESEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("AESEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            (
                "AETERM",
                "Reported Term for Adverse Event",
                "string",
                200,
                "$200.",
                None,
                True,
            ),
            (
                "AEDECOD",
                "Dictionary-Derived Term / PT",
                "string",
                200,
                "$200.",
                None,
                False,
            ),
            (
                "AEBODSYS",
                "Body System or Organ Class / SOC",
                "string",
                200,
                "$200.",
                None,
                False,
            ),
            ("AESEV", "Severity / Intensity", "string", 20, "$20.", None, False),
            ("AESER", "Serious Event Flag", "string", 2, "$2.", None, True),
            ("AEREL", "Causality / Relationship", "string", 40, "$40.", None, False),
            ("AEOUT", "Outcome of Adverse Event", "string", 40, "$40.", None, False),
            (
                "AESTDTC",
                "Start Date/Time of Adverse Event",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            (
                "AEENDTC",
                "End Date/Time of Adverse Event",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            ("AESDTH", "Results in Death", "string", 2, "$2.", None, False),
            ("AESHOSP", "Requires Hospitalization", "string", 2, "$2.", None, False),
            ("AESCONG", "Congenital Anomaly", "string", 2, "$2.", None, False),
            ("AESLIFE", "Is Life Threatening", "string", 2, "$2.", None, False),
            ("AESDISAB", "Significant Disability", "string", 2, "$2.", None, False),
            ("AETOXGR", "Standard Toxicity Grade", "string", 5, "$5.", None, False),
        ],
    },
    "CM": {
        "label": "Concomitant Medications",
        "keys": ["STUDYID", "USUBJID", "CMSEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("CMSEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            (
                "CMTRT",
                "Reported Name of Medication",
                "string",
                200,
                "$200.",
                None,
                True,
            ),
            (
                "CMDECOD",
                "Standardized Medication Name",
                "string",
                200,
                "$200.",
                None,
                False,
            ),
            ("CMCLAS", "Medication Class", "string", 100, "$100.", None, False),
            ("CMDOSE", "Dose per Administration", "float", 12, "12.2", None, False),
            ("CMDOSEU", "Dose Units", "string", 20, "$20.", None, False),
            ("CMDOSFRQ", "Dose Frequency", "string", 20, "$20.", None, False),
            ("CMROUTE", "Route of Administration", "string", 40, "$40.", None, False),
            (
                "CMSTDTC",
                "Start Date/Time of Medication",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            (
                "CMENDTC",
                "End Date/Time of Medication",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            (
                "CMENRF",
                "End Relative to Reference Period",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
            ("CMINDC", "Indication", "string", 200, "$200.", None, False),
        ],
    },
    "VS": {
        "label": "Vital Signs",
        "keys": ["STUDYID", "USUBJID", "VSSEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("VSSEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            ("VSTESTCD", "Vital Signs Test Short Code", "string", 8, "$8.", None, True),
            ("VSTEST", "Vital Signs Test Name", "string", 40, "$40.", None, True),
            ("VSCAT", "Category for Vital Signs", "string", 40, "$40.", None, False),
            ("VSORRES", "Original Result", "string", 20, "$20.", None, False),
            ("VSORRESU", "Original Units", "string", 20, "$20.", None, False),
            (
                "VSSTRESC",
                "Standardized Result Character",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
            (
                "VSSTRESN",
                "Standardized Result Numeric",
                "float",
                12,
                "12.2",
                None,
                False,
            ),
            ("VSSTRESU", "Standardized Units", "string", 20, "$20.", None, False),
            ("VSPOS", "Subject Position", "string", 20, "$20.", None, False),
            ("VSDTC", "Date/Time of Measurement", "string", 19, "ISO8601", None, False),
            ("VSDY", "Study Day of Vital Signs", "integer", 8, "8.", None, False),
            ("VISITNUM", "Visit Number", "float", 8, "8.1", None, False),
            ("VISIT", "Visit Name", "string", 40, "$40.", None, False),
            ("VSBLFL", "Baseline Flag", "string", 2, "$2.", None, False),
        ],
    },
    "LB": {
        "label": "Laboratory Findings",
        "keys": ["STUDYID", "USUBJID", "LBSEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("LBSEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            ("LBTESTCD", "Lab Test Short Code", "string", 8, "$8.", None, True),
            ("LBTEST", "Lab Test Name", "string", 40, "$40.", None, True),
            ("LBCAT", "Category for Lab Test", "string", 40, "$40.", None, False),
            ("LBORRES", "Original Result", "string", 20, "$20.", None, False),
            ("LBORRESU", "Original Units", "string", 20, "$20.", None, False),
            (
                "LBSTRESC",
                "Standardized Result Character",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
            (
                "LBSTRESN",
                "Standardized Result Numeric",
                "float",
                12,
                "12.2",
                None,
                False,
            ),
            ("LBSTRESU", "Standardized Units", "string", 20, "$20.", None, False),
            ("LBNRIND", "Reference Range Indicator", "string", 20, "$20.", None, False),
            (
                "LBORNRLO",
                "Ref Range Lower Limit Orig",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
            (
                "LBORNRHI",
                "Ref Range Upper Limit Orig",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
            ("LBSTNRLO", "Ref Range Lower Limit Std", "float", 12, "12.2", None, False),
            ("LBSTNRHI", "Ref Range Upper Limit Std", "float", 12, "12.2", None, False),
            ("LBDTC", "Date/Time of Collection", "string", 19, "ISO8601", None, False),
            (
                "LBDY",
                "Study Day of Specimen Collection",
                "integer",
                8,
                "8.",
                None,
                False,
            ),
            ("VISITNUM", "Visit Number", "float", 8, "8.1", None, False),
            ("VISIT", "Visit Name", "string", 40, "$40.", None, False),
            ("LBBLFL", "Baseline Flag", "string", 2, "$2.", None, False),
            ("LOINC", "LOINC Code", "string", 20, "$20.", None, False),
        ],
    },
    "EX": {
        "label": "Exposure",
        "keys": ["STUDYID", "USUBJID", "EXSEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("EXSEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            ("EXTRT", "Name of Actual Treatment", "string", 200, "$200.", None, True),
            ("EXDOSE", "Dose per Administration", "float", 12, "12.2", None, False),
            ("EXDOSU", "Dose Units", "string", 20, "$20.", None, False),
            ("EXDOSFRM", "Dose Form", "string", 40, "$40.", None, False),
            ("EXDOSFRQ", "Dosing Frequency", "string", 20, "$20.", None, False),
            ("EXROUTE", "Route of Administration", "string", 40, "$40.", None, False),
            (
                "EXSTDTC",
                "Start Date/Time of Treatment",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            (
                "EXENDTC",
                "End Date/Time of Treatment",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            ("VISITNUM", "Visit Number", "float", 8, "8.1", None, False),
            ("VISIT", "Visit Name", "string", 40, "$40.", None, False),
            ("EXLOT", "Lot Number", "string", 40, "$40.", None, False),
        ],
    },
    "DS": {
        "label": "Disposition",
        "keys": ["STUDYID", "USUBJID", "DSSEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("DSSEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            (
                "DSTERM",
                "Reported Term for Disposition",
                "string",
                200,
                "$200.",
                None,
                True,
            ),
            (
                "DSDECOD",
                "Standardized Disposition Term",
                "string",
                200,
                "$200.",
                None,
                True,
            ),
            (
                "DSCAT",
                "Category for Disposition Event",
                "string",
                40,
                "$40.",
                None,
                False,
            ),
            (
                "DSSCAT",
                "Subcategory for Disposition",
                "string",
                40,
                "$40.",
                None,
                False,
            ),
            (
                "DSSTDTC",
                "Start Date/Time of Disposition",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            ("EPOCH", "Epoch", "string", 20, "$20.", None, False),
        ],
    },
    "TU": {
        "label": "Tumor Identification",
        "keys": ["STUDYID", "USUBJID", "TUSEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("TUSEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            (
                "TULNKID",
                "Link ID / Lesion Identifier",
                "string",
                20,
                "$20.",
                None,
                True,
            ),
            ("TUTESTCD", "Tumor Ident Test Short Code", "string", 8, "$8.", None, True),
            (
                "TUTEST",
                "Tumor Identification Test Name",
                "string",
                40,
                "$40.",
                None,
                True,
            ),
            (
                "TUORRES",
                "Classification of Identified Tumor",
                "string",
                40,
                "$40.",
                None,
                True,
            ),
            (
                "TUSTRESC",
                "Standardized Result Character",
                "string",
                40,
                "$40.",
                None,
                True,
            ),
            ("TUMETHOD", "Method of Identification", "string", 40, "$40.", None, False),
            (
                "TULOC",
                "Anatomical Location of Tumor",
                "string",
                40,
                "$40.",
                None,
                False,
            ),
            ("VISITNUM", "Visit Number", "float", 8, "8.1", None, False),
            ("VISIT", "Visit Name", "string", 40, "$40.", None, False),
            ("TUDTC", "Date/Time of Assessment", "string", 19, "ISO8601", None, False),
            ("TUEVAL", "Evaluator Role", "string", 40, "$40.", None, False),
            ("TUEVALID", "Evaluator Identifier", "string", 40, "$40.", None, False),
        ],
    },
    "TR": {
        "label": "Tumor Results / Measurements",
        "keys": ["STUDYID", "USUBJID", "TRSEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("TRSEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            (
                "TRLNKID",
                "Link ID / Lesion Identifier",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
            (
                "TRTESTCD",
                "Tumor Result Test Short Code",
                "string",
                8,
                "$8.",
                None,
                True,
            ),
            ("TRTEST", "Tumor Result Test Name", "string", 40, "$40.", None, True),
            ("TRORRES", "Original Result", "string", 20, "$20.", None, False),
            ("TRORRESU", "Original Units", "string", 20, "$20.", None, False),
            (
                "TRSTRESC",
                "Standardized Result Character",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
            (
                "TRSTRESN",
                "Standardized Result Numeric",
                "float",
                12,
                "12.2",
                None,
                False,
            ),
            ("TRSTRESU", "Standardized Units", "string", 20, "$20.", None, False),
            ("TRMETHOD", "Measurement Method", "string", 40, "$40.", None, False),
            ("VISITNUM", "Visit Number", "float", 8, "8.1", None, False),
            ("VISIT", "Visit Name", "string", 40, "$40.", None, False),
            ("TRDTC", "Date/Time of Measurement", "string", 19, "ISO8601", None, False),
            ("TREVAL", "Evaluator Role", "string", 40, "$40.", None, False),
            ("TREVALID", "Evaluator Identifier", "string", 40, "$40.", None, False),
        ],
    },
    "RS": {
        "label": "Disease Response",
        "keys": ["STUDYID", "USUBJID", "RSSEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("RSSEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            (
                "RSTESTCD",
                "Response Assessment Test Short Code",
                "string",
                8,
                "$8.",
                None,
                True,
            ),
            (
                "RSTEST",
                "Response Assessment Test Name",
                "string",
                40,
                "$40.",
                None,
                True,
            ),
            ("RSORRES", "Response Classification", "string", 20, "$20.", None, True),
            (
                "RSSTRESC",
                "Standardized Result Character",
                "string",
                20,
                "$20.",
                None,
                True,
            ),
            (
                "RSMETHOD",
                "Assessment Criteria / Method",
                "string",
                40,
                "$40.",
                None,
                False,
            ),
            ("VISITNUM", "Visit Number", "float", 8, "8.1", None, False),
            ("VISIT", "Visit Name", "string", 40, "$40.", None, False),
            (
                "RSDTC",
                "Date/Time of Response Assessment",
                "string",
                19,
                "ISO8601",
                None,
                False,
            ),
            ("RSEVAL", "Evaluator Role", "string", 40, "$40.", None, False),
            ("RSEVALID", "Evaluator Identifier", "string", 40, "$40.", None, False),
            ("RSEVALSTAT", "Evaluation Status", "string", 20, "$20.", None, False),
        ],
    },
    "ADSL": {
        "label": "Subject-Level Analysis Dataset",
        "keys": ["STUDYID", "USUBJID"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("SUBJID", "Subject Identifier", "string", 20, "$20.", None, True),
            ("SITEID", "Site Identifier", "string", 20, "$20.", None, True),
            ("AGE", "Age", "integer", 3, "3.", None, False),
            ("AGEU", "Age Units", "string", 10, "$10.", None, False),
            ("SEX", "Sex", "string", 2, "$2.", None, True),
            ("RACE", "Race", "string", 80, "$80.", None, True),
            ("ETHNIC", "Ethnic Group", "string", 40, "$40.", None, False),
            ("ARM", "Description of Planned Arm", "string", 60, "$60.", None, True),
            ("ARMCD", "Planned Arm Code", "string", 20, "$20.", None, True),
            ("ACTARM", "Description of Actual Arm", "string", 60, "$60.", None, True),
            ("ACTARMCD", "Actual Arm Code", "string", 20, "$20.", None, True),
            (
                "TRT01P",
                "Planned Treatment for Period 01",
                "string",
                60,
                "$60.",
                None,
                True,
            ),
            (
                "TRT01A",
                "Actual Treatment for Period 01",
                "string",
                60,
                "$60.",
                None,
                True,
            ),
            ("TRTSDT", "Treatment Start Date", "string", 10, "ISO8601", None, False),
            ("TRTEDT", "Treatment End Date", "string", 10, "ISO8601", None, False),
            ("RANDT", "Randomization Date", "string", 10, "ISO8601", None, False),
            ("EOSDT", "End of Study Date", "string", 10, "ISO8601", None, False),
            ("DTHDT", "Death Date", "string", 10, "ISO8601", None, False),
            ("DTHFL", "Death Flag", "string", 2, "$2.", None, True),
            ("SAFFL", "Safety Population Flag", "string", 2, "$2.", None, True),
            ("ITTFL", "Intent-To-Treat Flag", "string", 2, "$2.", None, True),
            ("FASFL", "Full Analysis Set Flag", "string", 2, "$2.", None, True),
            ("COMPLFL", "Completed Study Flag", "string", 2, "$2.", None, True),
            ("HEIGHTBL", "Baseline Height", "float", 8, "8.1", None, False),
            ("WEIGHTBL", "Baseline Weight", "float", 8, "8.1", None, False),
            ("BMIBL", "Baseline BMI", "float", 8, "8.2", None, False),
        ],
    },
    "ADAE": {
        "label": "Adverse Events Analysis Dataset",
        "keys": ["STUDYID", "USUBJID", "AESEQ"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("SUBJID", "Subject Identifier", "string", 20, "$20.", None, True),
            ("SITEID", "Site Identifier", "string", 20, "$20.", None, True),
            ("ARM", "Description of Planned Arm", "string", 60, "$60.", None, True),
            ("ACTARM", "Description of Actual Arm", "string", 60, "$60.", None, True),
            (
                "TRT01A",
                "Actual Treatment for Period 01",
                "string",
                60,
                "$60.",
                None,
                True,
            ),
            ("SAFFL", "Safety Population Flag", "string", 2, "$2.", None, True),
            ("AESEQ", "Sequence Number", "integer", 8, "8.", 3, True),
            (
                "AETERM",
                "Reported Term for Adverse Event",
                "string",
                200,
                "$200.",
                None,
                True,
            ),
            (
                "AEDECOD",
                "Dictionary-Derived Term / PT",
                "string",
                200,
                "$200.",
                None,
                False,
            ),
            (
                "AEBODSYS",
                "System Organ Class / SOC",
                "string",
                200,
                "$200.",
                None,
                False,
            ),
            ("AESEV", "Severity / Intensity", "string", 20, "$20.", None, False),
            ("AESEVN", "Severity / Intensity (N)", "integer", 2, "2.", None, False),
            ("AESER", "Serious Adverse Event Flag", "string", 2, "$2.", None, True),
            ("AEREL", "Causality / Relationship", "string", 40, "$40.", None, False),
            ("AEOUT", "Outcome of Adverse Event", "string", 40, "$40.", None, False),
            ("AESTDTC", "Start Date/Time (SDTM)", "string", 19, "ISO8601", None, False),
            ("AEENDTC", "End Date/Time (SDTM)", "string", 19, "ISO8601", None, False),
            ("ASTDT", "Analysis Start Date", "string", 10, "ISO8601", None, False),
            ("AENDT", "Analysis End Date", "string", 10, "ISO8601", None, False),
            ("ASTDY", "Analysis Start Relative Day", "integer", 8, "8.", None, False),
            ("AENDY", "Analysis End Relative Day", "integer", 8, "8.", None, False),
            ("ADURN", "Analysis Duration (Days)", "integer", 8, "8.", None, False),
            ("ADURU", "Analysis Duration Units", "string", 10, "$10.", None, False),
            ("TRTEMFL", "Treatment Emergent Flag", "string", 2, "$2.", None, True),
            ("PREFL", "Pre-treatment Flag", "string", 2, "$2.", None, True),
        ],
    },
    "ADTTE": {
        "label": "Time-to-Event Analysis Dataset",
        "keys": ["STUDYID", "USUBJID", "PARAMCD"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("SUBJID", "Subject Identifier", "string", 20, "$20.", None, True),
            ("SITEID", "Site Identifier", "string", 20, "$20.", None, True),
            ("ARM", "Description of Planned Arm", "string", 60, "$60.", None, True),
            ("ACTARM", "Description of Actual Arm", "string", 60, "$60.", None, True),
            ("PARAMCD", "Parameter Code", "string", 8, "$8.", 3, True),
            ("PARAM", "Parameter Description", "string", 60, "$60.", None, True),
            (
                "STARTDT",
                "Time-to-Event Origin Date",
                "string",
                10,
                "ISO8601",
                None,
                True,
            ),
            ("ADT", "Analysis Date", "string", 10, "ISO8601", None, True),
            ("AVAL", "Analysis Value (Days)", "float", 12, "12.2", None, True),
            ("AVALU", "Analysis Value Units", "string", 10, "$10.", None, True),
            (
                "CNSR",
                "Censoring Flag (0=Event, 1=Censor)",
                "integer",
                2,
                "2.",
                None,
                True,
            ),
            (
                "EVNTDESC",
                "Event / Censoring Description",
                "string",
                100,
                "$100.",
                None,
                True,
            ),
            ("SAFFL", "Safety Population Flag", "string", 2, "$2.", None, True),
            ("ITTFL", "Intent-To-Treat Flag", "string", 2, "$2.", None, True),
        ],
    },
    "BDS": {
        "label": "Basic Data Structure",
        "keys": ["STUDYID", "USUBJID", "PARAMCD", "AVISITN"],
        "items": [
            ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
            ("USUBJID", "Unique Subject Identifier", "string", 40, "$40.", 2, True),
            ("SUBJID", "Subject Identifier", "string", 20, "$20.", None, True),
            ("SITEID", "Site Identifier", "string", 20, "$20.", None, True),
            ("ARM", "Description of Planned Arm", "string", 60, "$60.", None, True),
            ("ACTARM", "Description of Actual Arm", "string", 60, "$60.", None, True),
            ("TRTSDT", "Treatment Start Date", "string", 10, "ISO8601", None, False),
            ("PARAMCD", "Parameter Code", "string", 8, "$8.", 3, True),
            ("PARAM", "Parameter Description", "string", 60, "$60.", None, True),
            ("PARAMN", "Parameter Number", "integer", 4, "4.", None, False),
            ("AVAL", "Analysis Value (Numeric)", "float", 12, "12.2", None, False),
            ("AVALC", "Analysis Value (Character)", "string", 40, "$40.", None, False),
            ("BASE", "Baseline Value", "float", 12, "12.2", None, False),
            ("CHG", "Change from Baseline", "float", 12, "12.2", None, False),
            ("PCHG", "Percent Change from Baseline", "float", 12, "12.2", None, False),
            ("ADTC", "Analysis Date/Time", "string", 19, "ISO8601", None, False),
            ("ADY", "Analysis Relative Day", "integer", 8, "8.", None, False),
            ("AVISIT", "Analysis Visit Name", "string", 40, "$40.", None, False),
            ("AVISITN", "Analysis Visit Number", "float", 8, "8.1", 4, False),
            ("ABLFL", "Analysis Baseline Flag", "string", 2, "$2.", None, False),
            (
                "ANRIND",
                "Analysis Reference Range Indicator",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
            (
                "BNRIND",
                "Baseline Reference Range Indicator",
                "string",
                20,
                "$20.",
                None,
                False,
            ),
        ],
    },
}


def _record_to_dict(rec: Any) -> dict[str, Any]:
    """Extract dictionary from Pydantic model or dict."""
    if hasattr(rec, "model_dump"):
        return rec.model_dump()
    if isinstance(rec, dict):
        return dict(rec)
    return getattr(rec, "__dict__", {})


class DatasetJSONCompiler:
    """Serializes datasets into CDISC Dataset-JSON 1.1 / 2.0 and performs strict schema validation."""

    def __init__(
        self,
        study_id: str = "STUDY-001",
        metadata_version_oid: str = "MDV.CDISC.001",
        spec_version: str = "1.1.0",
    ) -> None:
        self.study_id = study_id
        self.metadata_version_oid = metadata_version_oid
        self.spec_version = spec_version

    def build_item_group(
        self, dataset_name: str, records: list[Any]
    ) -> DatasetJSONItemGroup:
        """Construct a validated DatasetJSONItemGroup for a single domain/dataset."""
        name_upper = dataset_name.strip().upper()
        spec = STANDARD_VARIABLE_SPECS.get(
            name_upper,
            {
                "label": f"{name_upper} Dataset",
                "keys": ["STUDYID", "USUBJID"],
                "items": [
                    ("STUDYID", "Study Identifier", "string", 20, "$20.", 1, True),
                    ("DOMAIN", "Domain Abbreviation", "string", 4, "$4.", None, True),
                    (
                        "USUBJID",
                        "Unique Subject Identifier",
                        "string",
                        40,
                        "$40.",
                        2,
                        True,
                    ),
                ],
            },
        )

        label = spec["label"]
        raw_items_def = spec["items"]

        # Build list of DatasetJSONItem definitions
        items: list[DatasetJSONItem] = []
        ordered_var_names: list[str] = []

        for name, lbl, ty, length, fmt, key_seq, mand in raw_items_def:
            ordered_var_names.append(name)
            items.append(
                DatasetJSONItem(
                    OID=f"IT.{name_upper}.{name}",
                    name=name,
                    label=lbl,
                    type=ty,
                    length=length,
                    displayFormat=fmt,
                    keySequence=key_seq,
                    mandatory=mand,
                )
            )

        # Build 2D matrix of row values
        item_data: list[list[Any]] = []
        for rec in records:
            row_dict = _record_to_dict(rec)
            row_vals: list[Any] = []
            for var_name in ordered_var_names:
                val = row_dict.get(var_name)
                row_vals.append(val)
            item_data.append(row_vals)

        return DatasetJSONItemGroup(
            itemGroupOID=f"IG.{name_upper}",
            name=name_upper,
            label=label,
            records=len(records),
            items=items,
            itemData=item_data,
        )

    def serialize_datasets(
        self,
        datasets: dict[str, list[Any]],
        file_oid: str | None = None,
    ) -> DatasetJSONDocument:
        """Serialize a full dictionary of SDTM/ADaM datasets into a CDISC Dataset-JSON 1.1 / 2.0 document."""
        item_group_data: dict[str, DatasetJSONItemGroup] = {}

        for ds_name, rec_list in datasets.items():
            ig = self.build_item_group(ds_name, rec_list or [])
            item_group_data[f"IG.{ds_name.upper()}"] = ig

        now_iso = datetime.now(UTC).isoformat().replace("+00:00", "Z")
        f_oid = (
            file_oid
            or f"FILE.{self.study_id}.DATASET-JSON.{datetime.now(UTC).strftime('%Y%m%d%H%M%S')}"
        )

        clinical_data = DatasetJSONClinicalData(
            studyOID=self.study_id,
            metaDataVersionOID=self.metadata_version_oid,
            metaDataRef="define.xml",
            itemGroupData=item_group_data,
        )

        return DatasetJSONDocument(
            creationDateTime=now_iso,
            datasetJSONVersion=self.spec_version,
            fileOID=f_oid,
            dbLastModifiedDateTime=now_iso,
            originator="Cadence Clinical CDR Engine",
            sourceSystem="Cadence Clinical",
            sourceSystemVersion="2.1.0",
            clinicalData=clinical_data,
        )

    def to_json(self, document: DatasetJSONDocument, indent: int | None = 2) -> str:
        """Export Dataset-JSON document to string."""
        doc_dict = document.model_dump(by_alias=True)
        # Ensure official root casing
        if (
            "datasetJSONCreationDateTime" not in doc_dict
            and "creationDateTime" in doc_dict
        ):
            doc_dict["datasetJSONCreationDateTime"] = doc_dict["creationDateTime"]
        return json.dumps(doc_dict, indent=indent, default=str)

    def validate_dataset_json(self, payload: dict[str, Any] | str) -> dict[str, Any]:
        """Perform strict structural, schema, and referential validation on a Dataset-JSON payload."""
        errors: list[str] = []
        warnings: list[str] = []

        if isinstance(payload, str):
            try:
                data = json.loads(payload)
            except Exception as e:
                return {
                    "is_valid": False,
                    "errors": [f"Invalid JSON syntax: {str(e)}"],
                    "warnings": [],
                    "dataset_count": 0,
                    "record_count": 0,
                }
        else:
            data = payload

        # 1. Check Root Envelope
        if not isinstance(data, dict):
            return {
                "is_valid": False,
                "errors": ["Dataset-JSON root document must be a JSON Object"],
                "warnings": [],
                "dataset_count": 0,
                "record_count": 0,
            }

        # Check version
        v = data.get("datasetJSONVersion") or data.get("datasetJsonVersion")
        if not v or v not in ("1.0.0", "1.1.0", "2.0.0"):
            errors.append(
                f"Invalid datasetJSONVersion '{v}'. Must be '1.0.0', '1.1.0', or '2.0.0'."
            )

        if not data.get("fileOID"):
            errors.append("Missing required root property 'fileOID'.")

        clin_data = data.get("clinicalData") or data.get("referenceData")
        if not clin_data or not isinstance(clin_data, dict):
            errors.append(
                "Missing required container 'clinicalData' or 'referenceData'."
            )
            return {
                "is_valid": False,
                "errors": errors,
                "warnings": warnings,
                "dataset_count": 0,
                "record_count": 0,
            }

        if not clin_data.get("studyOID"):
            errors.append("Missing required property 'studyOID' in clinicalData.")

        item_groups = clin_data.get("itemGroupData", {})
        if not isinstance(item_groups, dict):
            errors.append(
                "'itemGroupData' must be a JSON object mapping ItemGroup OIDs to dataset objects."
            )
            return {
                "is_valid": False,
                "errors": errors,
                "warnings": warnings,
                "dataset_count": 0,
                "record_count": 0,
            }

        total_records = 0
        usubjids_dm: set[str] = set()
        usubjids_other: dict[str, set[str]] = {}

        # 2. Check each ItemGroup
        for ig_oid, ig in item_groups.items():
            if not isinstance(ig, dict):
                errors.append(f"ItemGroup '{ig_oid}' must be a JSON object.")
                continue

            ds_name = ig.get("name")
            if not ds_name:
                errors.append(f"ItemGroup '{ig_oid}' missing required property 'name'.")
                continue

            items = ig.get("items", [])
            item_data = ig.get("itemData", [])
            records_count = ig.get("records", 0)

            if not isinstance(items, list):
                errors.append(f"ItemGroup '{ig_oid}' property 'items' must be a list.")
                continue
            if not isinstance(item_data, list):
                errors.append(
                    f"ItemGroup '{ig_oid}' property 'itemData' must be a 2D matrix."
                )
                continue

            if len(item_data) != records_count:
                warnings.append(
                    f"ItemGroup '{ig_oid}' declared records count ({records_count}) does not match itemData length ({len(item_data)})."
                )

            total_records += len(item_data)
            num_cols = len(items)

            # Build column map
            col_names = [it.get("name") for it in items if isinstance(it, dict)]
            usubjid_idx = col_names.index("USUBJID") if "USUBJID" in col_names else -1

            # Validate each row
            for row_idx, row in enumerate(item_data):
                if not isinstance(row, list):
                    errors.append(
                        f"ItemGroup '{ig_oid}' row {row_idx} must be a list of values."
                    )
                    continue
                if len(row) != num_cols:
                    errors.append(
                        f"ItemGroup '{ig_oid}' row {row_idx} length ({len(row)}) does not match items length ({num_cols})."
                    )

                # Collect USUBJIDs
                if usubjid_idx >= 0 and usubjid_idx < len(row):
                    u_val = row[usubjid_idx]
                    if u_val:
                        if ds_name.upper() == "DM":
                            usubjids_dm.add(str(u_val))
                        else:
                            usubjids_other.setdefault(ds_name.upper(), set()).add(
                                str(u_val)
                            )

        # 3. Referential Integrity Validation
        if "DM" in [
            ig.get("name", "").upper()
            for ig in item_groups.values()
            if isinstance(ig, dict)
        ]:
            for ds_name, subj_set in usubjids_other.items():
                unmapped = subj_set - usubjids_dm
                if unmapped:
                    errors.append(
                        f"Referential integrity failure in dataset '{ds_name}': USUBJID(s) {list(unmapped)[:3]} do not exist in Demographics (DM)."
                    )

        return {
            "is_valid": len(errors) == 0,
            "errors": errors,
            "warnings": warnings,
            "dataset_count": len(item_groups),
            "record_count": total_records,
        }
