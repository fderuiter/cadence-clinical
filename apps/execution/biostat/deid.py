"""
De-identification module for SDTM/ADaM export pipelines.
Provides deterministic pseudonymization, stable per-subject date-shifting, and age capping.
"""

import re
from datetime import timedelta
from typing import Any

from dateutil import parser as date_parser

from packages.deid.transforms import (
    normalize_and_cap_age,
    pseudonymize_value,
    shift_date_string,
)

# Standard registry of date fields
SDTM_DATE_FIELDS = {
    "RFSTDTC",
    "RFENDTC",
    "BRTHDTC",
    "AELDTC",
    "AESTDTC",
    "AEENDTC",
    "VSDTC",
    "LBDTC",
    "MHSTDTC",
    "MHENDTC",
    "CMSTDTC",
    "CMENDTC",
}

ADAM_DATE_FIELDS = {
    "TRTSDT",
    "TRTEDT",
    "RANDT",
    "DTHDT",
    "EOSDT",
    "ASTDT",
    "AENDT",
}


def shift_partial_date(date_str: str, shift_days: int) -> str:
    """
    Parses full or partial dates and shifts them while keeping relative ordering
    and leaving imprecise/placeholder components untouched.
    """
    if not date_str:
        return date_str

    original_str = date_str.strip()
    if not original_str:
        return date_str

    # Split timestamp if present (e.g., T12:00:00)
    t_split = original_str.split("T")
    date_part = t_split[0]
    time_part = "T" + t_split[1] if len(t_split) > 1 else ""

    # Split by common date separators - or /
    parts = re.split(r"([- /])", date_part)

    # We expect a valid year as parts[0] to proceed with structured shifting
    if len(parts) >= 1 and parts[0].isdigit() and len(parts[0]) == 4:
        year_str = parts[0]
        month_str = "06"  # Default mid-year for partial/missing month
        day_str = "15"  # Default mid-month for partial/missing day

        if len(parts) >= 3 and parts[2].isdigit():
            month_str = parts[2]
        if len(parts) >= 5 and parts[4].isdigit():
            day_str = parts[4]

        # Form a full dummy date string for calculation
        dummy_date_str = f"{year_str}-{month_str}-{day_str}"
        try:
            dt = date_parser.parse(dummy_date_str)
            shifted_dt = dt + timedelta(days=shift_days)

            shifted_year = f"{shifted_dt.year:04d}"
            shifted_month = f"{shifted_dt.month:02d}"
            shifted_day = f"{shifted_dt.day:02d}"

            # Reconstruct the original string, inserting shifted numeric parts
            new_parts = list(parts)
            new_parts[0] = shifted_year
            if len(parts) >= 3 and parts[2].isdigit():
                new_parts[2] = shifted_month
            if len(parts) >= 5 and parts[4].isdigit():
                new_parts[4] = shifted_day

            return "".join(new_parts) + time_part
        except Exception:
            pass

    # Fallback to standard shift_date_string if custom parse fails
    return shift_date_string(original_str, shift_days)


def deidentify_record(row: dict[str, Any], salt: str) -> dict[str, Any]:
    """
    Transforms a single SDTM/ADaM record without mutating the input.
    """
    # Create a shallow/deep copy of the dictionary
    r = dict(row)

    # 1. Resolve deterministic offset
    # Derive a stable per-subject date-shift offset from the record's original USUBJID
    # Capture the original USUBJID before modifying/pseudonymizing it!
    original_usubjid = r.get("USUBJID")
    offset = 0
    if (
        original_usubjid
        and isinstance(original_usubjid, str)
        and original_usubjid.strip()
    ):
        h = pseudonymize_value(original_usubjid, salt)
        # Map to range [-365, 365] inclusive (731 possible days)
        offset = (int(h, 16) % 731) - 365

    # 2. Pseudonymize USUBJID, SUBJID, SITEID
    for identifier_field in ("USUBJID", "SUBJID", "SITEID"):
        if identifier_field in r:
            val = r[identifier_field]
            if val is not None and isinstance(val, str) and val.strip():
                r[identifier_field] = pseudonymize_value(val, salt)

    # 3. Direct PII scrubbing and date shifting
    pii_direct = {
        "patient_name",
        "patientname",
        "name",
        "first_name",
        "last_name",
        "ssn",
        "social_security_number",
        "email",
        "phone",
        "telephone",
        "address",
        "street",
        "zipcode",
        "postal_code",
    }
    pii_dates = {
        "birth_date",
        "birthdate",
        "dob",
        "date_of_birth",
    }
    for field_name in list(r.keys()):
        fn_lower = field_name.lower()
        if fn_lower in pii_direct:
            r[field_name] = "[REDACTED]"
        elif fn_lower in pii_dates or field_name in SDTM_DATE_FIELDS:
            val = r[field_name]
            if val is not None and isinstance(val, str) and val.strip():
                r[field_name] = shift_partial_date(val, offset)
        elif field_name in ADAM_DATE_FIELDS:
            val = r[field_name]
            # SAS dates are floats/ints. Skip booleans.
            if (
                val is not None
                and isinstance(val, (int, float))
                and not isinstance(val, bool)
            ):
                r[field_name] = val + offset

    # 4. Cap AGE field
    # Cap the AGE field (DM and any ADaM row carrying it) at the policy threshold (values > 89 set to 89)
    if "AGE" in r:
        r["AGE"] = normalize_and_cap_age(r["AGE"])

    return r


def deidentify_export_data(
    export_data: dict[str, list[dict[str, Any]]] | list[dict[str, Any]],
    salt: str,
) -> dict[str, list[dict[str, Any]]] | list[dict[str, Any]]:
    """
    Applies non-mutating de-identification transform over lists or bundles of SDTM/ADaM records.
    """
    if isinstance(export_data, dict):
        new_bundle = {}
        for ds_name, records in export_data.items():
            new_bundle[ds_name] = [deidentify_record(r, salt) for r in records]
        return new_bundle
    if isinstance(export_data, list):
        return [deidentify_record(r, salt) for r in export_data]
    return export_data


def scrub_error_message(msg: str) -> str:
    """
    Scrubs and redacts raw subject identifiers and quoted field values
    to prevent leaking PII/PHI in biostat export audit error logs.
    """
    if not msg:
        return msg
    # Redact subject patterns like SUBJ-101, SUBJ-INVALID
    msg = re.sub(r"\bSUBJ-\w+", "[REDACTED_SUBJECT]", msg)
    # Redact site patterns like SITE-A
    msg = re.sub(r"\bSITE-\w+", "[REDACTED_SITE]", msg)
    # Redact study patterns like STUDY-001
    msg = re.sub(r"\bSTUDY-\w+", "[REDACTED_STUDY]", msg)
    # Redact any single/double quoted strings (which often hold raw values/IDs in errors)
    msg = re.sub(r"'(.*?)'", "'[REDACTED_VALUE]'", msg)
    return re.sub(r"\"(.*?)\"", '"[REDACTED_VALUE]"', msg)
