import calendar
from datetime import date
from typing import Optional, Tuple


def parse_partial_date(
    date_str: Optional[str],
) -> Tuple[Optional[int], Optional[int], Optional[int]]:
    """Parses a date string (possibly partial) into (year, month, day).

    Supports format like YYYY-MM-DD, YYYY-MM, YYYY, or with missing indicators like 'UN', 'UNK', '00'.
    Also ignores any T-separated timestamp parts.
    """
    if not date_str:
        return None, None, None

    # Strip whitespace and split by T to handle timestamps
    clean_str = str(date_str).strip().split("T")[0]
    if not clean_str:
        return None, None, None

    parts = clean_str.split("-")
    year: Optional[int] = None
    month: Optional[int] = None
    day: Optional[int] = None

    def parse_part(part_str: str) -> Optional[int]:
        part_str = part_str.strip()
        if not part_str:
            return None
        # Support case-insensitive missing placeholders like UN, UNK
        if part_str.upper() in {"UN", "UNK", "00", "0000", "XX", "XXX"}:
            return None
        if not part_str.isdigit():
            return None
        val = int(part_str)
        if val == 0:
            return None
        return val

    if len(parts) >= 1:
        year = parse_part(parts[0])
    if len(parts) >= 2:
        month = parse_part(parts[1])
    if len(parts) >= 3:
        day = parse_part(parts[2])

    return year, month, day


def to_date_obj(val) -> Optional[date]:
    """Converts a value (date, datetime, or string) to a Python date object."""
    if not val:
        return None
    if isinstance(val, date):
        # datetime is a subclass of date, so we can check if it has a date() method
        if hasattr(val, "date"):
            return val.date()
        return val
    # If it's a string, try parsing
    y, m, d = parse_partial_date(str(val))
    if y is not None and m is not None and d is not None:
        try:
            return date(y, m, d)
        except ValueError:
            return None
    return None


def impute_partial_date(
    date_str: Optional[str],
    direction: str = "START",
    treatment_start_date: Optional[str] = None,
    end_of_study_date: Optional[str] = None,
) -> Optional[str]:
    """Imputes a partial date string (e.g. YYYY-MM-UN) according to direction-aware rules.

    Returns date as 'YYYY-MM-DD' string, or None if year is missing or invalid.
    """
    y, m, d = parse_partial_date(date_str)
    if y is None:
        return None

    # Check if complete and valid
    if m is not None and d is not None:
        try:
            # Validate it's a real date
            date(y, m, d)
            return f"{y:04d}-{m:02d}-{d:02d}"
        except ValueError:
            return None

    # Parse reference dates
    trt_dt = to_date_obj(treatment_start_date)
    eos_dt = to_date_obj(end_of_study_date)

    if direction.upper() == "START":
        if m is None:  # Only year is known
            if trt_dt and trt_dt.year == y:
                return f"{trt_dt.year:04d}-{trt_dt.month:02d}-{trt_dt.day:02d}"
            else:
                return f"{y:04d}-01-01"
        else:  # Year and Month are known, Day is missing
            if trt_dt and trt_dt.year == y and trt_dt.month == m:
                return f"{trt_dt.year:04d}-{trt_dt.month:02d}-{trt_dt.day:02d}"
            else:
                return f"{y:04d}-{m:02d}-01"

    elif direction.upper() == "END":
        if m is None:  # Only year is known
            imputed = date(y, 12, 31)
            if eos_dt and eos_dt.year == y and eos_dt < imputed:
                imputed = eos_dt
            return f"{imputed.year:04d}-{imputed.month:02d}-{imputed.day:02d}"
        else:  # Year and Month are known, Day is missing
            try:
                last_day = calendar.monthrange(y, m)[1]
                imputed = date(y, m, last_day)
                return f"{imputed.year:04d}-{imputed.month:02d}-{imputed.day:02d}"
            except ValueError:
                return None

    return None


def to_sas_date(date_val) -> Optional[int]:
    """Converts a date string or object to a SAS numeric date (days since 1960-01-01).

    Returns None if date_val is invalid or incomplete.
    """
    dt = to_date_obj(date_val)
    if not dt:
        return None
    epoch = date(1960, 1, 1)
    return (dt - epoch).days
