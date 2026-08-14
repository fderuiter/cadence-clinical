import enum
from typing import Any

from apps.cdisc.models import DatasetJSON, DatasetJSONItemGroup
from apps.cdisc.terminology import (
    AEOutcome,
    AERelationship,
    AESeriousness,
    AESeverity,
    NullFlavor,
    Race,
    Sex,
    normalize_race,
    normalize_seriousness,
    normalize_severity,
    normalize_sex,
)

# Documented Dataset-JSON Validation Profile and Version
VALIDATION_PROFILE_NAME = "CADENCE-CDISC-DATASET-JSON-PROFILE"
VALIDATION_PROFILE_VERSION = "1.0.0"

# Actionable Error Codes for API Clients
MISSING_REQUIRED_VARIABLES = "MISSING_REQUIRED_VARIABLES"
EMPTY_STUDYID_USUBJID = "EMPTY_STUDYID_USUBJID"
DUPLICATE_SEQUENCE = "DUPLICATE_SEQUENCE"
REFERENTIAL_INCONSISTENCY = "REFERENTIAL_INCONSISTENCY"
CONTROLLED_TERMINOLOGY_VIOLATION = "CONTROLLED_TERMINOLOGY_VIOLATION"
NULL_FLAVOR_INCONSISTENCY = "NULL_FLAVOR_INCONSISTENCY"
SUPPLEMENTAL_QUALIFIER_VIOLATION = "SUPPLEMENTAL_QUALIFIER_VIOLATION"


class DatasetJSONValidationError(ValueError):
    """Custom exception raised when Dataset-JSON validation fails."""

    def __init__(self, errors: list[str]):
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
    "MH": ["STUDYID", "DOMAIN", "USUBJID", "MHSEQ", "MHTERM"],
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
    "MH": "MHSEQ",
    "ADAE": "AESEQ",
    "ADVS": "VSSEQ",
}


def _to_dict_list(item_group: DatasetJSONItemGroup) -> list[dict[str, Any]]:
    """Converts the internal items and itemData of DatasetJSONItemGroup into a list of dicts."""
    var_names = [item.name for item in item_group.items]
    records = []
    for row in item_group.itemData:
        records.append(dict(zip(var_names, row)))
    return records


