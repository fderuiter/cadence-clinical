"""Centralized datetime helpers for strict timezone-aware validation and UTC Z serialization in Pydantic v2."""

import datetime as dt_module
from datetime import UTC, datetime
from typing import Annotated, Any

from pydantic import PlainSerializer, WrapValidator
from sqlalchemy.types import DateTime, TypeDecorator


def validate_timezone_aware_datetime(v: Any, handler) -> datetime:
    """Validates that a datetime input is strictly timezone-aware.

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
    return dt.astimezone(UTC)


def serialize_utc_z(dt: datetime) -> str:
    """Formats the datetime back into an ISO-8601 string, enforcing a trailing 'Z' for UTC."""
    utc_dt = dt.astimezone(UTC)
    return utc_dt.isoformat().replace("+00:00", "Z")


# Custom timezone-aware datetime type for Pydantic v2
AwareDatetime = Annotated[
    datetime,
    WrapValidator(validate_timezone_aware_datetime),
    PlainSerializer(serialize_utc_z, return_type=str, when_used="json-unless-none"),
]


class UTCDateTime(TypeDecorator):
    """SQLAlchemy TypeDecorator that enforces UTC timezones on reads and writes."""

    impl = DateTime(timezone=True)
    cache_ok = True

    def process_bind_param(self, value, dialect):
        if value is not None:
            if not isinstance(value, dt_module.datetime):
                raise ValueError("Value must be a datetime object.")
            if value.tzinfo is None or value.tzinfo.utcoffset(value) is None:
                raise ValueError("Naive datetimes are not allowed.")
            return value.astimezone(dt_module.UTC)
        return value

    def process_result_value(self, value, dialect):
        if value is not None:
            if value.tzinfo is None:
                return value.replace(tzinfo=dt_module.UTC)
            return value.astimezone(dt_module.UTC)
        return value


__all__ = [
    "AwareDatetime",
    "serialize_utc_z",
    "validate_timezone_aware_datetime",
    "UTCDateTime",
]
