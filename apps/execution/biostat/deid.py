"""
De-identification module for SDTM/ADaM export pipelines.
Provides deterministic pseudonymization, stable per-subject date-shifting, and age capping.
"""

import re
from datetime import date, timedelta
from typing import Any

from packages.deid.transforms import (
    normalize_and_cap_age,
    pseudonymize_value,
    shift_date_string,
)
from packages.deid.transforms import (
    scrub_error_message as package_scrub_error_message,
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

PII_DIRECT = {
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

PII_DATES = {
    "birth_date",
    "birthdate",
    "dob",
    "date_of_birth",
}

AGE_FIELDS = {"age", "aage", "agetxt", "age_val", "age_value"}


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

        has_valid_month = False
        if len(parts) >= 3 and parts[2].isdigit():
            m_val = int(parts[2])
            if 1 <= m_val <= 12:
                month_str = f"{m_val:02d}"
                has_valid_month = True

        has_valid_day = False
        if len(parts) >= 5 and parts[4].isdigit():
            d_val = int(parts[4])
            if 1 <= d_val <= 31:
                day_str = f"{d_val:02d}"
                has_valid_day = True

        # Form a full date for fast calculation
        try:
            dt = date(int(year_str), int(month_str), int(day_str))
            shifted_dt = dt + timedelta(days=shift_days)

            shifted_year = f"{shifted_dt.year:04d}"
            shifted_month = f"{shifted_dt.month:02d}"
            shifted_day = f"{shifted_dt.day:02d}"

            # Reconstruct the original string, inserting shifted numeric parts
            new_parts = list(parts)
            new_parts[0] = shifted_year
            if len(parts) >= 3 and has_valid_month:
                new_parts[2] = shifted_month
            if len(parts) >= 5 and has_valid_day:
                new_parts[4] = shifted_day

            return "".join(new_parts) + time_part
        except Exception:
            pass

    # Fallback to standard shift_date_string if custom parse fails
    return shift_date_string(original_str, shift_days)


def deidentify_record(
    row: dict[str, Any],
    salt: str,
    offset_cache: dict[str, int] | None = None,
    pseudo_cache: dict[str, str] | None = None,
) -> dict[str, Any]:
    """
    Transforms a single SDTM/ADaM record without mutating the input.
    """
    r = dict(row)

    def _pseudo(val_str: str) -> str:
        if pseudo_cache is not None and val_str in pseudo_cache:
            return pseudo_cache[val_str]
        res = pseudonymize_value(val_str, salt)
        if pseudo_cache is not None:
            pseudo_cache[val_str] = res
        return res

    # 1. Resolve deterministic offset
    original_usubjid = r.get("USUBJID") or r.get("SUBJID") or r.get("subject_id") or ""
    offset = 0
    if (
        original_usubjid
        and isinstance(original_usubjid, str)
        and original_usubjid.strip()
    ):
        subj_key = original_usubjid.strip()
        if offset_cache is not None and subj_key in offset_cache:
            offset = offset_cache[subj_key]
        else:
            h = _pseudo(subj_key)
            # Map to range [-365, 365] inclusive (731 possible days)
            offset = (int(h, 16) % 731) - 365
            if offset_cache is not None:
                offset_cache[subj_key] = offset

    # 2. Pseudonymize USUBJID, SUBJID, SITEID, subject_id, site_id
    for identifier_field in ("USUBJID", "SUBJID", "SITEID", "subject_id", "site_id"):
        if identifier_field in r:
            val = r[identifier_field]
            if val is not None and isinstance(val, str) and val.strip():
                r[identifier_field] = _pseudo(val.strip())

    # 3. Direct PII scrubbing, date shifting, and age capping
    for field_name, val in list(r.items()):
        if val is None:
            continue
        fn_lower = field_name.lower()

        if fn_lower in PII_DIRECT:
            r[field_name] = "[REDACTED]"
            continue

        if (
            fn_lower in PII_DATES
            or field_name in SDTM_DATE_FIELDS
            or field_name in ADAM_DATE_FIELDS
            or field_name.endswith("DTC")
            or field_name.endswith("DT")
            or field_name.endswith("DTM")
            or fn_lower.endswith("_dt")
            or fn_lower.endswith("_date")
        ):
            if isinstance(val, (int, float)) and not isinstance(val, bool):
                r[field_name] = val + offset
            elif isinstance(val, str):
                val_strip = val.strip()
                if not val_strip:
                    continue
                if val_strip.isdigit() or (
                    val_strip.startswith("-") and val_strip[1:].isdigit()
                ):
                    r[field_name] = int(val_strip) + offset
                else:
                    r[field_name] = shift_partial_date(val_strip, offset)
            continue

        if fn_lower in AGE_FIELDS:
            r[field_name] = normalize_and_cap_age(val)

    if "AGE" in r and r["AGE"] is not None:
        r["AGE"] = normalize_and_cap_age(r["AGE"])

    return r


def deidentify_export_data(
    export_data: dict[str, list[dict[str, Any]]] | list[dict[str, Any]],
    salt: str,
) -> dict[str, list[dict[str, Any]]] | list[dict[str, Any]]:
    """
    Applies non-mutating de-identification transform over lists or bundles of SDTM/ADaM records.
    Uses in-memory offset and pseudonym caching for extreme high-throughput (<100ms for 1,000+ records).
    """
    offset_cache: dict[str, int] = {}
    pseudo_cache: dict[str, str] = {}

    def _transform(row: dict[str, Any]) -> dict[str, Any]:
        return deidentify_record(
            row, salt, offset_cache=offset_cache, pseudo_cache=pseudo_cache
        )

    if isinstance(export_data, dict):
        new_bundle = {}
        for ds_name, records in export_data.items():
            new_bundle[ds_name] = [_transform(r) for r in records]
        return new_bundle
    if isinstance(export_data, list):
        return [_transform(r) for r in export_data]
    return export_data


def scrub_error_message(msg: str) -> str:
    """
    Scrubs and redacts raw subject identifiers and quoted field values
    to prevent leaking PII/PHI in biostat export audit error logs.
    Delegates to package_scrub_error_message for unified platform scrubbing.
    """
    return package_scrub_error_message(msg)
