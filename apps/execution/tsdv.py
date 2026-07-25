import hashlib
from typing import Any, Optional


def is_subject_selected_for_sdv(
    config: Any, subject_uuid: str, enrollment_index: int
) -> bool:
    """Determine deterministically if a subject is selected for SDV.

    - The first configured number of subjects (initial_full_sdv_subject_count) receive full SDV (returns True).
    - Subsequent subjects are sampled based on random_sample_percentage and trial_random_seed.
    - Standard random selection must be deterministic for the same trial_random_seed and subject UUID.
    - Boundary values 0.0 and 100.0 are handled explicitly.
    """
    initial_count = getattr(config, "initial_full_sdv_subject_count", 0) or 0
    if initial_count < 0:
        initial_count = 0

    if enrollment_index < initial_count:
        return True

    percentage = getattr(config, "random_sample_percentage", 0.0) or 0.0
    if percentage >= 100.0:
        return True
    if percentage <= 0.0:
        return False

    seed = getattr(config, "trial_random_seed", None)
    if seed is None:
        seed = 0

    # Deterministic pseudo-random sampling using SHA-256
    key = f"{seed}:{subject_uuid}".encode("utf-8")
    h = hashlib.sha256(key).hexdigest()

    # Convert the first 16 hex characters (64 bits) to an integer
    # Divide by max 64-bit integer to get a uniform float in [0.0, 100.0]
    val = int(h[:16], 16) / 0xFFFFFFFFFFFFFFFF * 100.0
    return val < percentage


def is_field_required(config: Any, domain: str) -> bool:
    """Determine if a domain/field is required for SDV based on config.

    Precedence rule:
    - Safety/full-SDV domains always require SDV (returns True).
    - Zero-SDV/exploratory domains never require SDV (returns False).
    - If a domain is in both, safety/full-SDV takes precedence to prevent silent bypass.
    """
    if not domain:
        return False

    domain_norm = domain.strip().upper()

    full_sdv_domains = getattr(config, "full_sdv_domains", None) or []
    safety_endpoints = getattr(config, "safety_endpoints", None) or []
    zero_sdv_domains = getattr(config, "zero_sdv_domains", None) or []

    full_sdv_norm = [d.strip().upper() for d in full_sdv_domains if d]
    safety_norm = [d.strip().upper() for d in safety_endpoints if d]
    zero_sdv_norm = [d.strip().upper() for d in zero_sdv_domains if d]

    # Precedence check: Safety/full-SDV takes priority over zero-SDV
    if domain_norm in full_sdv_norm or domain_norm in safety_norm:
        return True

    if domain_norm in zero_sdv_norm:
        return False

    # Default to False for FIELD_BASED since only explicitly targeted fields require SDV
    return False


def evaluate_tsdv_requirement(
    config: Any,
    subject_uuid: str,
    enrollment_index: int,
    domain: Optional[str] = None,
) -> dict[str, Any]:
    """Evaluate TSDV requirement and return a transparent result with component rationale.

    Accepts the parameters needed to calculate a decision and combines subject/field
    decisions according to sampling_model.

    Supported sampling models:
    - SUBJECT_BASED:
        Decision is based entirely on subject selection (is_subject_selected).
    - FIELD_BASED:
        Decision is based entirely on field selection (is_field_required). Domain is required.
    - COMBINED:
        Combines subject and field selection:
        1. If domain in safety/full-SDV: required.
        2. If domain in zero-SDV: not required.
        3. If subject is selected: required.
        4. Else: not required.
        Domain is required.
    """
    sampling_model = getattr(config, "sampling_model", "SUBJECT_BASED")

    is_subj_sel = is_subject_selected_for_sdv(config, subject_uuid, enrollment_index)

    is_fld_req = None
    if domain:
        is_fld_req = is_field_required(config, domain)

    if sampling_model == "SUBJECT_BASED":
        required = is_subj_sel
        explanation = f"SUBJECT_BASED model: SDV requirement is determined solely by subject selection. Subject selected: {is_subj_sel}."
    elif sampling_model == "FIELD_BASED":
        if not domain:
            raise ValueError(
                "Domain parameter is required for FIELD_BASED sampling model."
            )
        required = is_fld_req
        explanation = f"FIELD_BASED model: SDV requirement is determined solely by field/domain selection. Domain '{domain}' required: {is_fld_req}."
    elif sampling_model == "COMBINED":
        if not domain:
            raise ValueError(
                "Domain parameter is required for COMBINED sampling model."
            )

        full_sdv_domains = getattr(config, "full_sdv_domains", None) or []
        safety_endpoints = getattr(config, "safety_endpoints", None) or []
        zero_sdv_domains = getattr(config, "zero_sdv_domains", None) or []

        domain_norm = domain.strip().upper()
        full_sdv_norm = [d.strip().upper() for d in full_sdv_domains if d]
        safety_norm = [d.strip().upper() for d in safety_endpoints if d]
        zero_sdv_norm = [d.strip().upper() for d in zero_sdv_domains if d]

        if domain_norm in full_sdv_norm or domain_norm in safety_norm:
            required = True
            explanation = (
                f"COMBINED model: Domain '{domain}' requires 100% SDV as a safety/full-SDV domain. "
                "Safety/full-SDV must not be silently bypassed."
            )
        elif domain_norm in zero_sdv_norm:
            required = False
            explanation = f"COMBINED model: Domain '{domain}' is designated as a zero-SDV domain, so SDV is not required."
        elif is_subj_sel:
            required = True
            explanation = (
                f"COMBINED model: Subject is selected for full SDV, and domain '{domain}' is "
                "not zero-SDV, so SDV is required."
            )
        else:
            required = False
            explanation = (
                f"COMBINED model: Subject is not selected for full SDV and domain '{domain}' "
                "is not a safety/full-SDV domain, so SDV is not required."
            )
    else:
        raise ValueError(f"Unsupported sampling model: {sampling_model}")

    return {
        "required": required,
        "sampling_model": sampling_model,
        "config_id": getattr(config, "id", None),
        "is_subject_selected": is_subj_sel,
        "is_field_required": is_fld_req,
        "explanation": explanation,
        "details": {
            "subject_uuid": subject_uuid,
            "enrollment_index": enrollment_index,
            "domain": domain,
            "initial_full_sdv_subject_count": getattr(
                config, "initial_full_sdv_subject_count", 0
            ),
            "random_sample_percentage": getattr(
                config, "random_sample_percentage", 0.0
            ),
            "trial_random_seed": getattr(config, "trial_random_seed", None),
        },
    }
