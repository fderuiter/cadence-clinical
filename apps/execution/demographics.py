"""
Secure clinical subject demographics derivation and normalization helper.

This module provides reusable, import-safe server-side helpers to decrypt
and derive demographics (gender, age) relative to clinical observations.
It ensures that raw personally identifiable information (PII) is never
exposed in logs, exceptions, or audit fields, while returning safe defaults
allowing generic range-matching logic to execute successfully.
"""

import base64
import json
import logging
from datetime import date, datetime
from typing import Any

from cryptography.fernet import Fernet, InvalidToken

logger = logging.getLogger(__name__)

# Symmetric encryption helper for patient demographics (reused key for compatibility)
_DEMO_KEY = base64.urlsafe_b64encode(b"cadence_clinical_demographics_32")
_fernet = Fernet(_DEMO_KEY)


def encrypt_demographics(data: dict) -> str:
    """Securely encrypt demographics dictionary payload to protect PII.

    Args:
        data (dict): Dictionary containing patient identifying details (e.g., name, birthdate, gender).

    Returns:
        str: Encrypted and base64-encoded string representation of the demographics payload.
    """
    serialized = json.dumps(data)
    return _fernet.encrypt(serialized.encode("utf-8")).decode("utf-8")


def decrypt_demographics(encrypted_str: str) -> dict:
    """Decrypt demographic details to retrieve raw PII payload.

    Args:
        encrypted_str (str): The encrypted demographic payload.

    Returns:
        dict: Decrypted raw dictionary.

    Raises:
        InvalidToken: If the decryption key is incorrect or token is corrupted.
    """
    decrypted = _fernet.decrypt(encrypted_str.encode("utf-8"))
    return json.loads(decrypted.decode("utf-8"))


def normalize_gender(gender_str: str | None, preserve_custom: bool = False) -> str:
    """Normalize supported gender/sex input values into standard rule-engine codes (CDISC SEX).

    Args:
        gender_str (Optional[str]): Raw input gender or sex string (e.g. "Male", "Female", "M", "F", "U").
        preserve_custom (bool): If True, returns custom/unmapped sex values stripped and uppercased
                                instead of defaulting to "U".

    Returns:
        str: Normalized standard code: "M" for Male, "F" for Female, and "U" for Unknown/Others.
    """
    if not gender_str:
        return "U"

    # Strip whitespace and convert to upper-case for robust case-insensitive comparison
    normalized = gender_str.strip().upper()

    # Normalize common variations of male and female
    if normalized in ("M", "MALE", "BOY", "MAN"):
        return "M"
    if normalized in ("F", "FEMALE", "GIRL", "WOMAN"):
        return "F"
    if normalized in ("U", "UNKNOWN", "UNK"):
        return "U"

    if preserve_custom and normalized:
        return normalized

    # Default to "U" (CDISC Unknown) for unmapped/absent/unknown values
    return "U"


def _parse_date_string(date_str: str) -> date | None:
    """Helper to parse common date strings safely.

    Args:
        date_str (str): String representing date or datetime.

    Returns:
        Optional[date]: Extracted date object, or None if parsing fails.
    """
    date_str = date_str.strip()
    if not date_str:
        return None

    try:
        # Check if the string has ISO 8601 time separator 'T'
        if "T" in date_str:
            # Replace 'Z' UTC offset with standard offset to support fromisoformat in older versions
            normalized_str = date_str.replace("Z", "+00:00")
            dt = datetime.fromisoformat(normalized_str)
            return dt.date()

        # Otherwise, try simple YYYY-MM-DD date parsing
        return date.fromisoformat(date_str)
    except Exception:
        # Fallback split for formats like "YYYY-MM-DD HH:MM:SS"
        try:
            parts = date_str.split()
            if parts:
                return date.fromisoformat(parts[0])
        except Exception:
            pass
        return None