def _extract_dataset_records(
    dataset_json: Any, dataset_name: str
) -> list[dict[str, Any]] | None:
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
            if isinstance(val, dict):
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
    dataset_json: DatasetJSON | dict[str, Any],
    external_datasets: dict[str, DatasetJSON | list[dict[str, Any]]] | None = None,
) -> None:
    """Performs strict CDISC Dataset-JSON validation."""
    errors: list[str] = []

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

    local_datasets: dict[str, list[dict[str, Any]]] = {}

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
        if not required_vars and ds_name_upper.startswith("SUPP"):
            required_vars = [
                "STUDYID",
                "RDOMAIN",
                "USUBJID",
                "IDVAR",
                "IDVARVAL",
                "QNAM",
                "QLABEL",
                "QVAL",
            ]

        if required_vars:
            missing_vars = [v for v in required_vars if v not in var_names]
            if missing_vars:
                errors.append(
                    f"[{MISSING_REQUIRED_VARIABLES}] [{ds_name_upper}] Missing required variable(s): {', '.join(missing_vars)}"
                )

        # --- Check 2: STUDYID / USUBJID presence & non-emptiness on all rows ---
        has_studyid = "STUDYID" in var_names
        has_usubjid = "USUBJID" in var_names

        for i, row in enumerate(records, start=1):
            if has_studyid:
                val = row.get("STUDYID")
                if val is None or str(val).strip() == "":
                    errors.append(
                        f"[{EMPTY_STUDYID_USUBJID}] [{ds_name_upper}] Row {i}: STUDYID is empty or missing."
                    )
            if has_usubjid:
                val = row.get("USUBJID")
                if val is None or str(val).strip() == "":
                    errors.append(
                        f"[{EMPTY_STUDYID_USUBJID}] [{ds_name_upper}] Row {i}: USUBJID is empty or missing."
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
                            f"[{DUPLICATE_SEQUENCE}] [{ds_name_upper}] Duplicate key found on Row {i}: USUBJID='{usubjid}', {seq_field}='{seq_val}'"
                        )
                    seen_seqs.add(pair)

        # --- Check 4: Controlled Terminology Validation ---
        valid_aerel = {r.value for r in AERelationship}
        valid_aeout = {o.value for o in AEOutcome}

        for i, row in enumerate(records, start=1):
            # Validate SEX
            if "SEX" in row:
                sex_val = row["SEX"]
                if sex_val is not None and str(sex_val).strip() != "":
                    try:
                        normalize_sex(sex_val)
                    except ValueError:
                        errors.append(
                            f"[{CONTROLLED_TERMINOLOGY_VIOLATION}] [{ds_name_upper}] Row {i}: SEX value '{sex_val}' is not a valid CDISC controlled terminology."
                        )
            # Validate RACE
            if "RACE" in row:
                race_val = row["RACE"]
                if race_val is not None and str(race_val).strip() != "":
                    try:
                        normalize_race(race_val)
                    except ValueError:
                        errors.append(
                            f"[{CONTROLLED_TERMINOLOGY_VIOLATION}] [{ds_name_upper}] Row {i}: RACE value '{race_val}' is not a valid CDISC controlled terminology."
                        )
            # Validate AESEV
            if "AESEV" in row:
                aesev_val = row["AESEV"]
                if aesev_val is not None and str(aesev_val).strip() != "":
                    try:
                        normalize_severity(aesev_val)
                    except ValueError:
                        errors.append(
                            f"[{CONTROLLED_TERMINOLOGY_VIOLATION}] [{ds_name_upper}] Row {i}: AESEV value '{aesev_val}' is not a valid CDISC controlled terminology."
                        )
            # Validate AESER
            if "AESER" in row:
                aeser_val = row["AESER"]
                if aeser_val is not None and str(aeser_val).strip() != "":
                    try:
                        normalize_seriousness(aeser_val)
                    except ValueError:
                        errors.append(
                            f"[{CONTROLLED_TERMINOLOGY_VIOLATION}] [{ds_name_upper}] Row {i}: AESER value '{aeser_val}' is not a valid CDISC controlled terminology."
                        )
            # Validate AEREL
            if "AEREL" in row:
                aerel_val = row["AEREL"]
                if aerel_val is not None and str(aerel_val).strip() != "":
                    if str(aerel_val).strip().upper() not in valid_aerel:
                        errors.append(
                            f"[{CONTROLLED_TERMINOLOGY_VIOLATION}] [{ds_name_upper}] Row {i}: AEREL value '{aerel_val}' is not a valid CDISC controlled terminology."
                        )
            # Validate AEOUT
            if "AEOUT" in row:
                aeout_val = row["AEOUT"]
                if aeout_val is not None and str(aeout_val).strip() != "":
                    if str(aeout_val).strip().upper() not in valid_aeout:
                        errors.append(
                            f"[{CONTROLLED_TERMINOLOGY_VIOLATION}] [{ds_name_upper}] Row {i}: AEOUT value '{aeout_val}' is not a valid CDISC controlled terminology."
                        )

        # --- Check 5: Null-flavor and --STAT / --REASND consistency ---
        for var_name in var_names:
            if var_name.endswith("STAT"):
                prefix = var_name[:-4]
                reasnd_var = prefix + "REASND"
                for i, row in enumerate(records, start=1):
                    stat_val = row.get(var_name)
                    if (
                        stat_val is not None
                        and str(stat_val).strip().upper() == "NOT DONE"
                    ):
                        reasnd_val = row.get(reasnd_var)
                        if reasnd_val is None or str(reasnd_val).strip() == "":
                            errors.append(
                                f"[{NULL_FLAVOR_INCONSISTENCY}] [{ds_name_upper}] Row {i}: {var_name} is 'NOT DONE', but {reasnd_var} is empty or missing."
                            )
                        for field, val in row.items():
                            if field.startswith(prefix) and any(
                                field.endswith(suf)
                                for suf in ["ORRES", "STRESN", "STRESC"]
                            ):
                                if field in {var_name, reasnd_var}:
                                    continue
                                if val is not None and str(val).strip() != "":
                                    errors.append(
                                        f"[{NULL_FLAVOR_INCONSISTENCY}] [{ds_name_upper}] Row {i}: {var_name} is 'NOT DONE', but measurement field {field} is populated with '{val}'."
                                    )
                    elif stat_val is None or str(stat_val).strip() == "":
                        reasnd_val = row.get(reasnd_var)
                        if reasnd_val is not None and str(reasnd_val).strip() != "":
                            errors.append(
                                f"[{NULL_FLAVOR_INCONSISTENCY}] [{ds_name_upper}] Row {i}: {var_name} is empty, but {reasnd_var} is populated with '{reasnd_val}'."
                            )

    # 2. Referential consistency checks
    def get_dataset_records(name: str) -> list[dict[str, Any]] | None:
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
            return _extract_dataset_records(ext_val, name_upper)
        return None

    adsl_records = get_dataset_records("ADSL")
    dm_records = get_dataset_records("DM")
    ae_records = get_dataset_records("AE")
    vs_records = get_dataset_records("VS")

    if adsl_records and dm_records:
        dm_subjects = {row.get("USUBJID") for row in dm_records if row.get("USUBJID")}
        for row in adsl_records:
            usubjid = row.get("USUBJID")
            if usubjid and usubjid not in dm_subjects:
                errors.append(
                    f"[{REFERENTIAL_INCONSISTENCY}] [ADSL] Referential inconsistency: Subject '{usubjid}' not found in DM."
                )

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
                        f"[{REFERENTIAL_INCONSISTENCY}] [ADAE] Referential inconsistency on Row {i}: Subject '{usubjid}' not found in ADSL."
                    )
                else:
                    adsl_row = adsl_map[usubjid]
                    for field in ["ARM", "ACTARM", "SAFFL", "ITTFL", "SITEID"]:
                        if field in row and field in adsl_row:
                            if row[field] != adsl_row[field]:
                                errors.append(
                                    f"[{REFERENTIAL_INCONSISTENCY}] [ADAE] Referential inconsistency on Row {i}: {field} value '{row[field]}' does not match ADSL value '{adsl_row[field]}' for subject '{usubjid}'."
                                )

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
                        f"[{REFERENTIAL_INCONSISTENCY}] [ADVS] Referential inconsistency on Row {i}: Subject '{usubjid}' not found in ADSL."
                    )
                else:
                    adsl_row = adsl_map[usubjid]
                    for field in ["ARM", "ACTARM", "SAFFL", "ITTFL", "SITEID"]:
                        if field in row and field in adsl_row:
                            if row[field] != adsl_row[field]:
                                errors.append(
                                    f"[{REFERENTIAL_INCONSISTENCY}] [ADVS] Referential inconsistency on Row {i}: {field} value '{row[field]}' does not match ADSL value '{adsl_row[field]}' for subject '{usubjid}'."
                                )

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
                        f"[{REFERENTIAL_INCONSISTENCY}] [ADAE] Referential inconsistency on Row {i}: Sequence AESEQ='{aeseq}' for subject '{usubjid}' not found in AE."
                    )

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
                        f"[{REFERENTIAL_INCONSISTENCY}] [ADVS] Referential inconsistency on Row {i}: Sequence VSSEQ='{vsseq}' for subject '{usubjid}' not found in VS."
                    )

    for key, group in item_groups.items():
        ds_name = key.split(".")[-1] if "." in key else key
        ds_name_upper = ds_name.upper()

        if ds_name_upper.startswith("SUPP"):
            parent_domain_name = ds_name_upper[4:]
            parent_records = get_dataset_records(parent_domain_name)

            for i, row in enumerate(local_datasets[ds_name_upper], start=1):
                rdomain = row.get("RDOMAIN")
                if rdomain != parent_domain_name:
                    errors.append(
                        f"[{SUPPLEMENTAL_QUALIFIER_VIOLATION}] [{ds_name_upper}] Row {i}: RDOMAIN '{rdomain}' must match parent domain '{parent_domain_name}'."
                    )

                usubjid = row.get("USUBJID")
                idvar = row.get("IDVAR")
                idvarval = row.get("IDVARVAL")

                idvar_empty = idvar is None or str(idvar).strip() == ""
                idvarval_empty = idvarval is None or str(idvarval).strip() == ""
                if idvar_empty != idvarval_empty:
                    errors.append(
                        f"[{SUPPLEMENTAL_QUALIFIER_VIOLATION}] [{ds_name_upper}] Row {i}: IDVAR and IDVARVAL must both be either populated or empty. IDVAR='{idvar}', IDVARVAL='{idvarval}'."
                    )

                if parent_records is not None:
                    matching_parents = [
                        r for r in parent_records if r.get("USUBJID") == usubjid
                    ]
                    if not matching_parents:
                        errors.append(
                            f"[{SUPPLEMENTAL_QUALIFIER_VIOLATION}] [{ds_name_upper}] Row {i}: Parent record not found for USUBJID='{usubjid}' in {parent_domain_name}."
                        )
                    elif not idvar_empty:
                        found_var = False
                        found_val = False
                        for p_rec in matching_parents:
                            if idvar in p_rec:
                                found_var = True
                                if str(p_rec[idvar]).strip() == str(idvarval).strip():
                                    found_val = True
                                    break
                        if not found_var:
                            errors.append(
                                f"[{SUPPLEMENTAL_QUALIFIER_VIOLATION}] [{ds_name_upper}] Row {i}: Identifying variable IDVAR='{idvar}' not found in parent dataset {parent_domain_name}."
                            )
                        elif not found_val:
                            errors.append(
                                f"[{SUPPLEMENTAL_QUALIFIER_VIOLATION}] [{ds_name_upper}] Row {i}: Identifying variable value IDVARVAL='{idvarval}' for IDVAR='{idvar}' not found in parent record for subject '{usubjid}'."
                            )

    if errors:
        raise DatasetJSONValidationError(errors)
