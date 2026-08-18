"""
De-identification transforms for clinical text, including full masking,
deterministic pseudonymization, configurable date-shifting, and age capping.
"""

import hashlib
import hmac
import re
from datetime import timedelta
from typing import Any

from dateutil import parser as date_parser
from pydantic import BaseModel, Field

from packages.deid.detector import resolve_overlaps
from packages.deid.models import DetectionResult, DetectorCategory

# Documented configurable default for resolving conflicting date-shift windows
DEFAULT_DATE_SHIFT_DAYS = 365


class RedactionRecordItem(BaseModel):
    """
    Structured item in the redaction record detailing an individual redaction operation.
    Crucially, it excludes any raw matched identifiers to preserve privacy.
    """

    category: str = Field(..., description="The category of PII/PHI detected")
    strategy: str = Field(
        ...,
        description="The transform strategy applied (e.g., mask, pseudonymize, date_shift, age_cap)",
    )
    start: int = Field(
        ..., description="The character start offset in the original source text"
    )
    end: int = Field(
        ..., description="The character end offset in the original source text"
    )
    replacement: str = Field(..., description="The sanitized replacement text")


def pseudonymize_value(value: str, salt: str | bytes, prefix: str = "") -> str:
    """
    Generates a deterministic HMAC-SHA256 pseudonym for the given value.

    Args:
        value (str): The raw string value to pseudonymize.
        salt (Union[str, bytes]): The secret salt used for the HMAC operation.
        prefix (str): An optional study-specific prefix to prepend.

    Returns:
        str: Prepended prefix + Hex-encoded HMAC-SHA256 of the value.
    """
    if isinstance(salt, str):
        salt = salt.encode("utf-8")
    h = hmac.new(salt, value.encode("utf-8"), hashlib.sha256).hexdigest()
    if prefix:
        return f"{prefix}{h}"
    return h


def shift_date_string(date_str: str, shift_days: int = DEFAULT_DATE_SHIFT_DAYS) -> str:
    """
    Parses a date string and shifts it by shift_days while attempting to preserve its format.

    Args:
        date_str (str): The raw date string.
        shift_days (int): The number of days to shift. Defaults to 365.

    Returns:
        str: The formatted shifted date string, or "[DATE_INVALID]" if parsing fails.
    """
    try:
        dt = date_parser.parse(date_str)
        shifted_dt = dt + timedelta(days=shift_days)

        # Format preservation heuristics
        if re.match(r"^\d{4}-\d{2}-\d{2}$", date_str):
            return shifted_dt.strftime("%Y-%m-%d")
        if re.match(r"^\d{4}/\d{2}/\d{2}$", date_str):
            return shifted_dt.strftime("%Y/%m/%d")
        if re.match(r"^\d{2}/\d{2}/\d{4}$", date_str):
            return shifted_dt.strftime("%m/%d/%Y")
        if re.match(r"^\d{1,2}-[a-zA-Z]{3}-\d{4}$", date_str, re.IGNORECASE):
            return shifted_dt.strftime("%d-%b-%Y")
        if re.match(r"^[a-zA-Z]{3}\s+\d{1,2},?\s+\d{4}$", date_str, re.IGNORECASE):
            # e.g., "Jan 15, 2026" or "Jan 15 2026"  # deid-ignore
            has_comma = "," in date_str
            fmt = "%b %d, %Y" if has_comma else "%b %d %Y"
            return shifted_dt.strftime(fmt)

        # Fallback to standard ISO formatting
        return shifted_dt.strftime("%Y-%m-%d")
    except Exception:
        return "[DATE_INVALID]"


