"""
Dataset-JSON Validator Module.

Provides conformance and referential validation for CDISC Dataset-JSON exports.
"""

from typing import Any, Dict, List, Optional, Union

from apps.execution.biostat.models import DatasetJSON, DatasetJSONItemGroup


class DatasetJSONValidationError(ValueError):
    """Custom exception raised when Dataset-JSON validation fails."""

    def __init__(self, errors: List[str]):
        self.errors = errors
        message = "Dataset-JSON Conformance Validation Failed:\n" + "\n".join(
            f"- {err}" for err in errors
        )
        super().__init__(message)


# Required variables per SDTM domain and ADaM dataset
REQUIRED_VARIABLES = {
    "DM": ["STUDYID", "DOMAIN", "USUBJID", "SUBJID", "SEX", "RACE", "ARM"],
    "AE": ["STUDYID", "DOMAIN", "USUBJID", "AESEQ", "AETERM", "AESER"],
    "VS": ["STUDYID", "DOMAIN", "USUBJID", "VSSEQ", "VSTESTCD", "VSTEST"],
    "LB": ["STUDYID", "DOMAIN", "USUBJID", "LBSEQ", "LBTESTCD", "LBTEST"],
    "CM": ["STUDYID", "DOMAIN", "USUBJID", "CMSEQ", "CMTRT"],
    "ADSL": [
        "STUDYID",
        "USUBJID",
        "SUBJID",
        "SITEID",
        "ARM",
        "ACTARM",
        "SAFFL",
        "ITTFL",
    ],
    "ADAE": ["STUDYID", "USUBJID", "ASTDT", "AEDECOD", "AESEQ"],
    "ADVS": ["STUDYID", "USUBJID", "PARAMCD", "PARAM", "AVAL", "ADY"],
}

# Domains/datasets with sequence numbers
SEQUENCE_FIELDS = {
    "AE": "AESEQ",
    "VS": "VSSEQ",
    "LB": "LBSEQ",
    "CM": "CMSEQ",
    "ADAE": "AESEQ",
    "ADVS": "VSSEQ",
}


def _to_dict_list(item_group: DatasetJSONItemGroup) -> List[Dict[str, Any]]:
    """Converts the internal items and itemData of DatasetJSONItemGroup into a list of dicts."""
    var_names = [item.name for item in item_group.items]
    records = []
    for row in item_group.itemData:
        records.append(dict(zip(var_names, row)))
    return records


def _extract_dataset_records(
    dataset_json: Any, dataset_name: str
) -> Optional[List[Dict[str, Any]]]:
    """Tries to extract records for a dataset by name from a DatasetJSON or parsed dict."""
    item_groups = {}
    if isinstance(dataset_json, DatasetJSON):
        if dataset_json.clinicalData:
            item_groups = dataset_json.clinicalData.itemGroupData
        elif dataset_json.referenceData:
            item_groups = dataset_json.referenceData.itemGroupData
    elif isinstance(dataset_json, dict):
        clinical_data = dataset_json.get("clinicalData") or {}
        item_groups = clinical_data.get("itemGroupData") or {}
        if not item_groups:
            ref_data = dataset_json.get("referenceData") or {}
            item_groups = ref_data.get("itemGroupData") or {}

    for key, val in item_groups.items():
        name = key.split(".")[-1] if "." in key else key
        if name.upper() == dataset_name.upper():
            if hasattr(val, "items") and hasattr(val, "itemData"):
                return _to_dict_list(val)
            elif isinstance(val, dict):
                items = val.get("items") or []
                item_data = val.get("itemData") or []
                var_names = [
                    it.get("name") if isinstance(it, dict) else getattr(it, "name", "")
                    for it in items
                ]
                records = []
                for row in item_data:
                    records.append(dict(zip(var_names, row)))
                return records
    return None


