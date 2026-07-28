"""
Dataset-JSON Serializer Module.

Provides functions to serialize SDTM or ADaM clinical datasets into
CDISC Dataset-JSON format using Pydantic v2 models.
"""

from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Union

from apps.execution.biostat.models import (
    ClinicalData,
    DatasetJSON,
    DatasetJSONItemGroup,
    VariableMetadata,
)

# Standard dataset metadata mapping
STANDARD_DATASETS = {
    "DM": {
        "label": "Demographics",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("DOMAIN", "Domain Abbreviation", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("SUBJID", "Subject Identifier", "string"),
            ("RFSTDTC", "Subject Reference Start Date/Time", "string"),
            ("RFENDTC", "Subject Reference End Date/Time", "string"),
            ("BRTHDTC", "Date of Birth", "string"),
            ("AGE", "Age", "integer"),
            ("AGEU", "Age Units", "string"),
            ("SEX", "Sex", "string"),
            ("RACE", "Race", "string"),
            ("ARM", "Description of Planned Arm", "string"),
        ],
    },
    "AE": {
        "label": "Adverse Events",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("DOMAIN", "Domain Abbreviation", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("AESEQ", "Sequence Number", "integer"),
            ("AETERM", "Reported Term for the Adverse Event", "string"),
            ("AELOC", "Anatomical Location", "string"),
            ("AELDTC", "Date/Time of Local Adverse Event Onset", "string"),
            ("AESTDTC", "Start Date/Time of Adverse Event", "string"),
            ("AEENDTC", "End Date/Time of Adverse Event", "string"),
            ("AESEV", "Severity/Intensity", "string"),
            ("AESER", "Serious Adverse Event Flag", "string"),
            ("AEREL", "Causality / Relationship to treatment", "string"),
            ("AEOUT", "Outcome", "string"),
        ],
    },
    "VS": {
        "label": "Vital Signs",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("DOMAIN", "Domain Abbreviation", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("VSSEQ", "Sequence Number", "integer"),
            ("VSTESTCD", "Vital Signs Test Short Code", "string"),
            ("VSTEST", "Vital Signs Test Name", "string"),
            ("VSORRES", "Original Result", "float"),
            ("VSORRESU", "Original Result Unit", "string"),
            ("VSSTRESC", "Standardized Result in Character Format", "string"),
            ("VSSTRESN", "Standardized Result in Numeric Format", "float"),
            ("VSSTRESU", "Standardized Result Unit", "string"),
            ("VSPOS", "Subject Position", "string"),
            ("VSDTC", "Date/Time of Vital Signs Measurement", "string"),
            ("VSBLFL", "Baseline Flag", "string"),
        ],
    },
    "LB": {
        "label": "Laboratory Findings",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("DOMAIN", "Domain Abbreviation", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("LBSEQ", "Sequence Number", "integer"),
            ("LBTESTCD", "Lab Test Short Code", "string"),
            ("LBTEST", "Lab Test Name", "string"),
            ("LBORRES", "Original Result", "string"),
            ("LBORRESU", "Original Result Unit", "string"),
            ("LBSTRESC", "Standardized Result in Character Format", "string"),
            ("LBSTRESN", "Standardized Result in Numeric Format", "float"),
            ("LBSTRESU", "Standardized Result Unit", "string"),
            ("LBNRIND", "Normal Range Reference Indicator", "string"),
            ("LBDTC", "Date/Time of Specimen Collection", "string"),
            ("LBLOINC", "LOINC Code", "string"),
        ],
    },
    "CM": {
        "label": "Concomitant Medications",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("DOMAIN", "Domain Abbreviation", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("CMSEQ", "Sequence Number", "integer"),
            ("CMTRT", "Reported Name of Medication", "string"),
            ("CMDECOD", "Standardized Medication Name", "string"),
            ("CMCLAS", "Medication Class", "string"),
            ("CMDOSE", "Dose per Administration", "float"),
            ("CMDOSEU", "Dose Units", "string"),
            ("CMDOSFRQ", "Dose Frequency", "string"),
            ("CMROUTE", "Route of Administration", "string"),
            ("CMSTDTC", "Start Date/Time of Medication", "string"),
            ("CMENDTC", "End Date/Time of Medication", "string"),
        ],
    },
    "MH": {
        "label": "Medical History",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("DOMAIN", "Domain Abbreviation", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("MHSEQ", "Sequence Number", "integer"),
            ("MHTERM", "Reported Term for the Medical History", "string"),
            ("MHDECOD", "Standardized Medical History Term", "string"),
            ("MHCAT", "Category of Medical History", "string"),
            ("MHBODSYS", "System Organ Class", "string"),
            ("MHSTDTC", "Start Date/Time of Medical History", "string"),
        ],
    },
    "ADSL": {
        "label": "Subject-Level Analysis Dataset",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("SUBJID", "Subject Identifier", "string"),
            ("SITEID", "Site Identifier", "string"),
            ("ARM", "Description of Planned Arm", "string"),
            ("ACTARM", "Actual Treatment Arm", "string"),
            ("TRT01P", "Planned Treatment for Period 01", "string"),
            ("TRT01A", "Actual Treatment for Period 01", "string"),
            ("TRTSDT", "Treatment Start Date", "integer"),
            ("TRTEDT", "Treatment End Date", "integer"),
            ("RANDT", "Randomization Date", "integer"),
            ("DTHDT", "Death Date", "integer"),
            ("EOSDT", "End of Study Date", "integer"),
            ("SAFFL", "Safety Population Flag", "string"),
            ("ITTFL", "Intent-To-Treat Population Flag", "string"),
        ],
    },
    "ADAE": {
        "label": "Adverse Events Analysis Dataset",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("SUBJID", "Subject Identifier", "string"),
            ("SITEID", "Site Identifier", "string"),
            ("ARM", "Description of Planned Arm", "string"),
            ("ACTARM", "Actual Treatment Arm", "string"),
            ("TRT01P", "Planned Treatment for Period 01", "string"),
            ("TRT01A", "Actual Treatment for Period 01", "string"),
            ("TRTSDT", "Treatment Start Date", "integer"),
            ("TRTEDT", "Treatment End Date", "integer"),
            ("RANDT", "Randomization Date", "integer"),
            ("DTHDT", "Death Date", "integer"),
            ("EOSDT", "End of Study Date", "integer"),
            ("SAFFL", "Safety Population Flag", "string"),
            ("ITTFL", "Intent-To-Treat Population Flag", "string"),
            ("AESEQ", "Sequence Number", "integer"),
            ("AETERM", "Reported Term for the Adverse Event", "string"),
            ("AEDECOD", "Standardized Medication Name/Preferred Term", "string"),
            ("AEBODSYS", "System Organ Class", "string"),
            ("AELOC", "Anatomical Location", "string"),
            ("AELDTC", "Date/Time of Local Adverse Event Onset", "string"),
            ("AESTDTC", "Start Date/Time of Adverse Event", "string"),
            ("AEENDTC", "End Date/Time of Adverse Event", "string"),
            ("AESEV", "Severity/Intensity", "string"),
            ("AESER", "Serious Adverse Event Flag", "string"),
            ("AEREL", "Causality / Relationship to treatment", "string"),
            ("AEOUT", "Outcome", "string"),
            ("ASTDT", "Analysis Start Date", "integer"),
            ("AENDT", "Analysis End Date", "integer"),
            ("ASTDY", "Analysis Start Relative Day", "integer"),
            ("AENDY", "Analysis End Relative Day", "integer"),
            ("TRTEMFL", "Treatment Emergent Adverse Event Flag", "string"),
            ("AESEVN", "Severity/Intensity (N)", "integer"),
        ],
    },
    "ADVS": {
        "label": "Vital Signs Analysis Dataset",
        "variables": [
            ("STUDYID", "Study Identifier", "string"),
            ("USUBJID", "Unique Subject Identifier", "string"),
            ("SUBJID", "Subject Identifier", "string"),
            ("SITEID", "Site Identifier", "string"),
            ("ARM", "Description of Planned Arm", "string"),
            ("ACTARM", "Actual Treatment Arm", "string"),
            ("TRT01P", "Planned Treatment for Period 01", "string"),
            ("TRT01A", "Actual Treatment for Period 01", "string"),
            ("TRTSDT", "Treatment Start Date", "integer"),
            ("TRTEDT", "Treatment End Date", "integer"),
            ("RANDT", "Randomization Date", "integer"),
            ("DTHDT", "Death Date", "integer"),
            ("EOSDT", "End of Study Date", "integer"),
            ("SAFFL", "Safety Population Flag", "string"),
            ("ITTFL", "Intent-To-Treat Population Flag", "string"),
            ("VSSEQ", "Sequence Number", "integer"),
            ("VSTESTCD", "Vital Signs Test Short Code", "string"),
            ("VSTEST", "Vital Signs Test Name", "string"),
            ("VSORRES", "Original Result", "float"),
            ("VSORRESU", "Original Result Unit", "string"),
            ("VSSTRESC", "Standardized Result in Character Format", "string"),
            ("VSSTRESN", "Standardized Result in Numeric Format", "float"),
            ("VSSTRESU", "Standardized Result Unit", "string"),
            ("VSPOS", "Subject Position", "string"),
            ("VSDTC", "Date/Time of Vital Signs Measurement", "string"),
            ("VSBLFL", "Baseline Flag", "string"),
            ("PARAMCD", "Parameter Code", "string"),
            ("PARAM", "Parameter Description", "string"),
            ("AVAL", "Analysis Value", "float"),
            ("AVALC", "Analysis Value (C)", "string"),
            ("ADY", "Analysis Relative Day", "integer"),
            ("AVISIT", "Analysis Visit", "string"),
            ("AVISITN", "Analysis Visit Number", "float"),
            ("BASE", "Baseline Value", "float"),
            ("CHG", "Change from Baseline", "float"),
            ("PCHG", "Percentage Change from Baseline", "float"),
            ("ABLFL", "Analysis Baseline Flag", "string"),
        ],
    },
}


