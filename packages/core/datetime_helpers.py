"""Centralized datetime helpers for strict timezone-aware validation and UTC Z serialization in Pydantic v2."""

from collections.abc import Callable
from datetime import UTC, datetime
from typing import Annotated

from pydantic import PlainSerializer, WrapValidator


def validate_timezone_aware_datetime(
    v: object, handler: Callable[[object], datetime]
) -> datetime:
    """Validates that a datetime input is strictly timezone-aware.

    Rejects any timezone-naive inputs immediately by raising a ValueError.
    Converts and normalizes all timezone-aware inputs to UTC.

    Args:
        v (object): The raw incoming input value to be validated.
        handler (Callable[[object], datetime]): Pydantic wrap validator handler callable.

    Returns:
        datetime: A timezone-aware datetime instance in UTC.

    Raises:
        ValueError: If the value is not a datetime instance or is timezone-naive.
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
        dt (datetime): The timezone-aware datetime instance to serialize.

    Returns:
        str: ISO-8601 formatted string ending with 'Z'.
    """
    utc_dt = dt.astimezone(UTC)
    return utc_dt.isoformat().replace("+00:00", "Z")


# Custom timezone-aware datetime type for Pydantic v2
AwareDatetime = Annotated[
    datetime,
    WrapValidator(validate_timezone_aware_datetime),
    PlainSerializer(serialize_utc_z, return_type=str, when_used="json-unless-none"),
]


__all__ = [
    "AwareDatetime",
    "serialize_utc_z",
    "validate_timezone_aware_datetime",
]