def validate_dataset_json(
    dataset_json: Union[DatasetJSON, Dict[str, Any]],
    external_datasets: Optional[
        Dict[str, Union[DatasetJSON, List[Dict[str, Any]]]]
    ] = None,
) -> None:
    """Performs strict CDISC Dataset-JSON validation.

    Checks conformance rules, required variables, unique keys, and ADaM referential
    consistency against other inputs. Raises DatasetJSONValidationError if any issues exist.
    """
    errors: List[str] = []

    # 1. Parse/normalize dataset_json
    item_groups = {}
    if isinstance(dataset_json, DatasetJSON):
        if dataset_json.clinicalData:
            item_groups = dataset_json.clinicalData.itemGroupData
        elif dataset_json.referenceData:
            item_groups = dataset_json.referenceData.itemGroupData
    elif isinstance(dataset_json, dict):
        clinical_data = dataset_json.get("clinicalData") or {}
        item_groups = clinical_data.get("itemGroupData") or {}
        if not item_groups:
            ref_data = dataset_json.get("referenceData") or {}
            item_groups = ref_data.get("itemGroupData") or {}
    else:
        errors.append("Invalid Dataset-JSON root object.")
        raise DatasetJSONValidationError(errors)

    local_datasets: Dict[str, List[Dict[str, Any]]] = {}

    for key, group in item_groups.items():
        ds_name = key.split(".")[-1] if "." in key else key
        ds_name_upper = ds_name.upper()

        items = []
        item_data = []
        if hasattr(group, "items") and hasattr(group, "itemData"):
            items = group.items
            item_data = group.itemData
        elif isinstance(group, dict):
            items = group.get("items") or []
            item_data = group.get("itemData") or []

        var_names = []
        for it in items:
            if isinstance(it, dict):
                var_names.append(it.get("name"))
            else:
                var_names.append(getattr(it, "name", ""))

        records = []
        for row in item_data:
            records.append(dict(zip(var_names, row)))

        local_datasets[ds_name_upper] = records

        # --- Check 1: Required variables ---
        required_vars = REQUIRED_VARIABLES.get(ds_name_upper)
        if required_vars:
            missing_vars = [v for v in required_vars if v not in var_names]
            if missing_vars:
                errors.append(
                    f"[{ds_name_upper}] Missing required variable(s): {', '.join(missing_vars)}"
                )

        # --- Check 2: STUDYID / USUBJID presence & non-emptiness on all rows ---
        has_studyid = "STUDYID" in var_names
        has_usubjid = "USUBJID" in var_names

        for i, row in enumerate(records, start=1):
            if has_studyid:
                val = row.get("STUDYID")
                if val is None or str(val).strip() == "":
                    errors.append(
                        f"[{ds_name_upper}] Row {i}: STUDYID is empty or missing."
                    )
            if has_usubjid:
                val = row.get("USUBJID")
                if val is None or str(val).strip() == "":
                    errors.append(
                        f"[{ds_name_upper}] Row {i}: USUBJID is empty or missing."
                    )

        # --- Check 3: Unique sequence values per subject ---
        seq_field = SEQUENCE_FIELDS.get(ds_name_upper)
        if seq_field and seq_field in var_names:
            seen_seqs = set()
            for i, row in enumerate(records, start=1):
                usubjid = row.get("USUBJID")
                seq_val = row.get(seq_field)
                if usubjid and seq_val is not None:
                    pair = (usubjid, seq_val)
                    if pair in seen_seqs:
                        errors.append(
                            f"[{ds_name_upper}] Duplicate key found on Row {i}: USUBJID='{usubjid}', {seq_field}='{seq_val}'"
                        )
                    seen_seqs.add(pair)

    # 2. Referential consistency checks
    def get_dataset_records(name: str) -> Optional[List[Dict[str, Any]]]:
        name_upper = name.upper()
        if name_upper in local_datasets:
            return local_datasets[name_upper]
        if external_datasets and name_upper in external_datasets:
            ext_val = external_datasets[name_upper]
            if isinstance(ext_val, list):
                return [
                    dict(r) if isinstance(r, dict) else getattr(r, "__dict__", {})
                    for r in ext_val
                ]
            else:
                return _extract_dataset_records(ext_val, name_upper)
        return None

    adsl_records = get_dataset_records("ADSL")
    dm_records = get_dataset_records("DM")
    ae_records = get_dataset_records("AE")
    vs_records = get_dataset_records("VS")

    # Check 1: ADSL vs DM
    if adsl_records and dm_records:
        dm_subjects = {row.get("USUBJID") for row in dm_records if row.get("USUBJID")}
        for row in adsl_records:
            usubjid = row.get("USUBJID")
            if usubjid and usubjid not in dm_subjects:
                errors.append(
                    f"[ADSL] Referential inconsistency: Subject '{usubjid}' not found in DM."
                )

    # Check 2: ADAE vs ADSL
    adae_records = get_dataset_records("ADAE")
    if adae_records and adsl_records:
        adsl_subjects = {
            row.get("USUBJID") for row in adsl_records if row.get("USUBJID")
        }
        adsl_map = {
            row.get("USUBJID"): row for row in adsl_records if row.get("USUBJID")
        }

        for i, row in enumerate(adae_records, start=1):
            usubjid = row.get("USUBJID")
            if usubjid:
                if usubjid not in adsl_subjects:
                    errors.append(
                        f"[ADAE] Referential inconsistency on Row {i}: Subject '{usubjid}' not found in ADSL."
                    )
                else:
                    adsl_row = adsl_map[usubjid]
                    for field in ["ARM", "ACTARM", "SAFFL", "ITTFL", "SITEID"]:
                        if field in row and field in adsl_row:
                            if row[field] != adsl_row[field]:
                                errors.append(
                                    f"[ADAE] Referential inconsistency on Row {i}: {field} value '{row[field]}' does not match ADSL value '{adsl_row[field]}' for subject '{usubjid}'."
                                )

    # Check 3: ADVS vs ADSL
    advs_records = get_dataset_records("ADVS")
    if advs_records and adsl_records:
        adsl_subjects = {
            row.get("USUBJID") for row in adsl_records if row.get("USUBJID")
        }
        adsl_map = {
            row.get("USUBJID"): row for row in adsl_records if row.get("USUBJID")
        }

        for i, row in enumerate(advs_records, start=1):
            usubjid = row.get("USUBJID")
            if usubjid:
                if usubjid not in adsl_subjects:
                    errors.append(
                        f"[ADVS] Referential inconsistency on Row {i}: Subject '{usubjid}' not found in ADSL."
                    )
                else:
                    adsl_row = adsl_map[usubjid]
                    for field in ["ARM", "ACTARM", "SAFFL", "ITTFL", "SITEID"]:
                        if field in row and field in adsl_row:
                            if row[field] != adsl_row[field]:
                                errors.append(
                                    f"[ADVS] Referential inconsistency on Row {i}: {field} value '{row[field]}' does not match ADSL value '{adsl_row[field]}' for subject '{usubjid}'."
                                )

    # Check 4: ADAE vs AE
    if adae_records and ae_records:
        ae_pairs = {
            (row.get("USUBJID"), row.get("AESEQ"))
            for row in ae_records
            if row.get("USUBJID") and row.get("AESEQ") is not None
        }
        for i, row in enumerate(adae_records, start=1):
            usubjid = row.get("USUBJID")
            aeseq = row.get("AESEQ")
            if usubjid and aeseq is not None:
                if (usubjid, aeseq) not in ae_pairs:
                    errors.append(
                        f"[ADAE] Referential inconsistency on Row {i}: Sequence AESEQ='{aeseq}' for subject '{usubjid}' not found in AE."
                    )

    # Check 5: ADVS vs VS
    if advs_records and vs_records:
        vs_pairs = {
            (row.get("USUBJID"), row.get("VSSEQ"))
            for row in vs_records
            if row.get("USUBJID") and row.get("VSSEQ") is not None
        }
        for i, row in enumerate(advs_records, start=1):
            usubjid = row.get("USUBJID")
            vsseq = row.get("VSSEQ")
            if usubjid and vsseq is not None:
                if (usubjid, vsseq) not in vs_pairs:
                    errors.append(
                        f"[ADVS] Referential inconsistency on Row {i}: Sequence VSSEQ='{vsseq}' for subject '{usubjid}' not found in VS."
                    )

    # 3. Raise errors if any found
    if errors:
        raise DatasetJSONValidationError(errors)
