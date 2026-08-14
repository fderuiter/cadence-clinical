import base64
import json
import logging
from datetime import date, datetime, timedelta
from typing import Any

from cryptography.fernet import Fernet, InvalidToken

logger = logging.getLogger(__name__)

# Symmetric encryption helper for patient demographics (reused key for compatibility)
_DEMO_KEY = base64.urlsafe_b64encode(b"cadence_clinical_demographics_32")
_fernet = Fernet(_DEMO_KEY)


def encrypt_demographics(data: dict) -> str:
    """Securely encrypt demographics dictionary payload to protect PII."""
    serialized = json.dumps(data)
    return _fernet.encrypt(serialized.encode("utf-8")).decode("utf-8")


def decrypt_demographics(encrypted_str: str) -> dict:
    """Decrypt demographic details to retrieve raw PII payload."""
    decrypted = _fernet.decrypt(encrypted_str.encode("utf-8"))
    return json.loads(decrypted.decode("utf-8"))


def normalize_gender(gender_str: str | None, preserve_custom: bool = False) -> str:
    """Normalize supported gender/sex input values into standard rule-engine codes (CDISC SEX)."""
    if not gender_str:
        return "U"

    normalized = gender_str.strip().upper()

    if normalized in ("M", "MALE", "BOY", "MAN"):
        return "M"
    if normalized in ("F", "FEMALE", "GIRL", "WOMAN"):
        return "F"
    if normalized in ("U", "UNKNOWN", "UNK"):
        return "U"

    if preserve_custom and normalized:
        return normalized

    return "U"


def _parse_date_string(date_str: str) -> date | None:
    """Helper to parse common date strings safely."""
    date_str = date_str.strip()
    if not date_str:
        return None

    try:
        if "T" in date_str:
            normalized_str = date_str.replace("Z", "+00:00")
            dt = datetime.fromisoformat(normalized_str)
            return dt.date()
        return date.fromisoformat(date_str)
    except Exception:
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
    """Calculate subject's precise decimal age relative to the observation date."""
    if not birthdate or not observation_date:
        return None

    try:
        if isinstance(birthdate, str):
            b_dt = _parse_date_string(birthdate)
        elif isinstance(birthdate, datetime):
            b_dt = birthdate.date()
        elif isinstance(birthdate, date):
            b_dt = birthdate
        else:
            return None

        if isinstance(observation_date, str):
            o_dt = _parse_date_string(observation_date)
        elif isinstance(observation_date, datetime):
            o_dt = observation_date.date()
        elif isinstance(observation_date, date):
            o_dt = observation_date
        else:
            return None

        if b_dt is None or o_dt is None:
            return None

        return (o_dt - b_dt).days / 365.25

    except Exception:
        logger.warning(
            "Safe failure during subject age calculation relative to observation date."
        )
        return None


def get_safe_demographics(
    subject: Any,
    observation_date: date | datetime | str | None,
    preserve_custom: bool = False,
) -> dict[str, Any]:
    """Securely extract range-matching demographics from ClinicalSubject without exposing raw PII."""
    result = {
        "gender": "U",
        "age": None,
    }

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
        decrypted_data = decrypt_demographics(encrypted_str)
        if not isinstance(decrypted_data, dict):
            logger.warning(
                "Decrypted demographics content is not structured as a dictionary."
            )
            return result

        raw_gender = decrypted_data.get("gender") or decrypted_data.get("sex")
        result["gender"] = normalize_gender(raw_gender, preserve_custom=preserve_custom)

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