def cap_age_string(age_str: str, cap: int = 89) -> str:
    """
    Finds the numeric age value in a string, and if it exceeds the cap, generalizes it.

    Args:
        age_str (str): The age matched string (e.g., "age 95", "92 years old").  # deid-ignore
        cap (int): The maximum age limit. Defaults to 89.

    Returns:
        str: The generalized age string, or the original if age is below or equal to cap.
    """
    pattern = re.compile(
        r"\b(?:age[sd]?|age of)\s*[:\- ]?\s*(\d{1,3})\b"
        r"|\b(\d{1,3})\s*(?:years?\s*(?:of\s*age)?\s*old|-years?-old|yo|-yo)\b",
        re.IGNORECASE,
    )

    matches = list(re.finditer(pattern, age_str))
    if not matches:
        return age_str

    result_parts = list(age_str)
    for match in reversed(matches):
        start_idx = -1
        end_idx = -1
        age_val_str = ""
        if match.group(1) is not None:
            start_idx = match.start(1)
            end_idx = match.end(1)
            age_val_str = match.group(1)
        elif match.group(2) is not None:
            start_idx = match.start(2)
            end_idx = match.end(2)
            age_val_str = match.group(2)

        if start_idx != -1 and end_idx != -1:
            try:
                age_val = int(age_val_str)
                if age_val > cap:
                    result_parts[start_idx:end_idx] = list(f"{cap}+")
            except Exception:
                pass

    return "".join(result_parts)


def apply_deid_transforms(
    text: str,
    results: list[DetectionResult],
    strategies: dict | None = None,
    default_strategy: str = "mask",
    salt: str | bytes = "secure-clinical-salt-98765",
    shift_days: int = DEFAULT_DATE_SHIFT_DAYS,
    age_cap: int = 89,
) -> tuple[str, list[RedactionRecordItem]]:
    """
    Apply de-identification transforms from right to left so original character offsets remain valid,
    and generate a redaction record that completely excludes raw matched identifiers.

    Args:
        text (str): Original source text.
        results (List[DetectionResult]): Detected PII/PHI occurrences.
        strategies (Optional[dict]): Map of DetectorCategory to specific strategy ("mask", "pseudonymize", "date_shift", "age_cap").
        default_strategy (str): Default strategy to use if none is specified for a category. Defaults to "mask".
        salt (Union[str, bytes]): Salt used for deterministic pseudonymization.
        shift_days (int): Shifts dates by this number of days. Defaults to DEFAULT_DATE_SHIFT_DAYS (365).
        age_cap (int): Caps ages above this limit. Defaults to 89.

    Returns:
        tuple[str, List[RedactionRecordItem]]: Redacted text and a list of redaction details.
    """
    # 1. Resolve overlaps first
    clean_results = resolve_overlaps(results)

    parts = list(text)
    redaction_record: list[RedactionRecordItem] = []

    # Process from right to left so offsets remain valid
    for res in reversed(clean_results):
        strategy = "mask"
        if strategies and res.category in strategies:
            strategy = strategies[res.category]
        elif default_strategy:
            strategy = default_strategy

        # Ensure strategy is valid, otherwise fallback to "mask"
        if strategy not in ("mask", "pseudonymize", "date_shift", "age_cap"):
            strategy = "mask"

        # Handle transformation based on strategy
        replacement = f"[{res.category.upper()}]"
        if strategy == "mask":
            replacement = f"[{res.category.upper()}]"
        elif strategy == "pseudonymize":
            replacement = pseudonymize_value(res.value, salt)
        elif strategy == "date_shift":
            if res.category == DetectorCategory.DATES:
                replacement = shift_date_string(res.value, shift_days)
            else:
                replacement = f"[{res.category.upper()}]"
        elif strategy == "age_cap":
            if res.category == DetectorCategory.AGE:
                replacement = cap_age_string(res.value, age_cap)
            else:
                replacement = f"[{res.category.upper()}]"

        parts[res.start : res.end] = list(replacement)

        redaction_record.append(
            RedactionRecordItem(
                category=res.category,
                strategy=strategy,
                start=res.start,
                end=res.end,
                replacement=replacement,
            )
        )

    redaction_record.reverse()
    transformed_text = "".join(parts)
    return transformed_text, redaction_record


def pseudonymize_subject_id(
    subject_id: str, salt: str | bytes, prefix: str = ""
) -> str:
    """
    Generates a deterministic study-specific pseudonym for a subject ID.
    """
    return pseudonymize_value(subject_id, salt, prefix)


def get_subject_date_shift(subject_id: str, salt: str | bytes) -> int:
    """
    Calculates a stable, deterministic date-shift offset in days for a given subject.
    Maps to range [-365, 365] inclusive (731 possible days).
    """
    if not subject_id:
        return 0
    # Generate HMAC-SHA256 pseudonym of subject_id without prefix
    h = pseudonymize_value(subject_id, salt)
    return (int(h, 16) % 731) - 365


