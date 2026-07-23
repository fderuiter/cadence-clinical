import os
import threading
import time
from unittest.mock import patch

import pytest

from apps.designer.db import TerminologyCache, db_query_counts


def test_terminology_cache_ttl_config():
    """Verify that TerminologyCache correctly configures TTL based on constructor arguments or environment variables."""
    # Test setting custom TTL via constructor
    cache = TerminologyCache(max_size=10, ttl=45.0)
    assert cache.ttl == 45.0

    # Test setting TTL via env variable
    with patch.dict(os.environ, {"TERMINOLOGY_CACHE_TTL": "120"}):
        cache_env = TerminologyCache(max_size=10)
        assert cache_env.ttl == 120.0

    # Test setting TTL via fallback env variable CACHE_TTL
    with patch.dict(os.environ, {"CACHE_TTL": "90"}):
        if "TERMINOLOGY_CACHE_TTL" in os.environ:
            del os.environ["TERMINOLOGY_CACHE_TTL"]
        cache_env2 = TerminologyCache(max_size=10)
        assert cache_env2.ttl == 90.0

    # Test invalid env variable falls back to default 3600.0
    with patch.dict(os.environ, {"TERMINOLOGY_CACHE_TTL": "invalid"}):
        if "CACHE_TTL" in os.environ:
            del os.environ["CACHE_TTL"]
        cache_invalid = TerminologyCache(max_size=10)
        assert cache_invalid.ttl == 3600.0


def test_terminology_cache_hit_and_expiration():
    """Verify cache hits function normally, and that expired entries trigger a database reload."""
    # Create a cache with a very small TTL
    cache = TerminologyCache(max_size=100, ttl=0.05)

    concept_id = "C123"
    # Get first time (cache miss, loads from DB)
    val1 = cache.get(concept_id)
    assert val1 is not None
    assert val1["code"] == "C123"

    # Get second time immediately (should hit the cache and not query the DB)
    with patch("apps.designer.db.get_terminology_from_db") as mock_db:
        val2 = cache.get(concept_id)
        mock_db.assert_not_called()
        assert val2 == val1

    # Wait for the TTL to expire
    time.sleep(0.06)

    # Get third time (expired, should query the DB again)
    with patch("apps.designer.db.get_terminology_from_db") as mock_db:
        mock_db.return_value = {
            "code": "C123",
            "decode": "Updated Treatment Arm",
            "system": "NCI",
        }
        val3 = cache.get(concept_id)
        mock_db.assert_called_once_with(concept_id)
        assert val3["decode"] == "Updated Treatment Arm"


def test_terminology_cache_unreachable_db_fallback():
    """Verify that if database is unreachable (raises Exception) when querying an expired entry, the cache falls back to returning the expired entry."""
    cache = TerminologyCache(max_size=100, ttl=0.01)

    concept_id = "C123"
    # Populate the cache
    val = cache.get(concept_id)
    assert val is not None

    # Wait for the TTL to expire
    time.sleep(0.02)

    # Database becomes temporarily unreachable and throws exception
    with patch(
        "apps.designer.db.get_terminology_from_db",
        side_effect=Exception("Database Connection Timeout"),
    ):
        # Cache should silently handle the exception and return the expired entry
        fallback_val = cache.get(concept_id)
        assert fallback_val == val

    # If querying an entry not present in cache, the DB exception should propagate
    with patch(
        "apps.designer.db.get_terminology_from_db",
        side_effect=Exception("Database Connection Timeout"),
    ):
        with pytest.raises(Exception, match="Database Connection Timeout"):
            cache.get("NON_EXISTENT_CONCEPT")


def test_terminology_cache_capacity_eviction():
    """Verify that TerminologyCache respects maximum capacity limit by evicting the oldest element when full."""
    cache = TerminologyCache(max_size=3, ttl=100.0)

    # Populate cache up to its max size
    cache.get("C123")
    cache.get("C456")
    cache.get("C789")

    status = cache.get_status()
    assert status["size"] == 3

    # Adding a 4th concept triggers eviction of the oldest entry ("C123")
    cache.get("C012")

    status2 = cache.get_status()
    assert status2["size"] == 3

    # Verifying that retrieval of C123 now triggers a database lookup (since it was evicted)
    initial_queries = db_query_counts["terminology_lookups"]
    cache.get("C123")
    assert db_query_counts["terminology_lookups"] == initial_queries + 1


def test_terminology_cache_thread_safety():
    """Verify that multiple concurrent threads can read and write to TerminologyCache without raising errors."""
    cache = TerminologyCache(max_size=100, ttl=10.0)

    def worker():
        for _ in range(50):
            cache.get("C123")
            cache.get("C456")

    threads = [threading.Thread(target=worker) for _ in range(10)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    status = cache.get_status()
    assert status["size"] == 2
