import datetime

import pytest
from pydantic import BaseModel

from packages.core.datetime_helpers import (
    AwareDatetime,
    serialize_utc_z,
)


class SampleModel(BaseModel):
    timestamp: AwareDatetime


def test_serialize_utc_z():
    """Validate UTC Z datetime serialization format.

    @req:PRD-SYS-001
    """
    dt = datetime.datetime(2026, 8, 25, 12, 0, 0, tzinfo=datetime.UTC)
    res = serialize_utc_z(dt)
    assert res == "2026-08-25T12:00:00Z"

    offset_dt = datetime.datetime(
        2026, 8, 25, 14, 0, 0, tzinfo=datetime.timezone(datetime.timedelta(hours=2))
    )
    res_offset = serialize_utc_z(offset_dt)
    assert res_offset == "2026-08-25T12:00:00Z"


def test_aware_datetime_pydantic_validation():
    """Validate Pydantic v2 AwareDatetime validation rules and naive rejection.

    @req:PRD-SYS-001
    """
    # Valid timezone-aware datetime
    m = SampleModel(
        timestamp=datetime.datetime(2026, 8, 25, 12, 0, 0, tzinfo=datetime.UTC)
    )
    assert m.timestamp.tzinfo == datetime.UTC

    # Reject naive datetime string or object
    with pytest.raises(Exception):
        SampleModel.model_validate({"timestamp": "2026-08-25T12:00:00"})


def test_aware_datetime_json_serialization():
    """Validate JSON serialization of AwareDatetime fields with Z suffix.

    @req:PRD-SYS-001
    """
    m = SampleModel(
        timestamp=datetime.datetime(2026, 8, 25, 12, 0, 0, tzinfo=datetime.UTC)
    )
    data = m.model_dump_json()
    assert '"timestamp":"2026-08-25T12:00:00Z"' in data
