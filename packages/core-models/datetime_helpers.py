"""
Centralized datetime helpers for strict timezone-aware validation and UTC Z serialization in Pydantic v2.
"""

from datetime import datetime, timezone
from typing import Annotated, Any

from pydantic import PlainSerializer, WrapValidator


def validate_timezone_aware_datetime(v: Any, handler) -> datetime:
    """
    Validates that a datetime input is strictly timezone-aware.
    Rejects any timezone-naive inputs immediately (e.g., throwing a ValueError).
    Converts and normalizes all timezone-aware inputs to UTC.
    """
    dt = handler(v)
    if not isinstance(dt, datetime):
        raise ValueError("Invalid datetime value")
    if dt.tzinfo is None or dt.tzinfo.utcoffset(dt) is None:
        raise ValueError(
            "Datetime must be timezone-aware (e.g. contain a 'Z' or offset like '+00:00')"
        )
    return dt.astimezone(timezone.utc)


def serialize_utc_z(dt: datetime) -> str:
    """
    Formats the datetime back into an ISO-8601 string, enforcing a trailing 'Z' for UTC.
    """
    utc_dt = dt.astimezone(timezone.utc)
    return utc_dt.isoformat().replace("+00:00", "Z")


# Custom timezone-aware datetime type for Pydantic v2
AwareDatetime = Annotated[
    datetime,
    WrapValidator(validate_timezone_aware_datetime),
    PlainSerializer(serialize_utc_z, return_type=str, when_used="json-unless-none"),
]
