# Phase 15: SDTM Domain Extraction Engine
from typing import List, Optional, Union

from sdtm.enums import AESeverity, Race, Sex
from sdtm.terminology import (
    normalize_race as _normalize_race,
)
from sdtm.terminology import (
    normalize_seriousness as _normalize_seriousness,
)
from sdtm.terminology import (
    normalize_severity as _normalize_severity,
)
from sdtm.terminology import (
    normalize_sex as _normalize_sex,
)

# CDISC Controlled Terminology sets mapped to core enums
VALID_SEX_VALUES = {s.value for s in Sex}

VALID_RACE_VALUES = {r.value for r in Race}

VALID_AESEV_VALUES = {s.value for s in AESeverity}


def normalize_sex(val: Optional[str]) -> str:
    """Normalizes and validates SEX value to CDISC Controlled Terminology: 'M', 'F', 'U'.

    Delegates to standard implementation in core-models packages.
    """
    return _normalize_sex(val)


def normalize_race(val: Union[str, List[str]]) -> str:
    """Normalizes and validates RACE value to CDISC Controlled Terminology.

    Delegates to standard implementation in core-models packages.
    """
    return _normalize_race(val)


def normalize_severity(val: Optional[str]) -> str:
    """Normalizes and validates AE severity (AESEV) to CDISC Controlled Terminology: 'MILD', 'MODERATE', 'SEVERE'.

    Delegates to standard implementation in core-models packages.
    """
    return _normalize_severity(val)


def normalize_seriousness(val: Optional[Union[str, bool]]) -> str:
    """Normalizes and validates AE seriousness (AESER) to CDISC: 'Y' or 'N'.

    Delegates to standard implementation in core-models packages.
    """
    return _normalize_seriousness(val)
