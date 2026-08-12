"""Field-Level Data Masking and Blinded Data Protection for Execution Service.

Provides fine-grained data masking helpers for Clinical Observations, Subject Data,
and RTSM Randomization Records based on granular PermissionEnum and unblinded access claims.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

from typing import Any

from packages.security.permissions import PermissionEnum

# Fields subject to unblinded access protection (blinded treatment arms, doses, kit numbers)
BLINDED_TREATMENT_FIELDS: set[str] = {
    "treatment_arm",
    "unblinded_dose",
    "kit_number",
    "randomization_code",
    "investigational_product_batch",
}

# Fields subject to PII/PHI redaction
PII_PHI_FIELDS: set[str] = {
    "ssn",
    "social_security_number",
    "first_name",
    "last_name",
    "full_name",
    "phone_number",
    "email_address",
    "home_address",
}

MASKED_REPLACEMENT_TEXT = "***MASKED***"


def mask_clinical_record(
    record: dict[str, Any],
    permissions: set[PermissionEnum],
    unblinded_access: bool = False,
) -> dict[str, Any]:
    """Mask sensitive blinded fields and PII in a single clinical record dictionary.

    Args:
        record: Dictionary representation of a clinical observation or subject record.
        permissions: Set of PermissionEnum members assigned to the caller.
        unblinded_access: True if explicit unblinded access claim is active.

    Returns:
        Masked record dictionary.
    """
    masked_record = dict(record)

    can_unblind = unblinded_access or (PermissionEnum.EXPERT_UNBLIND in permissions)

    for field_name, value in list(masked_record.items()):
        if value is None:
            continue

        lower_field = field_name.lower()

        # Blinded treatment fields masking
        if (
            lower_field in BLINDED_TREATMENT_FIELDS
            and not can_unblind
            or lower_field in PII_PHI_FIELDS
        ):
            masked_record[field_name] = MASKED_REPLACEMENT_TEXT

        # Handle nested dictionary or list items
        elif isinstance(value, dict):
            masked_record[field_name] = mask_clinical_record(
                value, permissions, unblinded_access
            )
        elif isinstance(value, list):
            masked_record[field_name] = [
                mask_clinical_record(item, permissions, unblinded_access)
                if isinstance(item, dict)
                else item
                for item in value
            ]

    return masked_record


def mask_clinical_records_list(
    records: list[dict[str, Any]],
    permissions: set[PermissionEnum],
    unblinded_access: bool = False,
) -> list[dict[str, Any]]:
    """Mask sensitive blinded fields and PII across a list of clinical records.

    Args:
        records: List of record dictionaries.
        permissions: Set of PermissionEnum members assigned to caller.
        unblinded_access: True if explicit unblinded access claim is active.

    Returns:
        List of masked record dictionaries.
    """
    return [mask_clinical_record(rec, permissions, unblinded_access) for rec in records]


def apply_rtsm_blinded_filter(data: dict, roles: Any) -> dict:
    """
    Response-serialization filter that, for blinded roles, replaces
    treatment_group, randomization_seed, and investigational_product_id with masked placeholders
    (and returns only ciphertext at the network layer), keyed off request.state.roles.
    """
    from packages.security.rbac import normalize_role

    if isinstance(roles, str):
        raw_roles = [r.strip() for r in roles.split(",") if r.strip()]
    elif isinstance(roles, (list, set, tuple)):
        raw_roles = [str(r).strip() for r in roles if str(r).strip()]
    else:
        raw_roles = []

    normalized_roles = [normalize_role(r) for r in raw_roles]

    unblinded_roles = {
        "unblinded_statistician",
        "idmc",
        "pharmacist",
        "emergency_unblinder",
        "sysadmin",
        "sponsor_dm",
        "sponsor_mm",
        "admin",
        "DataManager",
        "datamanager",
        "SponsorAdmin",
        "sponsoradmin",
        "sponsor_admin",
    }

    is_unblinded = any(r in unblinded_roles for r in normalized_roles)
    if is_unblinded:
        return data

    masked = dict(data)
    for field in (
        "treatment_group",
        "randomization_seed",
        "investigational_product_id",
    ):
        if field in masked:
            masked[field] = "MASKED"
    return masked
