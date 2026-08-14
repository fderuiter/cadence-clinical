import enum
import re
from enum import StrEnum


class Sex(StrEnum):
    M = "M"
    F = "F"
    U = "U"


class Race(StrEnum):
    AMERICAN_INDIAN_OR_ALASKA_NATIVE = "AMERICAN INDIAN OR ALASKA NATIVE"
    ASIAN = "ASIAN"
    BLACK_OR_AFRICAN_AMERICAN = "BLACK OR AFRICAN AMERICAN"
    NATIVE_HAWAIIAN_OR_OTHER_PACIFIC_ISLANDER = (
        "NATIVE HAWAIIAN OR OTHER PACIFIC ISLANDER"
    )
    WHITE = "WHITE"
    MULTIPLE = "MULTIPLE"
    OTHER = "OTHER"


class AESeverity(StrEnum):
    MILD = "MILD"
    MODERATE = "MODERATE"
    SEVERE = "SEVERE"


class AESeriousness(StrEnum):
    Y = "Y"
    N = "N"


class AERelationship(StrEnum):
    RELATED = "RELATED"
    NOT_RELATED = "NOT RELATED"
    POSSIBLY_RELATED = "POSSIBLY RELATED"


class AEOutcome(StrEnum):
    RECOVERED_RESOLVED = "RECOVERED/RESOLVED"
    RECOVERING_RESOLVING = "RECOVERING/RESOLVING"
    NOT_RECOVERED_NOT_RESOLVED = "NOT RECOVERED/NOT RESOLVED"
    RECOVERED_RESOLVED_WITH_SEQUELAE = "RECOVERED/RESOLVED WITH SEQUELAE"
    FATAL = "FATAL"
    UNKNOWN = "UNKNOWN"


class NullFlavor(StrEnum):
    NI = "NI"
    NA = "NA"
    UNK = "UNK"
    ASKU = "ASKU"
    NASK = "NASK"
    MSNG = "MSNG"


# CDISC Controlled Terminology sets mapped to core enums
VALID_SEX_VALUES = {s.value for s in Sex}
VALID_RACE_VALUES = {r.value for r in Race}
VALID_AESEV_VALUES = {s.value for s in AESeverity}


def normalize_sex(val: str | None) -> str:
    """Normalizes and validates SEX value to CDISC Controlled Terminology: 'M', 'F', 'U'."""
    if val is None:
        raise ValueError("SEX value cannot be None")

    cleaned = str(val).strip().upper()
    if cleaned in {"M", "MALE", "M_GEN", "1"}:
        return Sex.M.value
    if cleaned in {"F", "FEMALE", "F_GEN", "2"}:
        return Sex.F.value
    if cleaned in {
        "U",
        "UNKNOWN",
        "UNDIFFERENTIATED",
        "NOT REPORTED",
        "NOT_REPORTED",
        "9",
    }:
        return Sex.U.value

    raise ValueError(f"Value '{val}' is not a valid or normalizable CDISC SEX value.")


def normalize_race(val: str | list[str]) -> str:
    """Normalizes and validates RACE value to CDISC Controlled Terminology.

    If multiple races are provided (as a list or a delimited string), returns 'MULTIPLE'.
    """
    if val is None:
        raise ValueError("RACE value cannot be None")

    if isinstance(val, list):
        if len(val) == 0:
            raise ValueError("RACE list cannot be empty")
        if len(val) > 1:
            return Race.MULTIPLE.value
        val = val[0]

    raw_str = str(val).strip()
    if not raw_str:
        raise ValueError("RACE string cannot be blank")

    # Split on comma, semicolon, or " and "
    parts = re.split(r",|;| and | AND ", raw_str, flags=re.IGNORECASE)
    parts = [p.strip() for p in parts if p.strip()]

    if len(parts) > 1:
        return Race.MULTIPLE.value

    cleaned = parts[0].upper()
    cleaned = re.sub(r"\s+", " ", cleaned)

    valid_races = {r.value for r in Race}
    if cleaned in valid_races:
        return cleaned

    # Common aliases and fuzzy matches
    if cleaned in {"WHITE", "CAUCASIAN", "EUROPEAN"}:
        return Race.WHITE.value
    if cleaned in {"BLACK", "AFRICAN", "AFRICAN AMERICAN", "BLACK OR AFRICAN AMERICAN"}:
        return Race.BLACK_OR_AFRICAN_AMERICAN.value
    if cleaned in {"ASIAN", "EAST ASIAN", "SOUTH ASIAN"}:
        return Race.ASIAN.value
    if cleaned in {
        "AMERICAN INDIAN",
        "ALASKA NATIVE",
        "AMERICAN INDIAN OR ALASKA NATIVE",
    }:
        return Race.AMERICAN_INDIAN_OR_ALASKA_NATIVE.value
    if cleaned in {
        "HAWAIIAN",
        "PACIFIC ISLANDER",
        "NATIVE HAWAIIAN OR OTHER PACIFIC ISLANDER",
    }:
        return Race.NATIVE_HAWAIIAN_OR_OTHER_PACIFIC_ISLANDER.value
    if cleaned in {"MULTIPLE", "MIXED", "MORE THAN ONE RACE"}:
        return Race.MULTIPLE.value
    if cleaned in {"OTHER", "NOT REPORTED", "UNKNOWN", "DECLINED"}:
        return Race.OTHER.value

    raise ValueError(f"Value '{val}' is not a valid or normalizable CDISC RACE value.")


def normalize_severity(val: str | None) -> str:
    """Normalizes and validates AE severity (AESEV) to CDISC Controlled Terminology: 'MILD', 'MODERATE', 'SEVERE'."""
    if val is None:
        raise ValueError("AE Severity value cannot be None")

    cleaned = str(val).strip().upper()
    valid_sevs = {s.value for s in AESeverity}
    if cleaned in valid_sevs:
        return cleaned

    # Map numeric severity or common synonyms
    if cleaned in {"1", "MILD", "GRADE 1", "LOW"}:
        return AESeverity.MILD.value
    if cleaned in {"2", "MODERATE", "GRADE 2", "MEDIUM"}:
        return AESeverity.MODERATE.value
    if cleaned in {"3", "4", "5", "SEVERE", "GRADE 3", "GRADE 4", "GRADE 5", "HIGH"}:
        return AESeverity.SEVERE.value

    raise ValueError(f"Value '{val}' is not a valid or normalizable AE Severity value.")


def normalize_seriousness(val: str | bool | None) -> str:
    """Normalizes and validates AE seriousness (AESER) to CDISC: 'Y' or 'N'."""
    if val is None:
        raise ValueError("AE Seriousness value cannot be None")

    if isinstance(val, bool):
        return "Y" if val else "N"

    cleaned = str(val).strip().upper()
    if cleaned in {"Y", "YES", "TRUE", "1"}:
        return "Y"
    if cleaned in {"N", "NO", "FALSE", "0"}:
        return "N"

    raise ValueError(
        f"Value '{val}' is not a valid or normalizable seriousness code ('Y' or 'N')."
    )
