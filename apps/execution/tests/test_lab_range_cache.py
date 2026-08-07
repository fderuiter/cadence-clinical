import os
import time
from unittest import mock

import pytest

from apps.execution.lab_range_cache import (
    LabRangeCache,
    get_active_lab_ranges,
    lab_range_cache,
)


def test_lab_range_cache_ttl_config():
    # Test fallback flow: env var LAB_RANGE_CACHE_TTL
    with mock.patch.dict(os.environ, {"LAB_RANGE_CACHE_TTL": "100.5"}):
        cache = LabRangeCache()
        assert cache.ttl == 100.5

    # Test fallback flow: env var CACHE_TTL
    with mock.patch.dict(os.environ, {"CACHE_TTL": "50.0"}):
        if "LAB_RANGE_CACHE_TTL" in os.environ:
            del os.environ["LAB_RANGE_CACHE_TTL"]
        cache = LabRangeCache()
        assert cache.ttl == 50.0

    # Test default fallback: 3600.0
    with mock.patch.dict(os.environ, {}, clear=True):
        cache = LabRangeCache()
        assert cache.ttl == 3600.0

    # Test lenient parsing on invalid float fallback
    with mock.patch.dict(os.environ, {"LAB_RANGE_CACHE_TTL": "invalid_float"}):
        cache = LabRangeCache()
        assert cache.ttl == 3600.0


def test_lab_range_cache_operations():
    cache = LabRangeCache(max_size=3, ttl=1.0)

    # Empty cache check
    rows, is_expired = cache.get_cached("S1", "C1")
    assert rows is None
    assert is_expired is False

    # Set and retrieve
    test_rows_1 = ["row1", "row2"]
    cache.set_cached("S1", "C1", test_rows_1)

    rows, is_expired = cache.get_cached("S1", "C1")
    assert rows == test_rows_1
    assert is_expired is False

    # Test TTL expiration
    with mock.patch("time.time", return_value=time.time() + 2.0):
        rows, is_expired = cache.get_cached("S1", "C1")
        assert rows == test_rows_1
        assert is_expired is True

    # Test FIFO Eviction with max_size=3
    cache.set_cached("S2", "C2", ["row3"])
    cache.set_cached("S3", "C3", ["row4"])
    cache.set_cached("S4", "C4", ["row5"])  # This triggers eviction of S1, C1

    rows, is_expired = cache.get_cached("S1", "C1")
    assert rows is None  # Evicted

    rows, is_expired = cache.get_cached("S2", "C2")
    assert rows == ["row3"]

    # Test Invalidate
    cache.invalidate("S2", "C2")
    rows, is_expired = cache.get_cached("S2", "C2")
    assert rows is None

    # Test Clear
    cache.clear()
    rows, is_expired = cache.get_cached("S3", "C3")
    assert rows is None
    rows, is_expired = cache.get_cached("S4", "C4")
    assert rows is None


@pytest.mark.asyncio
async def test_get_active_lab_ranges_helper():
    cache = LabRangeCache(ttl=1.0)
    mock_session = mock.AsyncMock()
    mock_result = mock.MagicMock()

    # Mock database records
    class DummyRange:
        def __init__(self, id_val):
            self.id = id_val

    dummy_ranges = [DummyRange(1), DummyRange(2)]
    mock_result.scalars().all.return_value = dummy_ranges
    mock_session.execute.return_value = mock_result

    # 1. Miss / db query
    res = await get_active_lab_ranges(cache, mock_session, "S1", "T1")
    assert res == dummy_ranges
    assert mock_session.execute.call_count == 1

    # 2. Fresh hit / no db query
    res_hit = await get_active_lab_ranges(cache, mock_session, "S1", "T1")
    assert res_hit == dummy_ranges
    assert mock_session.execute.call_count == 1  # Still 1

    # 3. Expired with successful DB fetch
    with mock.patch("time.time", return_value=time.time() + 2.0):
        new_dummy_ranges = [DummyRange(3)]
        mock_result2 = mock.MagicMock()
        mock_result2.scalars().all.return_value = new_dummy_ranges
        mock_session.execute.return_value = mock_result2

        res_expired_new = await get_active_lab_ranges(cache, mock_session, "S1", "T1")
        assert res_expired_new == new_dummy_ranges
        assert mock_session.execute.call_count == 2

    # 4. Expired with failed DB fetch (stale-on-error fallback)
    with mock.patch("time.time", return_value=time.time() + 4.0):
        mock_session.execute.side_effect = Exception("DB error")

        # Should fallback to stale value (new_dummy_ranges) instead of raising
        res_stale = await get_active_lab_ranges(cache, mock_session, "S1", "T1")
        assert res_stale == new_dummy_ranges

    # 5. Miss (no cached value) with failed DB fetch (should raise)
    mock_session.execute.side_effect = Exception("DB connection failure")
    with pytest.raises(Exception, match="DB connection failure"):
        await get_active_lab_ranges(cache, mock_session, "S2", "T2")


def test_lab_range_cache_singleton():
    assert isinstance(lab_range_cache, LabRangeCache)
