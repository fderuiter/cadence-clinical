"""
Unit tests for secure clinical subject demographics derivation and normalization.

Verifies deterministic age and sex extraction, safety on missing, malformed,
or undecryptable data, boundary birthdate conditions, and robust gender normalization.
"""

from datetime import date, datetime
from typing import Any

import pytest

from apps.execution.demographics import (
    calculate_age,
    decrypt_demographics,
    encrypt_demographics,
    get_safe_demographics,
    normalize_gender,
)


def test_demographics_encryption_decryption_roundtrip() -> None:
    """Verify that demographics encrypt and decrypt deterministically."""
    original_payload = {
        "name": "Jane Doe",
        "birthdate": "1995-10-25",
        "gender": "Female",
    }

    encrypted = encrypt_demographics(original_payload)
    assert isinstance(encrypted, str)
    assert encrypted != "Jane Doe"

    decrypted = decrypt_demographics(encrypted)
    assert decrypted == original_payload


@pytest.mark.parametrize(
    "gender_input, expected_normalized",
    [
        ("M", "M"),
        ("Male", "M"),
        ("  male  ", "M"),
        ("MALE", "M"),
        ("boy", "M"),
        ("man", "M"),
        ("F", "F"),
        ("Female", "F"),
        ("  female  ", "F"),
        ("FEMALE", "F"),
        ("girl", "F"),
        ("woman", "F"),
        ("U", "U"),
        ("Unknown", "U"),
        ("unspecified", "U"),
        ("", "U"),
        (None, "U"),
        ("Alien/Unidentified", "U"),
        ("Other", "U"),
    ],
)
def test_gender_normalization(gender_input: Any, expected_normalized: str) -> None:
    """Verify that various gender inputs normalize to standard CDISC SEX codes."""
    assert normalize_gender(gender_input) == expected_normalized


@pytest.mark.parametrize(
    "birthdate, observation_date, expected_age",
    [
        # Birthday on the observation date
        (date(2000, 5, 15), date(2020, 5, 15), 20),
        ("2000-05-15", "2020-05-15", 20),
        ("2000-05-15T00:00:00Z", "2020-05-15T12:00:00Z", 20),
        # Birthday before the observation date in the same year
        (date(2000, 5, 14), date(2020, 5, 15), 20),
        ("2000-05-14", "2020-05-15", 20),
        # Birthday after the observation date in the same year (has not occurred yet)
        (date(2000, 5, 16), date(2020, 5, 15), 19),
        ("2000-05-16", "2020-05-15", 19),
        # Leap year birthday
        (date(2000, 2, 29), date(2021, 2, 28), 20),
        (date(2000, 2, 29), date(2021, 3, 1), 21),
        # Datetime objects input
        (datetime(2000, 5, 15, 10, 0), datetime(2020, 5, 15, 18, 0), 20),
        # Missing or invalid input cases should fail safely (return None)
        (None, "2020-05-15", None),
        ("2000-05-15", None, None),
        ("invalid-date-string", "2020-05-15", None),
        ("2000-05-15", "invalid-observation-date", None),
        # Future birthdate (observation occurs before birth) should fail safely
        (date(2021, 5, 15), date(2020, 5, 15), None),
        ("2021-05-15", "2020-05-15", None),
    ],
)
def test_age_derivation_boundary_dates(
    birthdate: Any, observation_date: Any, expected_age: Any
) -> None:
    """Verify that age relative to observation date handles all boundary date scenarios correctly."""
    assert calculate_age(birthdate, observation_date) == expected_age


def test_get_safe_demographics_valid_decryption() -> None:
    """Verify that a valid encrypted demographics payload yields correct age and gender."""
    payload = {
        "birthdate": "1990-06-20",
        "gender": "female",
        "name": "Jane Smith",  # PII
    }
    encrypted = encrypt_demographics(payload)

    # Extract safe demographics
    safe_profile = get_safe_demographics(encrypted, "2025-06-20")

    # Check that it extracted the correct gender and age
    assert safe_profile["gender"] == "F"
    assert safe_profile["age"] == 35

    # Check that raw PII (name, exact birthdate) is not present in the returned dictionary
    assert "name" not in safe_profile
    assert "birthdate" not in safe_profile


def test_get_safe_demographics_failures_fail_safely() -> None:
    """Verify that invalid/missing/malformed demographics return safe default fallback values."""
    # 1. Missing demographics (None input)
    profile_none = get_safe_demographics(None, "2025-06-20")
    assert profile_none == {"gender": "U", "age": None}

    # 2. Empty string demographics
    profile_empty = get_safe_demographics("", "2025-06-20")
    assert profile_empty == {"gender": "U", "age": None}

    # 3. Completely undecryptable or invalid ciphertext
    profile_malformed = get_safe_demographics("invalid_token_xyz_123", "2025-06-20")
    assert profile_malformed == {"gender": "U", "age": None}

    # 4. Decrypts to invalid JSON structure (e.g. decrypted data is not a dictionary)
    encrypted_list = encrypt_demographics([1, 2, 3])  # type: ignore
    profile_list = get_safe_demographics(encrypted_list, "2025-06-20")
    assert profile_list == {"gender": "U", "age": None}

    # 5. Missing birthdate or gender inside payload
    encrypted_incomplete = encrypt_demographics({"name": "Secret Person"})
    profile_incomplete = get_safe_demographics(encrypted_incomplete, "2025-06-20")
    assert profile_incomplete == {"gender": "U", "age": None}
