"""
CDISC Dataset-JSON v1.0 builder and automated pilot validator engine.

Requirements Traceability: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

from datetime import datetime, timezone
from typing import Any, Dict, List

from jsonschema import validate
from jsonschema.exceptions import ValidationError
from sdtm.dataset_json_models import (
    DatasetJsonItemDef,
    DatasetJsonPayload,
)

# Standard SDTMIG v3.4 metadata profiles for DM, AE, VS, LB, CM, MH.
SDTMIG_V34_METADATA = {
    "DM": {
        "label": "Demographics",
        "variables": [
            {"name": "STUDYID", "label": "Study Identifier", "type": "string"},
            {"name": "DOMAIN", "label": "Domain Abbreviation", "type": "string"},
            {"name": "USUBJID", "label": "Unique Subject Identifier", "type": "string"},
            {"name": "SUBJID", "label": "Subject Identifier", "type": "string"},
            {
                "name": "RFSTDTC",
                "label": "Subject Reference Start Date/Time",
                "type": "string",
            },
            {
                "name": "RFENDTC",
                "label": "Subject Reference End Date/Time",
                "type": "string",
            },
            {"name": "BRTHDTC", "label": "Date of Birth", "type": "string"},
            {"name": "AGE", "label": "Age", "type": "integer"},
            {"name": "AGEU", "label": "Age Units", "type": "string"},
            {"name": "SEX", "label": "Sex", "type": "string"},
            {"name": "RACE", "label": "Race", "type": "string"},
            {
                "name": "ARM",
                "label": "Description of Planned Arm",
                "type": "string",
            },
        ],
    },
    "AE": {
        "label": "Adverse Events",
        "variables": [
            {"name": "STUDYID", "label": "Study Identifier", "type": "string"},
            {"name": "DOMAIN", "label": "Domain Abbreviation", "type": "string"},
            {"name": "USUBJID", "label": "Unique Subject Identifier", "type": "string"},
            {"name": "AESEQ", "label": "Sequence Number", "type": "integer"},
            {
                "name": "AETERM",
                "label": "Reported Term for the Adverse Event",
                "type": "string",
            },
            {"name": "AELOC", "label": "Anatomical Location", "type": "string"},
            {
                "name": "AELDTC",
                "label": "Date/Time of Local Adverse Event Onset",
                "type": "string",
            },
            {
                "name": "AESTDTC",
                "label": "Start Date/Time of Adverse Event",
                "type": "string",
            },
            {
                "name": "AEENDTC",
                "label": "End Date/Time of Adverse Event",
                "type": "string",
            },
            {"name": "AESEV", "label": "Severity/Intensity", "type": "string"},
            {"name": "AESER", "label": "Serious Adverse Event Flag", "type": "string"},
            {
                "name": "AEREL",
                "label": "Causality / Relationship to treatment",
                "type": "string",
            },
            {"name": "AEOUT", "label": "Outcome", "type": "string"},
        ],
    },
    "VS": {
        "label": "Vital Signs",
        "variables": [
            {"name": "STUDYID", "label": "Study Identifier", "type": "string"},
            {"name": "DOMAIN", "label": "Domain Abbreviation", "type": "string"},
            {"name": "USUBJID", "label": "Unique Subject Identifier", "type": "string"},
            {"name": "VSSEQ", "label": "Sequence Number", "type": "integer"},
            {
                "name": "VSTESTCD",
                "label": "Vital Signs Test Short Code",
                "type": "string",
            },
            {"name": "VSTEST", "label": "Vital Signs Test Name", "type": "string"},
            {"name": "VSORRES", "label": "Original Result", "type": "float"},
            {"name": "VSORRESU", "label": "Original Result Unit", "type": "string"},
            {
                "name": "VSSTRESC",
                "label": "Standardized Result in Character Format",
                "type": "string",
            },
            {
                "name": "VSSTRESN",
                "label": "Standardized Result in Numeric Format",
                "type": "float",
            },
            {"name": "VSSTRESU", "label": "Standardized Result Unit", "type": "string"},
            {"name": "VSPOS", "label": "Subject Position", "type": "string"},
            {
                "name": "VSDTC",
                "label": "Date/Time of Vital Signs Measurement",
                "type": "string",
            },
            {"name": "VSBLFL", "label": "Baseline Flag", "type": "string"},
        ],
    },
    "LB": {
        "label": "Laboratory Findings",
        "variables": [
            {"name": "STUDYID", "label": "Study Identifier", "type": "string"},
            {"name": "DOMAIN", "label": "Domain Abbreviation", "type": "string"},
            {"name": "USUBJID", "label": "Unique Subject Identifier", "type": "string"},
            {"name": "LBSEQ", "label": "Sequence Number", "type": "integer"},
            {"name": "LBTESTCD", "label": "Lab Test Short Code", "type": "string"},
            {"name": "LBTEST", "label": "Lab Test Name", "type": "string"},
            {"name": "LBORRES", "label": "Original Result", "type": "string"},
            {"name": "LBORRESU", "label": "Original Result Unit", "type": "string"},
            {
                "name": "LBSTRESC",
                "label": "Standardized Result in Character Format",
                "type": "string",
            },
            {
                "name": "LBSTRESN",
                "label": "Standardized Result in Numeric Format",
                "type": "float",
            },
            {"name": "LBSTRESU", "label": "Standardized Result Unit", "type": "string"},
            {
                "name": "LBNRIND",
                "label": "Normal Range Reference Indicator",
                "type": "string",
            },
            {
                "name": "LBDTC",
                "label": "Date/Time of Specimen Collection",
                "type": "string",
            },
            {"name": "LBLOINC", "label": "LOINC Code", "type": "string"},
        ],
    },
    "CM": {
        "label": "Concomitant Medications",
        "variables": [
            {"name": "STUDYID", "label": "Study Identifier", "type": "string"},
            {"name": "DOMAIN", "label": "Domain Abbreviation", "type": "string"},
            {"name": "USUBJID", "label": "Unique Subject Identifier", "type": "string"},
            {"name": "CMSEQ", "label": "Sequence Number", "type": "integer"},
            {
                "name": "CMTRT",
                "label": "Reported Name of Medication",
                "type": "string",
            },
            {
                "name": "CMDECOD",
                "label": "Standardized Medication Name",
                "type": "string",
            },
            {"name": "CMCLAS", "label": "Medication Class", "type": "string"},
            {"name": "CMDOSE", "label": "Dose per Administration", "type": "float"},
            {"name": "CMDOSEU", "label": "Dose Units", "type": "string"},
            {"name": "CMDOSFRQ", "label": "Dose Frequency", "type": "string"},
            {
                "name": "CMROUTE",
                "label": "Route of Administration",
                "type": "string",
            },
            {
                "name": "CMSTDTC",
                "label": "Start Date/Time of Medication",
                "type": "string",
            },
            {
                "name": "CMENDTC",
                "label": "End Date/Time of Medication",
                "type": "string",
            },
        ],
    },
    "MH": {
        "label": "Medical History",
        "variables": [
            {"name": "STUDYID", "label": "Study Identifier", "type": "string"},
            {"name": "DOMAIN", "label": "Domain Abbreviation", "type": "string"},
            {"name": "USUBJID", "label": "Unique Subject Identifier", "type": "string"},
            {"name": "MHSEQ", "label": "Sequence Number", "type": "integer"},
            {
                "name": "MHTERM",
                "label": "Reported Term for the Medical History",
                "type": "string",
            },
            {
                "name": "MHDECOD",
                "label": "Standardized Medical History Term",
                "type": "string",
            },
            {"name": "MHCAT", "label": "Category of Medical History", "type": "string"},
            {"name": "MHBODSYS", "label": "System Organ Class", "type": "string"},
            {
                "name": "MHSTDTC",
                "label": "Start Date/Time of Medical History",
                "type": "string",
            },
        ],
    },
}

DATASET_JSON_SCHEMA = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "properties": {
        "creationDateTime": {"type": "string"},
        "datasetJSONVersion": {"type": "string", "enum": ["1.0.0"]},
        "fileOID": {"type": "string"},
        "clinicalData": {
            "type": "object",
            "properties": {
                "studyOID": {"type": "string"},
                "metaDataVersionOID": {"type": "string"},
                "itemGroupData": {
                    "type": "object",
                    "additionalProperties": {
                        "type": "object",
                        "properties": {
                            "records": {"type": "integer"},
                            "name": {"type": "string"},
                            "label": {"type": "string"},
                            "items": {
                                "type": "array",
                                "items": {
                                    "type": "object",
                                    "properties": {
                                        "name": {"type": "string"},
                                        "label": {"type": "string"},
                                        "type": {
                                            "type": "string",
                                            "enum": [
                                                "string",
                                                "integer",
                                                "float",
                                                "date",
                                                "datetime",
                                            ],
                                        },
                                        "length": {"type": ["integer", "null"]},
                                    },
                                    "required": ["name", "label", "type"],
                                },
                            },
                            "itemData": {
                                "type": "array",
                                "items": {"type": "array"},
                            },
                        },
                        "required": ["records", "name", "label", "items", "itemData"],
                    },
                },
            },
            "required": ["studyOID", "metaDataVersionOID", "itemGroupData"],
        },
    },
    "required": [
        "creationDateTime",
        "datasetJSONVersion",
        "fileOID",
        "clinicalData",
    ],
}


class DatasetJsonBuilder:
    """CDISC Dataset-JSON builder for a specific study."""

    def __init__(self, study_id: str):
        self.study_id = study_id

    def build_domain_dataset(
        self, domain_code: str, records: List[Dict[str, Any]]
    ) -> Dict[str, Any]:
        """Serialize domain records into CDISC Dataset-JSON v1.0 compliant structure.

        Requirements: PRD-SYS-001
        """
        payload = build_dataset_json(domain_code, records, study_id=self.study_id)
        return payload.model_dump()


def build_dataset_json(
    domain_code: str, rows: List[Dict[str, Any]], study_id: str = "study001"
) -> DatasetJsonPayload:
    """Build a CDISC Dataset-JSON v1.0 compliant payload.

    Requirements: PRD-SYS-001
    """
    domain_upper = domain_code.upper()

    # Get standard variables or fallback to dynamically generated ones
    if domain_upper in SDTMIG_V34_METADATA:
        meta_profile = SDTMIG_V34_METADATA[domain_upper]
        label = meta_profile["label"]
        variables = meta_profile["variables"]
    else:
        label = f"{domain_upper} Domain Dataset"
        # Gather all keys from rows to build variables dynamically
        all_keys = set()
        for r in rows:
            all_keys.update(r.keys())
        sorted_keys = sorted(list(all_keys))
        variables = []
        for key in sorted_keys:
            # Infer basic type
            v_type = "string"
            for r in rows:
                val = r.get(key)
                if val is not None:
                    if isinstance(val, bool):
                        v_type = "string"
                    elif isinstance(val, int):
                        v_type = "integer"
                    elif isinstance(val, float):
                        v_type = "float"
                    break
            variables.append({"name": key, "label": key, "type": v_type})

    # Construct items metadata list using DatasetJsonItemDef
    items_def = []
    for var in variables:
        items_def.append(
            DatasetJsonItemDef(
                name=var["name"],
                label=var["label"],
                type=var["type"],
                length=var.get("length"),
            )
        )

    # Format values into ordered itemData arrays
    item_data = []
    for r in rows:
        row_values = []
        for var in variables:
            row_values.append(r.get(var["name"]))
        item_data.append(row_values)

    # Build clinicalData structures
    now_str = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    clinical_data = {
        "studyOID": study_id,
        "metaDataVersionOID": "MDV.001",
        "itemGroupData": {
            domain_upper: {
                "records": len(rows),
                "name": domain_upper,
                "label": label,
                "items": [item.model_dump() for item in items_def],
                "itemData": item_data,
            }
        },
    }

    payload_dict = {
        "creationDateTime": now_str,
        "datasetJSONVersion": "1.0.0",
        "fileOID": f"www.cdisc.org/dataset-json/v1.0/{study_id}/{domain_upper.lower()}",
        "clinicalData": clinical_data,
    }

    # Validate output against JSON Schema spec using jsonschema.validate()
    validate(payload_dict, DATASET_JSON_SCHEMA)

    # Return strongly typed DatasetJsonPayload
    return DatasetJsonPayload(**payload_dict)


def validate_dataset_json_conformance(payload: dict) -> List[ValidationError]:
    """Verify mandatory SDTM variables exist and data types match expected metadata.

    Requirements: PRD-SYS-001
    """
    errors = []

    clinical_data = payload.get("clinicalData") or {}
    item_group_data = clinical_data.get("itemGroupData") or {}

    for group_key, group in item_group_data.items():
        domain_name = group.get("name") or group_key
        domain_upper = domain_name.upper()

        items = group.get("items") or []
        item_data = group.get("itemData") or []

        # Build map of variable name to its item definition
        items_map = {}
        for it in items:
            items_map[it["name"]] = it

        # 1. Verify mandatory variables exist: STUDYID, DOMAIN, USUBJID for all domains
        mandatory_vars = ["STUDYID", "DOMAIN", "USUBJID"]
        # If not DM, we also expect sequence variable ({DOMAIN}SEQ, e.g. AESEQ, VSSEQ, LBSEQ, etc.)
        if domain_upper != "DM":
            mandatory_vars.append(f"{domain_upper}SEQ")

        for var in mandatory_vars:
            if var not in items_map:
                errors.append(
                    ValidationError(
                        f"Mandatory SDTM variable '{var}' is missing in domain '{domain_upper}'."
                    )
                )
            else:
                # Find index of this variable in items
                col_idx = [i for i, item in enumerate(items) if item["name"] == var][0]
                for row_idx, row in enumerate(item_data):
                    val = row[col_idx] if col_idx < len(row) else None
                    if val is None or (isinstance(val, str) and not val.strip()):
                        errors.append(
                            ValidationError(
                                f"Row {row_idx}: Mandatory SDTM variable '{var}' is empty or missing in domain '{domain_upper}'."
                            )
                        )

        # 2. Verify data types match expected SDTM metadata definitions in items metadata,
        # and verify each value in itemData matches the declared item type.
        for row_idx, row in enumerate(item_data):
            for col_idx, item in enumerate(items):
                val = row[col_idx] if col_idx < len(row) else None
                if val is not None:
                    expected_type = item["type"]
                    # Perform data type check
                    if expected_type == "integer":
                        if isinstance(val, bool) or not isinstance(val, int):
                            errors.append(
                                ValidationError(
                                    f"Row {row_idx}: Variable '{item['name']}' has type '{type(val).__name__}' but expected 'integer'."
                                )
                            )
                    elif expected_type == "float":
                        if isinstance(val, bool) or not isinstance(val, (int, float)):
                            errors.append(
                                ValidationError(
                                    f"Row {row_idx}: Variable '{item['name']}' has type '{type(val).__name__}' but expected 'float'."
                                )
                            )
                    elif expected_type == "string":
                        if not isinstance(val, str):
                            errors.append(
                                ValidationError(
                                    f"Row {row_idx}: Variable '{item['name']}' has type '{type(val).__name__}' but expected 'string'."
                                )
                            )
                    # Support date and datetime as well if they are string formats
                    elif expected_type in ("date", "datetime"):
                        if not isinstance(val, str):
                            errors.append(
                                ValidationError(
                                    f"Row {row_idx}: Variable '{item['name']}' has type '{type(val).__name__}' but expected '{expected_type}' (string format)."
                                )
                            )

    return errors
