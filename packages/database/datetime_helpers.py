"""Centralized datetime helpers for strict timezone-aware validation and UTC Z serialization in Pydantic v2."""

import datetime as dt_module

from sqlalchemy.types import DateTime, TypeDecorator

from packages.security.datetime_helpers import (
    AwareDatetime,
    serialize_utc_z,
    validate_timezone_aware_datetime,
)


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
