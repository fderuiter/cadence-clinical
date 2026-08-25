"""Tests for centralized timezone-aware datetime validation, UTC serialization, and Pydantic v2 integration.

@req:PRD-SYS-001
@req:PRD-SYS-004
"""

from datetime import UTC, datetime, timedelta, timezone

import pytest
from pydantic import BaseModel

from packages.core.datetime_helpers import (
    AwareDatetime,
    serialize_utc_z,
    validate_timezone_aware_datetime,
)


class SampleModel(BaseModel):
    """Sample Pydantic model utilizing AwareDatetime for timezone-aware fields."""

    timestamp: AwareDatetime


def test_validate_timezone_aware_datetime_valid() -> None:
    """Verify timezone-aware UTC datetime passes validation.

    @req:PRD-SYS-004
    """
    dt = datetime.now(UTC)
    result = validate_timezone_aware_datetime(dt, lambda x: x)  # type: ignore[arg-type]
    assert result.tzinfo == UTC
    assert result == dt


def test_validate_timezone_aware_datetime_offset() -> None:
    """Verify offset timezone datetime is converted to UTC.

    @req:PRD-SYS-004
    """
    est = timezone(timedelta(hours=-5))
    dt = datetime(2026, 1, 1, 12, 0, 0, tzinfo=est)
    result = validate_timezone_aware_datetime(dt, lambda x: x)  # type: ignore[arg-type]
    assert result.tzinfo == UTC
    assert result.hour == 17


def test_validate_timezone_aware_datetime_naive_rejected() -> None:
    """Verify timezone-naive datetime is rejected with ValueError.

    @req:PRD-SYS-004
    """
    naive_dt = datetime(2026, 1, 1, 12, 0, 0)
    with pytest.raises(ValueError, match="Datetime must be timezone-aware"):
        validate_timezone_aware_datetime(naive_dt, lambda x: x)  # type: ignore[arg-type]


def test_serialize_utc_z() -> None:
    """Verify UTC datetime is serialized to ISO string with trailing Z.

    @req:PRD-SYS-004
    """
    dt = datetime(2026, 1, 1, 12, 0, 0, tzinfo=UTC)
    serialized = serialize_utc_z(dt)
    assert serialized.endswith("Z")
    assert serialized == "2026-01-01T12:00:00Z"


def test_pydantic_aware_datetime_model() -> None:
    """Verify Pydantic model with AwareDatetime parses valid ISO strings and rejects naive ones.

    @req:PRD-SYS-004
    """
    # Valid ISO string with Z
    m = SampleModel.model_validate({"timestamp": "2026-08-25T12:00:00Z"})
    assert m.timestamp.tzinfo == UTC
    json_output = m.model_dump_json()
    assert '"timestamp":"2026-08-25T12:00:00Z"' in json_output

    # Reject naive string
    with pytest.raises(ValueError):
        SampleModel.model_validate({"timestamp": "2026-08-25T12:00:00"})
