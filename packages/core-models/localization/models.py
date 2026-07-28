"""
Localization-related constants and validation logic.
"""

from typing import Set

# A comprehensive list of supported ISO 639-1 language codes for Cadence Clinical
SUPPORTED_LANGUAGE_CODES: Set[str] = {
    "en",
    "es",
    "fr",
    "de",
    "it",
    "pt",
    "zh",
    "ja",
    "ko",
    "nl",
    "pl",
    "ru",
    "sv",
    "tr",
    "ar",
    "hi",
    "da",
    "fi",
    "no",
    "he",
    "id",
    "ms",
    "th",
    "vi",
    "el",
    "hu",
    "cs",
    "ro",
    "sk",
    "bg",
    "uk",
    "hr",
    "sr",
    "sl",
    "et",
    "lv",
    "lt",
}


def validate_language_code(code: str) -> str:
    """
    Validates that a string is a valid ISO 639-1 language code supported by the platform.
    Normalizes the code by stripping and converting to lowercase.
    Raises ValueError if invalid.
    """
    if not isinstance(code, str):
        raise ValueError("Language code must be a string.")

    normalized = code.strip().lower()
    if not normalized:
        raise ValueError("Language code cannot be empty.")

    if normalized not in SUPPORTED_LANGUAGE_CODES:
        raise ValueError(
            f"Language code '{code}' is not supported. Must be a valid ISO 639-1 code."
        )

    return normalized
