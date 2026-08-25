from datetime import UTC, datetime, timedelta, timezone

import pytest
from pydantic import BaseModel

from packages.core.datetime_helpers import (
    AwareDatetime,
    serialize_utc_z,
    validate_timezone_aware_datetime,
)


class SampleModel(BaseModel):
    timestamp: AwareDatetime


def test_validate_timezone_aware_datetime_valid():
    dt = datetime.now(UTC)
    result = validate_timezone_aware_datetime(dt, lambda x: x)
    assert result.tzinfo == UTC
    assert result == dt


def test_validate_timezone_aware_datetime_offset():
    est = timezone(timedelta(hours=-5))
    dt = datetime(2026, 1, 1, 12, 0, 0, tzinfo=est)
    result = validate_timezone_aware_datetime(dt, lambda x: x)
    assert result.tzinfo == UTC
    assert result.hour == 17


def test_validate_timezone_aware_datetime_naive_rejected():
    naive_dt = datetime(2026, 1, 1, 12, 0, 0)
    with pytest.raises(ValueError, match="Datetime must be timezone-aware"):
        validate_timezone_aware_datetime(naive_dt, lambda x: x)


def test_serialize_utc_z():
    dt = datetime(2026, 1, 1, 12, 0, 0, tzinfo=UTC)
    serialized = serialize_utc_z(dt)
    assert serialized.endswith("Z")
    assert serialized == "2026-01-01T12:00:00Z"


def test_pydantic_aware_datetime_model():
    # Valid ISO string with Z
    m = SampleModel.model_validate({"timestamp": "2026-08-25T12:00:00Z"})
    assert m.timestamp.tzinfo == UTC
    json_output = m.model_dump_json()
    assert '"timestamp":"2026-08-25T12:00:00Z"' in json_output

    # Reject naive string
    with pytest.raises(ValueError):
        SampleModel.model_validate({"timestamp": "2026-08-25T12:00:00"})
