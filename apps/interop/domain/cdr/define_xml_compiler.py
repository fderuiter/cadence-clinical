"""CDISC Define-XML v2.1 Metadata Compiler & Conformance Engine.

Auto-compiles standard CDISC Define-XML v2.1 metadata specifications encapsulating:
- Dataset and domain structures (ItemGroupDefs for 10 SDTM domains & 4 ADaM datasets)
- Variable definitions with strict data types, lengths, and display formats (ItemDefs)
- Value Level Metadata (VLM) via def:ValueListDef and def:ValueListRef
- WhereClause definitions (def:WhereClauseDef with RangeCheck conditions)
- Detailed Origin annotations (Collected, Derived, Predecessor, Assigned, Protocol)
- Controlled Terminology codelists (CodeList definitions)
- Computational Methods / Algorithmic derivations (MethodDef definitions)
- Architectural standards definitions (def:Standards for SDTM-IG 3.4 & ADaM-IG 1.3)

Requirements: PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import xml.etree.ElementTree as ET
from datetime import UTC, datetime
from typing import Any

import defusedxml.ElementTree as DefusedET
import defusedxml.minidom as minidom

from apps.interop.domain.cdr.dataset_json_compiler import STANDARD_VARIABLE_SPECS
from apps.interop.domain.cdr.models import (
    DefineXMLCompilationRequest,
    DefineXMLValidationResult,
)

# Standard Origins for Variables
VARIABLE_ORIGINS: dict[str, dict[str, tuple[str, str]]] = {
    "DM": {
        "STUDYID": ("Protocol", "Assigned protocol identifier"),
        "DOMAIN": ("Assigned", "Domain abbreviation"),
        "USUBJID": ("Derived", "Concatenation of STUDYID and SUBJID"),
        "SUBJID": ("Collected", "EDC Demographics eCRF"),
        "RFSTDTC": ("Derived", "First date of study medication administration from EX"),
        "BRTHDTC": ("Collected", "EDC Demographics eCRF"),
        "AGE": ("Derived", "Calculated as (RFSTDTC - BRTHDTC) in years"),
        "SEX": ("Collected", "EDC Demographics eCRF"),
        "RACE": ("Collected", "EDC Demographics eCRF"),
        "ARM": ("Assigned", "RTSM / Randomization Arm Assignment"),
    },
    "AE": {
        "AETERM": ("Collected", "EDC Adverse Events eCRF"),
        "AEDECOD": ("Derived", "MedDRA Automated Coder dictionary match"),
        "AEBODSYS": ("Derived", "MedDRA System Organ Class mapping"),
        "AESEV": ("Collected", "EDC Adverse Events eCRF"),
        "AESER": ("Collected", "EDC Adverse Events eCRF"),
        "AEREL": ("Collected", "EDC Adverse Events eCRF"),
        "AESTDTC": ("Collected", "EDC Adverse Events eCRF"),
        "AEENDTC": ("Collected", "EDC Adverse Events eCRF"),
    },
    "VS": {
        "VSTESTCD": ("Assigned", "Vital signs test code"),
        "VSORRES": ("Collected", "EDC / Connected Biometric Sensors"),
        "VSSTRESN": ("Derived", "Standardized numeric conversion of VSORRES"),
        "VSDTC": ("Collected", "EDC Vital Signs eCRF"),
        "VSDY": ("Derived", "Calculated study day relative to DM.RFSTDTC"),
        "VSBLFL": ("Derived", "Last non-missing record on or prior to RFSTDTC"),
    },
    "LB": {
        "LBTESTCD": ("Assigned", "Laboratory test code"),
        "LBORRES": ("Collected", "Central / Local Diagnostic Laboratory Feed"),
        "LBSTRESN": ("Derived", "Standardized numeric result conversion"),
        "LBNRIND": ("Derived", "Comparison against reference normal ranges"),
        "LBDTC": ("Collected", "Central Lab collection timestamp"),
        "LBDY": ("Derived", "Calculated study day relative to DM.RFSTDTC"),
        "LBBLFL": ("Derived", "Last non-missing lab record prior to RFSTDTC"),
    },
    "EX": {
        "EXTRT": ("Collected", "EDC Drug Administration eCRF"),
        "EXDOSE": ("Collected", "EDC Drug Administration eCRF"),
        "EXSTDTC": ("Collected", "EDC Drug Administration eCRF"),
        "EXENDTC": ("Collected", "EDC Drug Administration eCRF"),
    },
    "DS": {
        "DSTERM": ("Collected", "EDC Study Completion / Discontinuation eCRF"),
        "DSDECOD": ("Assigned", "CDISC Controlled Terminology mapping"),
        "DSSTDTC": ("Collected", "EDC Disposition eCRF"),
    },
    "TU": {
        "TULNKID": ("Collected", "IRC Adjudication Dossier Lesion Identifier"),
        "TUORRES": ("Collected", "Independent Radiologist Lesion Classification"),
        "TUMETHOD": ("Collected", "Imaging Modality (CT/MRI)"),
        "TUDTC": ("Collected", "Scan acquisition date"),
    },
    "TR": {
        "TRTESTCD": ("Assigned", "Tumor result test code"),
        "TRORRES": ("Collected", "IRC Caliper Lesion Measurement (mm)"),
        "TRSTRESN": ("Derived", "Numeric longest diameter measurement in mm"),
        "TRDTC": ("Collected", "Scan acquisition date"),
    },
    "RS": {
        "RSTESTCD": ("Assigned", "Overall response assessment test code"),
        "RSORRES": ("Collected", "RECIST 1.1 Independent Adjudication Outcome"),
        "RSDTC": ("Collected", "Assessment timestamp"),
    },
    "ADSL": {
        "TRTSDT": ("Derived", "First non-missing SDTM EX.EXSTDTC where EXDOSE > 0"),
        "TRTEDT": ("Derived", "Last non-missing SDTM EX.EXENDTC where EXDOSE > 0"),
        "SAFFL": (
            "Derived",
            "Set to 'Y' if subject received at least 1 dose in EX, else 'N'",
        ),
        "ITTFL": ("Derived", "Set to 'Y' for all randomized subjects in DM"),
        "FASFL": ("Derived", "Set to 'Y' if SAFFL == 'Y' and ITTFL == 'Y'"),
        "COMPLFL": ("Derived", "Set to 'Y' if DS indicates study completed"),
        "BMIBL": ("Derived", "WEIGHTBL / (HEIGHTBL/100)^2 from baseline VS"),
    },
    "ADAE": {
        "ASTDT": ("Derived", "Analysis start date from SDTM AE.AESTDTC"),
        "AENDT": ("Derived", "Analysis end date from SDTM AE.AEENDTC"),
        "ASTDY": (
            "Derived",
            "ASTDT - ADSL.TRTSDT + 1 (if >= TRTSDT) or ASTDT - TRTSDT",
        ),
        "AENDY": (
            "Derived",
            "AENDT - ADSL.TRTSDT + 1 (if >= TRTSDT) or AENDT - TRTSDT",
        ),
        "ADURN": ("Derived", "Duration in days: AENDT - ASTDT + 1"),
        "TRTEMFL": ("Derived", "Set to 'Y' if ASTDT >= ADSL.TRTSDT, else 'N'"),
        "PREFL": ("Derived", "Set to 'Y' if ASTDT < ADSL.TRTSDT, else 'N'"),
    },
    "ADTTE": {
        "STARTDT": ("Derived", "Origin analysis date (ADSL.TRTSDT or ADSL.RANDT)"),
        "ADT": ("Derived", "Date of event or censoring milestone"),
        "AVAL": ("Derived", "Analysis duration in days: (ADT - STARTDT + 1)"),
        "CNSR": ("Derived", "0 if target event occurred, 1 if censored"),
    },
    "BDS": {
        "BASE": ("Derived", "Baseline analysis value where ABLFL == 'Y'"),
        "CHG": ("Derived", "Change from baseline: AVAL - BASE"),
        "PCHG": ("Derived", "Percent change from baseline: ((AVAL - BASE)/BASE) * 100"),
        "ADY": ("Derived", "Relative day relative to ADSL.TRTSDT"),
    },
}

# Computational Derivation Method Definitions
METHOD_DEFINITIONS: dict[str, dict[str, str]] = {
    "MT.ADSL.BMIBL": {
        "name": "Derivation for BMIBL",
        "type": "Computation",
        "description": "Calculated as WEIGHTBL / (HEIGHTBL/100)^2 using baseline height (cm) and weight (kg).",
        "expression": "weight_kg / ((height_cm / 100) ** 2)",
    },
    "MT.ADSL.TRTSDT": {
        "name": "Derivation for TRTSDT",
        "type": "Computation",
        "description": "Earliest non-missing EXSTDTC in the EX domain with dose > 0.",
        "expression": "min(ex.EXSTDTC for ex in EX if ex.EXDOSE > 0)",
    },
    "MT.ADAE.TRTEMFL": {
        "name": "Derivation for TRTEMFL",
        "type": "Computation",
        "description": "Set to 'Y' if adverse event start date (ASTDT) is on or after treatment start date (TRTSDT), else 'N'.",
        "expression": "'Y' if ASTDT >= TRTSDT else 'N'",
    },
    "MT.ADAE.ASTDY": {
        "name": "Derivation for ASTDY",
        "type": "Computation",
        "description": "Calculated as ASTDT - TRTSDT + 1 when ASTDT >= TRTSDT, or ASTDT - TRTSDT when ASTDT < TRTSDT.",
        "expression": "(ASTDT - TRTSDT).days + (1 if ASTDT >= TRTSDT else 0)",
    },
    "MT.ADTTE.AVAL": {
        "name": "Derivation for ADTTE.AVAL",
        "type": "Computation",
        "description": "Duration from time-to-event origin (STARTDT) to analysis date (ADT): (ADT - STARTDT + 1).",
        "expression": "(ADT - STARTDT).days + 1",
    },
    "MT.BDS.CHG": {
        "name": "Derivation for BDS.CHG",
        "type": "Computation",
        "description": "Change from baseline calculated as AVAL - BASE for post-baseline visits.",
        "expression": "AVAL - BASE",
    },
    "MT.BDS.PCHG": {
        "name": "Derivation for BDS.PCHG",
        "type": "Computation",
        "description": "Percent change from baseline calculated as ((AVAL - BASE) / BASE) * 100.",
        "expression": "((AVAL - BASE) / BASE) * 100.0 if BASE != 0 else None",
    },
}

# Standard Controlled Terminology Codelists
CODELISTS: dict[str, dict[str, Any]] = {
    "CL.SEX": {
        "name": "Sex",
        "datatype": "text",
        "items": [("M", "Male"), ("F", "Female"), ("U", "Unknown")],
    },
    "CL.RACE": {
        "name": "Race",
        "datatype": "text",
        "items": [
            ("AMERICAN INDIAN OR ALASKA NATIVE", "American Indian or Alaska Native"),
            ("ASIAN", "Asian"),
            ("BLACK OR AFRICAN AMERICAN", "Black or African American"),
            (
                "NATIVE HAWAIIAN OR OTHER PACIFIC ISLANDER",
                "Native Hawaiian or Other Pacific Islander",
            ),
            ("WHITE", "White"),
            ("MULTIPLE", "Multiple"),
            ("OTHER", "Other"),
        ],
    },
    "CL.NY": {
        "name": "No Yes Response",
        "datatype": "text",
        "items": [("N", "No"), ("Y", "Yes")],
    },
    "CL.AESEV": {
        "name": "Severity / Intensity Scale",
        "datatype": "text",
        "items": [("MILD", "Mild"), ("MODERATE", "Moderate"), ("SEVERE", "Severe")],
    },
    "CL.AEREL": {
        "name": "Causality Scale",
        "datatype": "text",
        "items": [("NOT RELATED", "Not Related"), ("RELATED", "Related")],
    },
    "CL.AEOUT": {
        "name": "Outcome of Adverse Event",
        "datatype": "text",
        "items": [
            ("RECOVERED/RESOLVED", "Recovered/Resolved"),
            ("RECOVERING/RESOLVING", "Recovering/Resolving"),
            ("NOT RECOVERED/NOT RESOLVED", "Not Recovered/Not Resolved"),
            ("FATAL", "Fatal"),
            ("UNKNOWN", "Unknown"),
        ],
    },
    "CL.RECIST": {
        "name": "RECIST 1.1 Response Criteria",
        "datatype": "text",
        "items": [
            ("CR", "Complete Response"),
            ("PR", "Partial Response"),
            ("SD", "Stable Disease"),
            ("PD", "Progressive Disease"),
            ("NE", "Not Evaluable"),
        ],
    },
    "CL.PARAMCD_ADTTE": {
        "name": "Time to Event Parameter Code",
        "datatype": "text",
        "items": [
            ("TTDEATH", "Time to Death / Overall Survival"),
            ("TTPROG", "Progression Free Survival"),
            ("TTTOX", "Time to First Adverse Event"),
            ("TTDISC", "Time to Treatment Discontinuation"),
        ],
    },
}

# Value Level Metadata (VLM) & WhereClause Definitions
VLM_DEFINITIONS: dict[str, dict[str, Any]] = {
    "VL.LB.LBORRES": {
        "variable": "LBORRES",
        "domain": "LB",
        "entries": [
            (
                "ALT",
                "IT.LB.LBORRES.ALT",
                "float",
                8,
                "8.1",
                "Alanine Aminotransferase (U/L)",
                "Central Lab",
            ),
            (
                "AST",
                "IT.LB.LBORRES.AST",
                "float",
                8,
                "8.1",
                "Aspartate Aminotransferase (U/L)",
                "Central Lab",
            ),
            (
                "BILI",
                "IT.LB.LBORRES.BILI",
                "float",
                8,
                "8.2",
                "Bilirubin (mg/dL)",
                "Central Lab",
            ),
            (
                "GLUC",
                "IT.LB.LBORRES.GLUC",
                "float",
                8,
                "8.1",
                "Glucose (mg/dL)",
                "Central Lab",
            ),
            (
                "CREAT",
                "IT.LB.LBORRES.CREAT",
                "float",
                8,
                "8.2",
                "Creatinine (mg/dL)",
                "Central Lab",
            ),
            (
                "HGB",
                "IT.LB.LBORRES.HGB",
                "float",
                8,
                "8.1",
                "Hemoglobin (g/dL)",
                "Central Lab",
            ),
        ],
    },
    "VL.VS.VSORRES": {
        "variable": "VSORRES",
        "domain": "VS",
        "entries": [
            (
                "SYSBP",
                "IT.VS.VSORRES.SYSBP",
                "integer",
                4,
                "4.",
                "Systolic Blood Pressure (mmHg)",
                "Vital Signs eCRF",
            ),
            (
                "DIABP",
                "IT.VS.VSORRES.DIABP",
                "integer",
                4,
                "4.",
                "Diastolic Blood Pressure (mmHg)",
                "Vital Signs eCRF",
            ),
            (
                "PULSE",
                "IT.VS.VSORRES.PULSE",
                "integer",
                4,
                "4.",
                "Pulse Rate (beats/min)",
                "Vital Signs eCRF",
            ),
            (
                "TEMP",
                "IT.VS.VSORRES.TEMP",
                "float",
                5,
                "5.1",
                "Body Temperature (C)",
                "Vital Signs eCRF",
            ),
            (
                "WEIGHT",
                "IT.VS.VSORRES.WEIGHT",
                "float",
                8,
                "8.1",
                "Weight (kg)",
                "Vital Signs eCRF",
            ),
            (
                "HEIGHT",
                "IT.VS.VSORRES.HEIGHT",
                "float",
                8,
                "8.1",
                "Height (cm)",
                "Vital Signs eCRF",
            ),
        ],
    },
    "VL.BDS.AVAL": {
        "variable": "AVAL",
        "domain": "BDS",
        "entries": [
            (
                "SYSBP",
                "IT.BDS.AVAL.SYSBP",
                "float",
                8,
                "8.1",
                "Analysis Systolic BP (mmHg)",
                "Derived",
            ),
            (
                "DIABP",
                "IT.BDS.AVAL.DIABP",
                "float",
                8,
                "8.1",
                "Analysis Diastolic BP (mmHg)",
                "Derived",
            ),
            (
                "ALT",
                "IT.BDS.AVAL.ALT",
                "float",
                8,
                "8.1",
                "Analysis ALT (U/L)",
                "Derived",
            ),
            (
                "AST",
                "IT.BDS.AVAL.AST",
                "float",
                8,
                "8.1",
                "Analysis AST (U/L)",
                "Derived",
            ),
        ],
    },
}


class DefineXMLCompiler:
    """Auto-compiler generating standard CDISC Define-XML v2.1 metadata documents."""

    def __init__(self, request: DefineXMLCompilationRequest) -> None:
        self.req = request
        self.namespaces = {
            "": "http://www.cdisc.org/ns/odm/v1.3",
            "def": "http://www.cdisc.org/ns/def/v2.1",
            "arm": "http://www.cdisc.org/ns/arm/v1.0",
            "xlink": "http://www.w3.org/1999/xlink",
            "xs": "http://www.w3.org/2001/XMLSchema-instance",
        }

    def compile(self) -> str:
        """Compile complete CDISC Define-XML v2.1 document string."""
        root = ET.Element(
            "ODM",
            {
                "xmlns": self.namespaces[""],
                "xmlns:def": self.namespaces["def"],
                "xmlns:arm": self.namespaces["arm"],
                "xmlns:xlink": self.namespaces["xlink"],
                "xmlns:xs": self.namespaces["xs"],
                "FileType": "Snapshot",
                "FileOID": f"DEFINE.{self.req.study_id}.{datetime.now(UTC).strftime('%Y%m%d%H%M%S')}",
                "CreationDateTime": datetime.now(UTC)
                .isoformat()
                .replace("+00:00", "Z"),
                "ODMVersion": "1.3.2",
            },
        )

        study_elem = ET.SubElement(root, "Study", {"OID": f"STUDY.{self.req.study_id}"})

        # Global Variables
        global_vars = ET.SubElement(study_elem, "GlobalVariables")
        study_name_el = ET.SubElement(global_vars, "StudyName")
        study_name_el.text = self.req.study_name or f"Study {self.req.study_id}"
        study_desc_el = ET.SubElement(global_vars, "StudyDescription")
        study_desc_el.text = f"Cadence Clinical Study {self.req.study_id} Tabulation and Analysis Metadata"
        proto_el = ET.SubElement(global_vars, "ProtocolName")
        proto_el.text = self.req.protocol_name or f"PROTOCOL-{self.req.study_id}"

        # MetaDataVersion
        mdv_elem = ET.SubElement(
            study_elem,
            "MetaDataVersion",
            {
                "OID": self.req.metadata_version_oid,
                "Name": f"{self.req.standard_name} {self.req.standard_version} Metadata",
                "Description": "CDISC SDTM & ADaM Dataset Definitions with VLM and Origins",
                "def:DefineVersion": "2.1.0",
                "def:StandardName": self.req.standard_name,
                "def:StandardVersion": self.req.standard_version,
            },
        )

        # 1. def:Standards
        standards_elem = ET.SubElement(mdv_elem, "def:Standards")
        ET.SubElement(
            standards_elem,
            "def:Standard",
            {
                "OID": "STD.SDTMIG.3.4",
                "Name": "SDTM-IG",
                "Version": "3.4",
                "Type": "IG",
                "Status": "Final",
            },
        )
        ET.SubElement(
            standards_elem,
            "def:Standard",
            {
                "OID": "STD.ADAMIG.1.3",
                "Name": "ADaM-IG",
                "Version": "1.3",
                "Type": "IG",
                "Status": "Final",
            },
        )
        ET.SubElement(
            standards_elem,
            "def:Standard",
            {
                "OID": "STD.CT.2024-09-27",
                "Name": "CDISC CT",
                "Version": "2024-09-27",
                "Type": "CT",
                "Status": "Final",
            },
        )

        all_target_datasets = self.req.domains + self.req.adam_datasets

        # 2. ItemGroupDef elements
        for ds_name in all_target_datasets:
            ds_upper = ds_name.upper()
            spec = STANDARD_VARIABLE_SPECS.get(
                ds_upper, {"label": f"{ds_upper} Dataset", "items": []}
            )
            label = spec["label"]
            items_list = spec["items"]

            is_adam = ds_upper in ("ADSL", "ADAE", "ADTTE", "BDS")
            ig_class = (
                "SUBJECT LEVEL ANALYSIS"
                if ds_upper == "ADSL"
                else (
                    "OCCURRENCE DATA STRUCTURE"
                    if ds_upper == "ADAE"
                    else (
                        "BASIC DATA STRUCTURE"
                        if ds_upper in ("ADTTE", "BDS")
                        else (
                            "SPECIAL PURPOSE"
                            if ds_upper in ("DM", "CO")
                            else "FINDINGS"
                        )
                    )
                )
            )
            structure = (
                "One record per subject"
                if ds_upper in ("DM", "ADSL")
                else "One record per subject per event/parameter"
            )

            ig_elem = ET.SubElement(
                mdv_elem,
                "ItemGroupDef",
                {
                    "OID": f"IG.{ds_upper}",
                    "Name": ds_upper,
                    "Repeating": "No" if ds_upper in ("DM", "ADSL") else "Yes",
                    "IsReferenceData": "No",
                    "Domain": ds_upper if not is_adam else "",
                    "def:Structure": structure,
                    "def:Class": ig_class,
                    "def:ArchiveLocationID": f"LOC.{ds_upper}",
                },
            )

            desc_el = ET.SubElement(ig_elem, "Description")
            trans_el = ET.SubElement(desc_el, "TranslatedText", {"xml:lang": "en"})
            trans_el.text = label

            # ItemRef children
            for order, (name, _, _, _, _, key_seq, mand) in enumerate(
                items_list, start=1
            ):
                attrs: dict[str, str] = {
                    "ItemOID": f"IT.{ds_upper}.{name}",
                    "OrderNumber": str(order),
                    "Mandatory": "Yes" if mand else "No",
                }
                if key_seq is not None:
                    attrs["KeySequence"] = str(key_seq)
                ET.SubElement(ig_elem, "ItemRef", attrs)

            # def:leaf pointing to Dataset-JSON file
            leaf_el = ET.SubElement(
                ig_elem,
                "def:leaf",
                {
                    "ID": f"LF.{ds_upper}",
                    "xlink:href": f"{ds_upper.lower()}.json",
                },
            )
            title_el = ET.SubElement(leaf_el, "def:title")
            title_el.text = f"{ds_upper.lower()}.json"

        # 3. ItemDef elements
        for ds_name in all_target_datasets:
            ds_upper = ds_name.upper()
            spec = STANDARD_VARIABLE_SPECS.get(
                ds_upper, {"label": f"{ds_upper} Dataset", "items": []}
            )
            items_list = spec["items"]
            origins_map = VARIABLE_ORIGINS.get(ds_upper, {})

            for name, lbl, ty, length, fmt, _, _ in items_list:
                item_oid = f"IT.{ds_upper}.{name}"
                item_attrs: dict[str, str] = {
                    "OID": item_oid,
                    "Name": name,
                    "DataType": ty,
                }
                if length:
                    item_attrs["Length"] = str(length)
                if fmt:
                    item_attrs["def:DisplayFormat"] = fmt
                if ty == "float":
                    item_attrs["SignificantDigits"] = "2"

                item_def = ET.SubElement(mdv_elem, "ItemDef", item_attrs)

                # Description
                desc_el = ET.SubElement(item_def, "Description")
                trans_el = ET.SubElement(desc_el, "TranslatedText", {"xml:lang": "en"})
                trans_el.text = lbl

                # Origin Annotation
                if self.req.include_origins:
                    orig_info = origins_map.get(name)
                    if orig_info:
                        orig_type, orig_desc = orig_info
                        orig_attrs = {"Type": orig_type}
                        if orig_type == "Collected":
                            orig_attrs["Source"] = orig_desc
                        orig_el = ET.SubElement(item_def, "def:Origin", orig_attrs)
                        if orig_desc and orig_type != "Collected":
                            orig_desc_el = ET.SubElement(orig_el, "def:Description")
                            t_el = ET.SubElement(
                                orig_desc_el, "TranslatedText", {"xml:lang": "en"}
                            )
                            t_el.text = orig_desc
                    else:
                        ET.SubElement(item_def, "def:Origin", {"Type": "Collected"})

                # CodeList Reference
                if name == "SEX":
                    ET.SubElement(item_def, "CodeListRef", {"CodeListOID": "CL.SEX"})
                elif name == "RACE":
                    ET.SubElement(item_def, "CodeListRef", {"CodeListOID": "CL.RACE"})
                elif name in (
                    "AESER",
                    "AESDTH",
                    "AESHOSP",
                    "DTHFL",
                    "SAFFL",
                    "ITTFL",
                    "FASFL",
                    "COMPLFL",
                    "TRTEMFL",
                    "PREFL",
                    "VSBLFL",
                    "LBBLFL",
                    "ABLFL",
                ):
                    ET.SubElement(item_def, "CodeListRef", {"CodeListOID": "CL.NY"})
                elif name == "AESEV":
                    ET.SubElement(item_def, "CodeListRef", {"CodeListOID": "CL.AESEV"})
                elif name == "AEREL":
                    ET.SubElement(item_def, "CodeListRef", {"CodeListOID": "CL.AEREL"})
                elif name == "AEOUT":
                    ET.SubElement(item_def, "CodeListRef", {"CodeListOID": "CL.AEOUT"})
                elif name == "RSORRES":
                    ET.SubElement(item_def, "CodeListRef", {"CodeListOID": "CL.RECIST"})
                elif name == "PARAMCD" and ds_upper == "ADTTE":
                    ET.SubElement(
                        item_def, "CodeListRef", {"CodeListOID": "CL.PARAMCD_ADTTE"}
                    )

                # Method Reference (Derivation)
                method_oid = f"MT.{ds_upper}.{name}"
                if method_oid in METHOD_DEFINITIONS:
                    ET.SubElement(item_def, "def:MethodRef", {"MethodOID": method_oid})

                # Value Level Metadata Reference
                vl_key = f"VL.{ds_upper}.{name}"
                if self.req.include_vlm and vl_key in VLM_DEFINITIONS:
                    ET.SubElement(
                        item_def, "def:ValueListRef", {"ValueListOID": vl_key}
                    )

        # 4. Value Level Metadata (def:ValueListDef) & Specific VLM ItemDefs
        if self.req.include_vlm:
            for vl_oid, vl_data in VLM_DEFINITIONS.items():
                domain = vl_data["domain"]
                if domain not in all_target_datasets:
                    continue

                vl_def = ET.SubElement(mdv_elem, "def:ValueListDef", {"OID": vl_oid})
                entries = vl_data["entries"]

                for order, (
                    code,
                    vlm_item_oid,
                    ty,
                    length,
                    fmt,
                    vlm_desc,
                    source,
                ) in enumerate(entries, start=1):
                    # ItemRef with WhereClauseRef
                    wc_oid = f"WC.{domain}.{code}"
                    it_ref = ET.SubElement(
                        vl_def,
                        "ItemRef",
                        {
                            "ItemOID": vlm_item_oid,
                            "OrderNumber": str(order),
                            "Mandatory": "No",
                        },
                    )
                    ET.SubElement(
                        it_ref, "def:WhereClauseRef", {"WhereClauseOID": wc_oid}
                    )

                    # Generate specific ItemDef for this VLM entry
                    vlm_item_def = ET.SubElement(
                        mdv_elem,
                        "ItemDef",
                        {
                            "OID": vlm_item_oid,
                            "Name": vl_data["variable"],
                            "DataType": ty,
                            "Length": str(length),
                            "def:DisplayFormat": fmt,
                        },
                    )
                    desc_el = ET.SubElement(vlm_item_def, "Description")
                    t_el = ET.SubElement(desc_el, "TranslatedText", {"xml:lang": "en"})
                    t_el.text = vlm_desc

                    if self.req.include_origins:
                        orig_type = "Derived" if source == "Derived" else "Collected"
                        orig_el = ET.SubElement(
                            vlm_item_def,
                            "def:Origin",
                            {"Type": orig_type, "Source": source},
                        )
                        if orig_type == "Derived":
                            d_el = ET.SubElement(orig_el, "def:Description")
                            tt_el = ET.SubElement(
                                d_el, "TranslatedText", {"xml:lang": "en"}
                            )
                            tt_el.text = f"Calculated value for {code}"

        # 5. WhereClause Definitions (def:WhereClauseDef)
        if self.req.include_where_clauses:
            for vl_oid, vl_data in VLM_DEFINITIONS.items():
                domain = vl_data["domain"]
                if domain not in all_target_datasets:
                    continue
                match_var = (
                    "PARAMCD" if domain in ("BDS", "ADTTE") else f"{domain}TESTCD"
                )
                match_item_oid = f"IT.{domain}.{match_var}"

                for code, _, _, _, _, _, _ in vl_data["entries"]:
                    wc_oid = f"WC.{domain}.{code}"
                    wc_def = ET.SubElement(
                        mdv_elem, "def:WhereClauseDef", {"OID": wc_oid}
                    )
                    rc = ET.SubElement(
                        wc_def,
                        "def:RangeCheck",
                        {
                            "Comparator": "EQ",
                            "SoftHard": "Hard",
                        },
                    )
                    ET.SubElement(rc, "ItemRef", {"ItemOID": match_item_oid})
                    chk_val = ET.SubElement(rc, "def:CheckValue")
                    chk_val.text = code

        # 6. CodeLists (CodeList)
        for cl_oid, cl_data in CODELISTS.items():
            cl_elem = ET.SubElement(
                mdv_elem,
                "CodeList",
                {
                    "OID": cl_oid,
                    "Name": cl_data["name"],
                    "DataType": cl_data["datatype"],
                },
            )
            for coded_val, decode_text in cl_data["items"]:
                cli = ET.SubElement(cl_elem, "CodeListItem", {"CodedValue": coded_val})
                dec = ET.SubElement(cli, "Decode")
                t_el = ET.SubElement(dec, "TranslatedText", {"xml:lang": "en"})
                t_el.text = decode_text

        # 7. Method Definitions (MethodDef)
        for m_oid, m_data in METHOD_DEFINITIONS.items():
            m_elem = ET.SubElement(
                mdv_elem,
                "MethodDef",
                {
                    "OID": m_oid,
                    "Name": m_data["name"],
                    "Type": m_data["type"],
                },
            )
            desc_el = ET.SubElement(m_elem, "Description")
            t_el = ET.SubElement(desc_el, "TranslatedText", {"xml:lang": "en"})
            t_el.text = m_data["description"]

            if "expression" in m_data:
                expr_el = ET.SubElement(
                    m_elem, "def:FormalExpression", {"Context": "Python"}
                )
                expr_el.text = m_data["expression"]

        # Convert to formatted string with XML declaration
        raw_xml = ET.tostring(root, encoding="utf-8")
        parsed = minidom.parseString(raw_xml)
        return parsed.toprettyxml(indent="  ", encoding="utf-8").decode("utf-8")

    @classmethod
    def validate_define_xml(cls, xml_content: str) -> DefineXMLValidationResult:
        """Validate structural syntax and CDISC Define-XML 2.1 conformance."""
        errors: list[str] = []
        warnings: list[str] = []

        if not xml_content or not xml_content.strip():
            return DefineXMLValidationResult(
                is_valid=False,
                errors=["XML content is empty"],
            )

        try:
            root = DefusedET.fromstring(xml_content.encode("utf-8"))
        except Exception as e:
            return DefineXMLValidationResult(
                is_valid=False,
                errors=[f"XML parse error: {str(e)}"],
            )

        # Check Root tag and attributes
        tag = root.tag.split("}")[-1] if "}" in root.tag else root.tag
        if tag != "ODM":
            errors.append(f"Root element must be <ODM>, found <{tag}>")

        odm_version = root.attrib.get("ODMVersion")
        if odm_version != "1.3.2":
            errors.append(f"ODMVersion must be '1.3.2', found '{odm_version}'")

        # Find MetaDataVersion
        mdv_nodes = [el for el in root.iter() if el.tag.endswith("MetaDataVersion")]
        if not mdv_nodes:
            errors.append("Missing <MetaDataVersion> element")
            return DefineXMLValidationResult(is_valid=False, errors=errors)

        mdv = mdv_nodes[0]
        def_version = mdv.attrib.get(
            "{http://www.cdisc.org/ns/def/v2.1}DefineVersion"
        ) or mdv.attrib.get("def:DefineVersion")
        if def_version != "2.1.0":
            errors.append(f"def:DefineVersion must be '2.1.0', found '{def_version}'")

        # Count components
        item_groups = [el for el in mdv.iter() if el.tag.endswith("ItemGroupDef")]
        item_defs = [el for el in mdv.iter() if el.tag.endswith("ItemDef")]
        vlms = [el for el in mdv.iter() if el.tag.endswith("ValueListDef")]
        where_clauses = [el for el in mdv.iter() if el.tag.endswith("WhereClauseDef")]
        codelists = [el for el in mdv.iter() if el.tag.endswith("CodeList")]
        methods = [el for el in mdv.iter() if el.tag.endswith("MethodDef")]

        if not item_groups:
            errors.append("No ItemGroupDef elements found")
        if not item_defs:
            errors.append("No ItemDef elements found")

        return DefineXMLValidationResult(
            is_valid=len(errors) == 0,
            errors=errors,
            warnings=warnings,
            item_group_count=len(item_groups),
            item_def_count=len(item_defs),
            value_level_metadata_count=len(vlms),
            where_clause_count=len(where_clauses),
            codelist_count=len(codelists),
            method_count=len(methods),
        )