def shift_date_by_subject(
    date_val: Any,
    subject_id: str,
    salt: str | bytes,
) -> Any:
    """
    Shifts a date value (standard ISO string, numeric SAS date, or float/int SAS date)
    deterministically per subject.
    """
    if date_val is None:
        return None
    offset = get_subject_date_shift(subject_id, salt)

    # Check if numeric SAS date (must not be boolean)
    if isinstance(date_val, (int, float)) and not isinstance(date_val, bool):
        return date_val + offset

    # Check if string
    if isinstance(date_val, str):
        val_strip = date_val.strip()
        if not val_strip:
            return date_val
        # Check if integer
        if re.match(r"^-?\d+$", val_strip):
            return int(val_strip) + offset
        # Check if float
        if re.match(r"^-?\d+\.\d+$", val_strip):
            return float(val_strip) + offset

        # Standard ISO or textual date string
        return shift_date_string(val_strip, offset)

    return date_val


def cap_age_numeric(age: int | float, cap: int = 89) -> int | float:
    """
    Caps a numeric age value if it exceeds the specified cap.
    """
    if isinstance(age, bool):
        return age
    if age > cap:
        if isinstance(age, float):
            return float(cap)
        return int(cap)
    return age


def normalize_and_cap_age(age_val: Any, cap: int = 89) -> Any:
    """
    Parses, normalizes, and caps all forms of age values (integers, floats, decimal strings,
    and string-appended units/suffixes) if they exceed the specified cap.

    If age is string-based, returns the capped age as a string (e.g., '89').
    If it is numeric float/int, preserves its type (e.g., 89.0 or 89).
    Ages equal to or below cap are returned unaltered.
    Non-age values, None, and bool values are returned untouched.
    """
    if age_val is None or isinstance(age_val, bool):
        return age_val

    if isinstance(age_val, (int, float)):
        if age_val > cap:
            if isinstance(age_val, float):
                return float(cap)
            return int(cap)
        return age_val

    if isinstance(age_val, str):
        val_strip = age_val.strip()
        if not val_strip:
            return age_val

        # Centralized parser uses robust regular expressions to parse decimal strings and units
        match = re.search(r"(\d+(?:\.\d+)?)", val_strip)
        if not match:
            return age_val

        num_str = match.group(1)
        try:
            val = float(num_str) if "." in num_str else int(num_str)

            if val > cap:
                return str(cap)
            return age_val
        except ValueError:
            return age_val

    return age_val


def scrub_error_message(msg: str) -> str:
    """
    Scrubs and redacts raw subject, site, and study identifiers, quoted field values,
    and PII to prevent leaking PHI in audit logs and diagnostic messages.
    """
    if not msg:
        return msg

    # Redact SSNs, emails, phones
    msg = re.sub(r"\b\d{3}-\d{2}-\d{4}\b", "[REDACTED_SSN]", msg)
    msg = re.sub(
        r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        "[REDACTED_EMAIL]",
        msg,
    )
    msg = re.sub(
        r"\b(?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}\b",
        "[REDACTED_PHONE]",
        msg,
    )

    # Redact subject patterns like SUBJ-101, USUBJID-123, SUBJID: 1001, subject_001, PATIENT-001
    msg = re.sub(
        r"\b(?:USUBJID|SUBJID|SUBJECT|PATIENT)[_:-]+\s*[\w-]+\b",
        "[REDACTED_SUBJECT]",
        msg,
        flags=re.IGNORECASE,
    )
    msg = re.sub(r"\bSUBJ-[\w-]+\b", "[REDACTED_SUBJECT]", msg, flags=re.IGNORECASE)

    # Redact site patterns like SITE-01, SITEID_99, site-A
    msg = re.sub(
        r"\b(?:SITEID|SITE)[_:-]+\s*[\w-]+\b",
        "[REDACTED_SITE]",
        msg,
        flags=re.IGNORECASE,
    )

    # Redact study patterns like STUDY-001, STUDYID_123
    msg = re.sub(
        r"\b(?:STUDYID|STUDY)[_:-]+\s*[\w-]+\b",
        "[REDACTED_STUDY]",
        msg,
        flags=re.IGNORECASE,
    )

    # Redact quoted field values
    msg = re.sub(r"'(.*?)'", "'[REDACTED_VALUE]'", msg)
    return re.sub(r"\"(.*?)\"", '"[REDACTED_VALUE]"', msg)
