import hashlib
from typing import Optional, Tuple


def is_subject_selected_for_sdv(
    config, subject_uuid: str, enrollment_index: int
) -> bool:
    """Determine deterministically if a subject is selected for SDV based on the config.

    First N enrolled subjects always receive full SDV.
    Subsequent subjects are selected deterministically using a SHA-256 hash
    of the seed and subject_uuid.

    Args:
        config: Configuration object containing initial_full_sdv_subject_count,
                random_sample_percentage, and trial_random_seed.
        subject_uuid (str): Unique identifier of the subject.
        enrollment_index (int): Enrolled subject index (0-based).

    Returns:
        bool: True if selected for SDV, False otherwise.
    """
    initial_count = getattr(config, "initial_full_sdv_subject_count", 0) or 0
    if enrollment_index < initial_count:
        return True

    percentage = getattr(config, "random_sample_percentage", 0.0) or 0.0
    if percentage <= 0.0:
        return False
    if percentage >= 100.0:
        return True

    seed = getattr(config, "trial_random_seed", 0) or 0
    input_str = f"{seed}:{subject_uuid}"
    hasher = hashlib.sha256(input_str.encode("utf-8"))
    hex_digest = hasher.hexdigest()
    hash_int = int(hex_digest, 16)
    scaled_value = (hash_int / float(2**256)) * 100.0

    return scaled_value < percentage


def is_field_required(config, domain: Optional[str]) -> Optional[bool]:
    """Check if SDV is required/excluded for a domain based on configuration.

    Safety endpoints and full-SDV domains always require SDV, overriding zero-SDV.
    Zero-SDV domains never require SDV.
    Other domains return None (not configured).

    Args:
        config: Configuration object containing full_sdv_domains,
                safety_endpoints, and zero_sdv_domains.
        domain (str): The SDTM domain/field name.

    Returns:
        Optional[bool]: True if required, False if excluded, None if not explicitly configured.
    """
    if not domain:
        return None

    full_sdv = getattr(config, "full_sdv_domains", []) or []
    safety = getattr(config, "safety_endpoints", []) or []
    zero_sdv = getattr(config, "zero_sdv_domains", []) or []

    full_sdv_set = {d.upper().strip() for d in full_sdv if d}
    safety_set = {d.upper().strip() for d in safety if d}
    zero_sdv_set = {d.upper().strip() for d in zero_sdv if d}

    high_priority = full_sdv_set.union(safety_set)
    norm_domain = domain.upper().strip()

    if norm_domain in high_priority:
        return True
    if norm_domain in zero_sdv_set:
        return False

    return None


def evaluate_tsdv_requirement(
    config, subject_uuid: str, enrollment_index: int, domain: Optional[str] = None
) -> Tuple[bool, bool, Optional[bool], str]:
    """Combine subject and field decisions according to the configuration's sampling model.

    Args:
        config: Configuration object containing sampling_model and other sampling fields.
        subject_uuid (str): Unique identifier of the subject.
        enrollment_index (int): Enrolled subject index.
        domain (Optional[str]): SDTM domain code or field context.

    Returns:
        Tuple[bool, bool, Optional[bool], str]:
            - required (bool): Overall SDV requirement decision.
            - subject_selected (bool): Subject selection component.
            - field_decision (Optional[bool]): Field configuration component.
            - explanation (str): Readable explanation of the final decision.
    """
    sampling_model = (
        getattr(config, "sampling_model", "SUBJECT_BASED") or "SUBJECT_BASED"
    )
    subject_selected = is_subject_selected_for_sdv(
        config, subject_uuid, enrollment_index
    )
    field_decision = is_field_required(config, domain) if domain else None

    # Precedence validation
    # Safety endpoints and full-SDV domains always require SDV.
    # Zero-SDV domains never require SDV.
    # Combined, these take priority over subject-level decisions.
    if field_decision is True:
        explanation = (
            f"Required: Domain '{domain}' is a configured safety/full-SDV domain, which "
            f"takes absolute precedence under {sampling_model} model."
        )
        return True, subject_selected, field_decision, explanation

    if field_decision is False:
        explanation = (
            f"Not required: Domain '{domain}' is a configured zero-SDV domain, which "
            f"takes absolute precedence under {sampling_model} model."
        )
        return False, subject_selected, field_decision, explanation

    if sampling_model == "FIELD_BASED":
        explanation = (
            f"Not required: Under FIELD_BASED model, domain '{domain}' "
            "is not explicitly configured as a safety/full-SDV domain."
        )
        return False, subject_selected, field_decision, explanation

    # For SUBJECT_BASED and COMBINED (since field_decision is None, they behave similarly on the subject level)
    if subject_selected:
        initial_count = getattr(config, "initial_full_sdv_subject_count", 0) or 0
        if enrollment_index < initial_count:
            explanation = (
                f"Required: Subject is within the first {initial_count} enrolled subjects "
                f"(index {enrollment_index}) under {sampling_model} model."
            )
        else:
            pct = getattr(config, "random_sample_percentage", 0.0) or 0.0
            explanation = (
                f"Required: Subject selected via deterministic random sampling percentage ({pct}%) "
                f"under {sampling_model} model."
            )
        return True, subject_selected, field_decision, explanation
    else:
        pct = getattr(config, "random_sample_percentage", 0.0) or 0.0
        explanation = (
            f"Not required: Subject was not selected via deterministic random sampling percentage ({pct}%) "
            f"under {sampling_model} model."
        )
        return False, subject_selected, field_decision, explanation
