"""Centralized datetime helpers for strict timezone-aware validation and UTC Z serialization in Pydantic v2."""

import datetime as dt_module

from sqlalchemy.types import DateTime, TypeDecorator

from packages.core.datetime_helpers import (
    AwareDatetime,
    serialize_utc_z,
    validate_timezone_aware_datetime,
)


class UTCDateTime(TypeDecorator):
    """SQLAlchemy TypeDecorator that enforces UTC timezones on reads and writes."""

    impl = DateTime(timezone=True)
    cache_ok = True

    def process_bind_param(
        self, value: dt_module.datetime | None, dialect: dt_module.datetime | object
    ) -> dt_module.datetime | None:
        """Process datetime parameter bound to SQL statement.

        Args:
            value (dt_module.datetime | None): The datetime value to process.
            dialect (dt_module.datetime | object): The database dialect.

        Returns:
            dt_module.datetime | None: UTC-normalized datetime or None.

        Raises:
            ValueError: If value is not a datetime object or is timezone-naive.
        """
        if value is not None:
            if not isinstance(value, dt_module.datetime):
                raise ValueError("Value must be a datetime object.")
            if value.tzinfo is None or value.tzinfo.utcoffset(value) is None:
                raise ValueError("Naive datetimes are not allowed.")
            return value.astimezone(dt_module.UTC)
        return value

    def process_result_value(
        self, value: dt_module.datetime | None, dialect: dt_module.datetime | object
    ) -> dt_module.datetime | None:
        """Process datetime result value returned from SQL query.

        Args:
            value (dt_module.datetime | None): The database result datetime value.
            dialect (dt_module.datetime | object): The database dialect.

        Returns:
            dt_module.datetime | None: UTC-normalized datetime or None.
        """
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
