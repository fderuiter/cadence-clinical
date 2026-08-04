"""Centralized datetime helpers for strict timezone-aware validation and UTC Z serialization in Pydantic v2.

Supports GxP-compliant clinical trial audit logging and synchronized operations.
"""

from datetime import UTC, datetime
from typing import Annotated, Any

from pydantic import PlainSerializer, WrapValidator


def validate_timezone_aware_datetime(v: Any, handler: Any) -> datetime:
    """Validates that a datetime input is strictly timezone-aware.

    Rejects any timezone-naive inputs immediately (e.g., throwing a ValueError).
    Converts and normalizes all timezone-aware inputs to UTC.

    Args:
        v (Any): The input value to validate.
        handler (Any): The Pydantic validator handler.

    Returns:
        datetime: A timezone-aware UTC datetime.

    Raises:
        ValueError: If the input is not a datetime or is not timezone-aware.
    """
    dt = handler(v)
    if not isinstance(dt, datetime):
        raise ValueError("Invalid datetime value")
    if dt.tzinfo is None or dt.tzinfo.utcoffset(dt) is None:
        raise ValueError(
            "Datetime must be timezone-aware (e.g. contain a 'Z' or offset like '+00:00')"
        )
    return dt.astimezone(UTC)


def serialize_utc_z(dt: datetime) -> str:
    """Formats the datetime back into an ISO-8601 string, enforcing a trailing 'Z' for UTC.

    Args:
        dt (datetime): The datetime to serialize.

    Returns:
        str: Serialized UTC string with trailing 'Z'.
    """
    utc_dt = dt.astimezone(UTC)
    return utc_dt.isoformat().replace("+00:00", "Z")


# Custom timezone-aware datetime type for Pydantic v2
AwareDatetime = Annotated[
    datetime,
    WrapValidator(validate_timezone_aware_datetime),
    PlainSerializer(serialize_utc_z, return_type=str, when_used="json-unless-none"),
]


def get_utc_now_aware() -> datetime:
    """Generates a timezone-aware UTC datetime.

    Returns:
        datetime: A timezone-aware datetime representing current UTC time.
    """
    return datetime.now(UTC)


def get_utc_now_naive() -> datetime:
    """Generates a timezone-naive UTC datetime.

    Returns:
        datetime: A timezone-naive datetime representing current UTC time.
    """
    return datetime.now(UTC).replace(tzinfo=None)


def normalize_to_utc_naive(dt: datetime) -> datetime:
    """Normalizes any datetime (aware or naive) to timezone-naive UTC.

    If the input datetime is timezone-aware, it is converted to UTC and
    then stripped of its timezone offset. If the input datetime is timezone-naive,
    it is assumed to represent UTC and returned directly.

    Args:
        dt (datetime): The datetime to normalize.

    Returns:
        datetime: A timezone-naive UTC datetime.
    """
    if dt.tzinfo is not None:
        return dt.astimezone(UTC).replace(tzinfo=None)
    return dt


def normalize_to_utc_aware(dt: datetime) -> datetime:
    """Normalizes any datetime (aware or naive) to timezone-aware UTC.

    If the input datetime is timezone-naive, it is assumed to represent UTC
    and assigned the UTC timezone offset. If the input datetime is timezone-aware,
    it is converted to UTC.

    Args:
        dt (datetime): The datetime to normalize.

    Returns:
        datetime: A timezone-aware UTC datetime.
    """
    if dt.tzinfo is None:
        return dt.replace(tzinfo=UTC)
    return dt.astimezone(UTC)