def _to_dict(record: Any) -> Dict[str, Any]:
    """Helper to safely convert pydantic models or dict-like objects to a standard dict."""
    if hasattr(record, "model_dump"):
        return record.model_dump()
    elif hasattr(record, "dict"):
        return record.dict()
    elif isinstance(record, dict):
        return dict(record)
    else:
        # Fallback for arbitrary class objects
        return getattr(record, "__dict__", {})


def _infer_type(value: Any) -> str:
    """Infers the Dataset-JSON data type from a Python value."""
    if value is None:
        return "string"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, int):
        return "integer"
    if isinstance(value, float):
        return "float"
    return "string"


def _build_item_group(
    dataset_name: str,
    records: List[Any],
) -> DatasetJSONItemGroup:
    """Constructs a DatasetJSONItemGroup for a specific dataset list."""
    name_upper = dataset_name.strip().upper()

    # 1. Standard variables & label profile
    profile = STANDARD_DATASETS.get(
        name_upper, {"label": f"{name_upper} Dataset", "variables": []}
    )
    label = profile["label"]
    standard_vars = profile["variables"]

    # Pre-map standard variables for fast lookup
    standard_map = {name: (lbl, ty) for name, lbl, ty in standard_vars}

    # 2. Extract actual keys present in rows
    row_dicts = [_to_dict(rec) for rec in records]

    # Collect union of all keys across records
    actual_keys = set()
    for row in row_dicts:
        actual_keys.update(row.keys())

    # Filter out internal auditable fields
    internal_gxp_fields = {
        "created_at",
        "created_by",
        "reason_for_change",
        "version_index",
    }
    actual_keys = actual_keys - internal_gxp_fields

    # 3. Determine ordered variables list
    ordered_variable_names = []

    # Add standard variables if they exist in standard metadata or in row data
    for var_name, _, _ in standard_vars:
        if var_name in actual_keys:
            ordered_variable_names.append(var_name)

    # Add any extra keys that were not standard variables
    extra_keys = sorted(list(actual_keys - set(ordered_variable_names)))
    ordered_variable_names.extend(extra_keys)

    # 4. Construct VariableMetadata objects
    items_meta = []
    for var_name in ordered_variable_names:
        if var_name in standard_map:
            v_lbl, v_ty = standard_map[var_name]
        else:
            v_lbl = var_name
            # Infer type from non-None values
            inferred_types = set()
            for row in row_dicts:
                val = row.get(var_name)
                if val is not None:
                    inferred_types.add(_infer_type(val))
            v_ty = next(iter(inferred_types)) if inferred_types else "string"

        items_meta.append(
            VariableMetadata(
                name=var_name,
                label=v_lbl,
                type=v_ty,
            )
        )

    # 5. Extract itemData (ordered values for each row)
    item_data = []
    for row in row_dicts:
        row_values = []
        for var_name in ordered_variable_names:
            val = row.get(var_name)
            row_values.append(val)
        item_data.append(row_values)

    return DatasetJSONItemGroup(
        records=len(records),
        name=name_upper,
        label=label,
        items=items_meta,
        itemData=item_data,
    )


