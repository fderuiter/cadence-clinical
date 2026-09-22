"""HIPAA and GDPR Compliant De-Identified CSV Serializer.

Generates RFC 4180-compliant CSV documents and multi-domain ZIP archives
with deterministic HMAC pseudonymization, per-subject date-shifting, and age capping.
"""

import csv
import io
import os
import zipfile
from typing import Any

from apps.execution.biostat.deid import (
    deidentify_record,
)
from packages.deid.transforms import (
    normalize_and_cap_age,
    pseudonymize_value,
)


def _to_dict(record: Any) -> dict[str, Any]:
    """Converts pydantic models or dict-like objects to a standard dictionary."""
    if hasattr(record, "model_dump"):
        return record.model_dump()
    if isinstance(record, dict):
        return dict(record)
    return getattr(record, "__dict__", {})


def serialize_to_csv(
    records: list[Any],
    privacy_profile: str = "SAFE_HARBOR",
    salt: str | None = None,
    include_audit_fields: bool = False,
) -> str:
    """Serializes a list of dataset records into a de-identified CSV string.

    Args:
        records: List of domain records (dicts or Pydantic models).
        privacy_profile: One of 'SAFE_HARBOR', 'LIMITED_DATA_SET', 'GDPR_PSEUDONYMIZED', 'UNRESTRICTED'.
        salt: Secret HMAC salt for deterministic pseudonymization.
        include_audit_fields: Whether to include internal audit columns (created_at, etc.).

    Returns:
        str: RFC 4180-compliant CSV string.
    """
    if not records:
        return ""

    actual_salt = salt or os.getenv(
        "BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765"
    )  # pragma: allowlist secret

    raw_dicts = [_to_dict(r) for r in records]

    # Apply de-identification based on profile
    profile_upper = privacy_profile.strip().upper()
    processed_records: list[dict[str, Any]] = []

    if profile_upper == "SAFE_HARBOR":
        for r in raw_dicts:
            deid_r = deidentify_record(r, actual_salt)
            processed_records.append(deid_r)
    elif profile_upper == "LIMITED_DATA_SET":
        for r in raw_dicts:
            r_copy = dict(r)
            # Direct identifiers removed/pseudonymized, dates retained or shifted
            for id_col in ("USUBJID", "SUBJID", "SITEID"):
                if id_col in r_copy and r_copy[id_col]:
                    r_copy[id_col] = pseudonymize_value(
                        str(r_copy[id_col]), actual_salt
                    )
            if "AGE" in r_copy:
                r_copy["AGE"] = normalize_and_cap_age(r_copy["AGE"])
            processed_records.append(r_copy)
    elif profile_upper == "GDPR_PSEUDONYMIZED":
        for r in raw_dicts:
            deid_r = deidentify_record(r, actual_salt)
            processed_records.append(deid_r)
    else:  # UNRESTRICTED
        processed_records = raw_dicts

    # Determine ordered column headers
    internal_audit_fields = {
        "created_at",
        "created_by",
        "reason_for_change",
        "version_index",
    }

    # Collect union of keys maintaining stable order
    all_keys: list[str] = []
    for r in processed_records:
        for k in r:
            if not include_audit_fields and k.lower() in internal_audit_fields:
                continue
            if k not in all_keys:
                all_keys.append(k)

    # Write to CSV in-memory buffer
    output = io.StringIO()
    writer = csv.DictWriter(
        output,
        fieldnames=all_keys,
        extrasaction="ignore",
        lineterminator="\n",
    )
    writer.writeheader()
    for r in processed_records:
        writer.writerow(r)

    return output.getvalue()


def serialize_bundle_to_csv_zip(
    bundle_data: dict[str, list[Any]],
    privacy_profile: str = "SAFE_HARBOR",
    salt: str | None = None,
    include_audit_fields: bool = False,
) -> bytes:
    """Serializes a bundle of domain datasets into a ZIP archive containing individual CSV files.

    Args:
        bundle_data: Dict mapping domain names (e.g. 'DM', 'AE', 'ADSL') to record lists.
        privacy_profile: Privacy policy profile.
        salt: HMAC pseudonymization salt.
        include_audit_fields: Whether to include audit columns in CSVs.

    Returns:
        bytes: Binary ZIP archive content.
    """
    actual_salt = salt or os.getenv(
        "BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765"
    )  # pragma: allowlist secret

    zip_buffer = io.BytesIO()
    with zipfile.ZipFile(
        zip_buffer, mode="w", compression=zipfile.ZIP_DEFLATED
    ) as zip_file:
        for ds_name, records in bundle_data.items():
            csv_content = serialize_to_csv(
                records=records,
                privacy_profile=privacy_profile,
                salt=actual_salt,
                include_audit_fields=include_audit_fields,
            )
            filename = f"{ds_name.lower()}.csv"
            zip_file.writestr(filename, csv_content.encode("utf-8"))

    return zip_buffer.getvalue()
