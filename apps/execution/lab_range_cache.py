import os
import threading
import time
from typing import Any, Optional
from sqlalchemy import select

from apps.execution.database.models import LabReferenceRange


class LabRangeCache:
    """
    Thread-safe, TTL-based cache for reference ranges.
    Uses a single threading.Lock to guard all reads and writes.
    Uses a composite key (study_id, test_code).
    """

    def __init__(self, max_size: int = 1000, ttl: Optional[float] = None) -> None:
        self.max_size = max_size
        self._lock = threading.Lock()
        self._cache: dict[tuple[str, str], tuple[list[Any], float]] = {}

        if ttl is not None:
            self.ttl = float(ttl)
        else:
            env_ttl = os.getenv("LAB_RANGE_CACHE_TTL") or os.getenv("CACHE_TTL")
            if env_ttl is not None:
                try:
                    self.ttl = float(env_ttl)
                except ValueError:
                    self.ttl = 3600.0
            else:
                self.ttl = 3600.0

    def get_cached(self, study_id: str, test_code: str) -> tuple[Optional[list[Any]], bool]:
        """
        Retrieves the item from cache. Returns (rows | None, is_expired).
        """
        key = (study_id, test_code)
        now = time.time()
        with self._lock:
            if key in self._cache:
                rows, timestamp = self._cache[key]
                if now - timestamp < self.ttl:
                    return rows, False
                return rows, True
        return None, False

    def set_cached(self, study_id: str, test_code: str, rows: list[Any]) -> None:
        """
        Stores reference range rows in cache. FIFO eviction when max_size is reached.
        """
        key = (study_id, test_code)
        with self._lock:
            if len(self._cache) >= self.max_size and key not in self._cache:
                # FIFO eviction
                oldest_key = next(iter(self._cache))
                self._cache.pop(oldest_key)
            self._cache[key] = (rows, time.time())

    def invalidate(self, study_id: str, test_code: str) -> None:
        """
        Removes one exact key from the cache.
        """
        key = (study_id, test_code)
        with self._lock:
            if key in self._cache:
                del self._cache[key]

    def clear(self) -> None:
        """
        Clears the whole cache.
        """
        with self._lock:
            self._cache.clear()


async def get_active_lab_ranges(
    cache: LabRangeCache,
    session: Any,
    study_id: str,
    test_code: str,
) -> list[Any]:
    """
    Read-through helper that returns cached rows on a hit and loads from the database on a miss.
    On a fresh hit, return the cached rows.
    On a miss or an expired entry, query the database for active rows.
    On a database fetch error, if a stale cached entry exists, return it; otherwise re-raise.
    """
    cached_rows, is_expired = cache.get_cached(study_id, test_code)
    if cached_rows is not None and not is_expired:
        return cached_rows

    try:
        stmt = select(LabReferenceRange).where(
            LabReferenceRange.study_id == study_id,
            LabReferenceRange.test_code == test_code,
            LabReferenceRange.is_deleted.is_(False),
        )
        result = await session.execute(stmt)
        rows = list(result.scalars().all())
    except Exception as e:
        if cached_rows is not None:
            return cached_rows
        raise e

    cache.set_cached(study_id, test_code, rows)
    return rows


# Module-level singleton instance
lab_range_cache = LabRangeCache()