def calculate_age(
    birthdate: date | datetime | str | None,
    observation_date: date | datetime | str | None,
) -> float | None:
    """Calculate subject's precise decimal age relative to the observation date.

    Handles boundary dates such as birthdays on, before, or after the observation date.
    Returns decimal age based on exact calendar days, or None if safe derivation fails.

    Args:
        birthdate (Union[date, datetime, str, None]): The subject's birthdate.
        observation_date (Union[date, datetime, str, None]): The date of observation.

    Returns:
        Optional[float]: Derived decimal age, or None if safe derivation fails.
    """
    if not birthdate or not observation_date:
        return None

    try:
        # Normalize birthdate to a python date object
        if isinstance(birthdate, str):
            b_dt = _parse_date_string(birthdate)
        elif isinstance(birthdate, datetime):
            b_dt = birthdate.date()
        elif isinstance(birthdate, date):
            b_dt = birthdate
        else:
            return None

        # Normalize observation_date to a python date object
        if isinstance(observation_date, str):
            o_dt = _parse_date_string(observation_date)
        elif isinstance(observation_date, datetime):
            o_dt = observation_date.date()
        elif isinstance(observation_date, date):
            o_dt = observation_date
        else:
            return None

        # Validate both dates were parsed successfully
        if b_dt is None or o_dt is None:
            return None

        # Compute exact calendar-day subtraction as a precise decimal float
        return (o_dt - b_dt).days / 365.25

    except Exception:
        # Log generic error strictly without exposing sensitive birthdate inputs
        logger.warning(
            "Safe failure during subject age calculation relative to observation date."
        )
        return None


def get_safe_demographics(
    subject: Any,
    observation_date: date | datetime | str | None,
    preserve_custom: bool = False,
) -> dict[str, Any]:
    """Securely extract range-matching demographics from ClinicalSubject without exposing raw PII.

    This function safely decrypts ClinicalSubject.encrypted_demographics and extracts
    only the normalized gender/sex and age required for reference range evaluation, ensuring
    strict adherence to HIPAA, GDPR, and GxP standards. Raw PII is never logged, exposed,
    or stored unencrypted.

    Args:
        subject (Any): ClinicalSubject instance, a dict, or raw encrypted ciphertext string.
        observation_date (Union[date, datetime, str, None]): Date of the observation.
        preserve_custom (bool): If True, preserves custom biological sex strings.

    Returns:
        Dict[str, Any]: Safe demographic profile with 'gender' (str) and 'age' (int/None).
    """
    result = {
        "gender": "U",
        "age": None,
    }

    # Retrieve ciphertext string from the inputs
    encrypted_str = None
    if isinstance(subject, str):
        encrypted_str = subject
    elif isinstance(subject, dict):
        encrypted_str = subject.get("encrypted_demographics")
    elif subject is not None and hasattr(subject, "encrypted_demographics"):
        encrypted_str = subject.encrypted_demographics

    if not encrypted_str:
        return result

    try:
        # Decrypt demographic payload
        decrypted_data = decrypt_demographics(encrypted_str)
        if not isinstance(decrypted_data, dict):
            logger.warning(
                "Decrypted demographics content is not structured as a dictionary."
            )
            return result

        # Normalize gender/sex
        raw_gender = decrypted_data.get("gender") or decrypted_data.get("sex")
        result["gender"] = normalize_gender(raw_gender, preserve_custom=preserve_custom)

        # Parse birthdate and calculate observation-relative age
        raw_birthdate = (
            decrypted_data.get("birthdate")
            or decrypted_data.get("date_of_birth")
            or decrypted_data.get("dob")
        )
        result["age"] = calculate_age(raw_birthdate, observation_date)

    except InvalidToken:
        logger.warning(
            "Demographics decryption failed: Invalid token or incorrect secret key."
        )
    except Exception:
        logger.warning(
            "Safely handled an unexpected error during demographics derivation."
        )

    return result
