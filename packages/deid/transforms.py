"""
De-identification transforms for clinical text, including full masking,
deterministic pseudonymization, configurable date-shifting, and age capping.
"""

import hashlib
import hmac
import re
from datetime import timedelta
from typing import List, Optional, Union

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


def pseudonymize_value(value: str, salt: Union[str, bytes]) -> str:
    """
    Generates a deterministic HMAC-SHA256 pseudonym for the given value.

    Args:
        value (str): The raw string value to pseudonymize.
        salt (Union[str, bytes]): The secret salt used for the HMAC operation.

    Returns:
        str: Hex-encoded HMAC-SHA256 of the value.
    """
    if isinstance(salt, str):
        salt = salt.encode("utf-8")
    return hmac.new(salt, value.encode("utf-8"), hashlib.sha256).hexdigest()


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
        elif re.match(r"^\d{4}/\d{2}/\d{2}$", date_str):
            return shifted_dt.strftime("%Y/%m/%d")
        elif re.match(r"^\d{2}/\d{2}/\d{4}$", date_str):
            return shifted_dt.strftime("%m/%d/%Y")
        elif re.match(r"^\d{1,2}-[a-zA-Z]{3}-\d{4}$", date_str, re.IGNORECASE):
            return shifted_dt.strftime("%d-%b-%Y")
        elif re.match(r"^[a-zA-Z]{3}\s+\d{1,2},?\s+\d{4}$", date_str, re.IGNORECASE):
            # e.g., "Jan 15, 2026" or "Jan 15 2026"
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
        age_str (str): The age matched string (e.g., "age 95", "92 years old").
        cap (int): The maximum age limit. Defaults to 89.

    Returns:
        str: The generalized age string, or the original if age is below or equal to cap.
    """
    match = re.search(r"\d{1,3}", age_str)
    if not match:
        return age_str

    try:
        age_val = int(match.group())
        if age_val > cap:
            return age_str.replace(match.group(), f"{cap}+", 1)
    except Exception:
        pass

    return age_str


def apply_deid_transforms(
    text: str,
    results: List[DetectionResult],
    strategies: Optional[dict] = None,
    default_strategy: str = "mask",
    salt: Union[str, bytes] = "secure-clinical-salt-98765",
    shift_days: int = DEFAULT_DATE_SHIFT_DAYS,
    age_cap: int = 89,
) -> tuple[str, List[RedactionRecordItem]]:
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
    redaction_record: List[RedactionRecordItem] = []

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