def serialize_to_dataset_json(
    data: Union[List[Any], Dict[str, List[Any]]],
    study_id: str,
    metadata_version_id: str = "MDV.001",
    file_oid: Optional[str] = None,
    originator: Optional[str] = None,
    source_system: Optional[str] = None,
    source_system_version: Optional[str] = None,
) -> DatasetJSON:
    """Serializes dataset lists or mapped bundles into CDISC Dataset-JSON structure."""
    item_group_data = {}

    if isinstance(data, dict):
        # Bundle of multiple datasets
        for name, records in data.items():
            if not records:
                records = []
            item_group_data[f"IG.{name.upper()}"] = _build_item_group(name, records)
    else:
        # Single dataset list
        if not data:
            raise ValueError("Input data list is empty. Cannot infer dataset name.")

        first_rec = _to_dict(data[0])
        name = first_rec.get("DOMAIN") or "DATASET"

        item_group_data[f"IG.{name.upper()}"] = _build_item_group(name, data)

    clinical_data = ClinicalData(
        studyOID=study_id,
        metaDataVersionOID=metadata_version_id,
        itemGroupData=item_group_data,
    )

    return DatasetJSON(
        creationDateTime=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        datasetJSONVersion="1.0.0",
        fileOID=file_oid,
        originator=originator,
        sourceSystem=source_system,
        sourceSystemVersion=source_system_version,
        clinicalData=clinical_data,
    )
