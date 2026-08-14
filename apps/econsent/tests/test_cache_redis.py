import json
import os
from unittest.mock import MagicMock, patch

import pytest

from apps.econsent.infrastructure.cache import ApprovedTranslationCache


def test_redis_unconfigured_graceful_fallback():
    """Verify that if REDIS_HOST is unconfigured, cache initializes and acts as local in-memory cache."""
    with patch.dict(os.environ, {}, clear=True):
        cache = ApprovedTranslationCache()
        assert cache.redis_host is None
        assert cache._pub_client is None
        assert cache._sub_thread is None

        # Test local cache still functions normally
        cache.set_cached("t1", 1, "es", {"name": "Spanish Translation"})
        data, expired = cache.get_cached("t1", 1, "es")
        assert data == {"name": "Spanish Translation"}
        assert not expired

        cache.invalidate("t1", 1, "es")
        data, expired = cache.get_cached("t1", 1, "es")
        assert data is None


def test_redis_unreachable_graceful_fallback():
    """Verify that if REDIS_HOST is configured but unreachable, initialization succeeds and doesn't crash."""
    with patch.dict(
        os.environ, {"REDIS_HOST": "unreachable.redis.local", "REDIS_PORT": "6379"}
    ):
        with patch("redis.Redis") as mock_redis:
            # Simulate Redis connection failure on ping/subscribe
            mock_redis.side_effect = Exception("Connection refused")

            with patch.object(
                ApprovedTranslationCache, "_run_subscriber", return_value=None
            ):
                cache = ApprovedTranslationCache()

                # Local operations still work
                cache.set_cached("t1", 1, "es", {"name": "Spanish Translation"})
                data, expired = cache.get_cached("t1", 1, "es")
                assert data == {"name": "Spanish Translation"}
                cache.close()


def test_redis_publish_on_invalidate_and_clear():
    """Verify that calling invalidate or clear on cache publishes lightweight payloads to Redis."""
    with patch.dict(
        os.environ, {"REDIS_HOST": "mock.redis.local", "REDIS_CHANNEL": "test_channel"}
    ):
        mock_pub_client = MagicMock()
        with patch("redis.Redis", return_value=mock_pub_client):
            # To prevent background subscriber thread from causing noise/errors, mock its target or let it run
            with patch.object(
                ApprovedTranslationCache, "_run_subscriber", return_value=None
            ):
                cache = ApprovedTranslationCache()
                cache._pub_client = mock_pub_client

                # Seed some cache
                cache.set_cached("t1", 1, "es", {"foo": "bar"})
                assert cache.get_status()["size"] == 1

                # Invalidate a template version
                cache.invalidate("t1", 1, "es")
                assert cache.get_status()["size"] == 0

                # Check that correct payload was published
                mock_pub_client.publish.assert_any_call(
                    "test_channel",
                    json.dumps(
                        {
                            "action": "invalidate",
                            "template_id": "t1",
                            "version_index": 1,
                            "language_code": "es",
                        }
                    ),
                )

                # Reset mock and test clear
                mock_pub_client.reset_mock()
                cache.set_cached("t1", 1, "es", {"foo": "bar"})
                cache.clear()
                assert cache.get_status()["size"] == 0

                mock_pub_client.publish.assert_any_call(
                    "test_channel",
                    json.dumps(
                        {
                            "action": "clear",
                        }
                    ),
                )

                # Reset mock and test invalidate_template
                mock_pub_client.reset_mock()
                cache.set_cached("t1", 1, "es", {"foo": "bar"})
                cache.invalidate_template("t1")
                assert cache.get_status()["size"] == 0

                mock_pub_client.publish.assert_any_call(
                    "test_channel",
                    json.dumps(
                        {
                            "action": "invalidate_template",
                            "template_id": "t1",
                        }
                    ),
                )


def test_redis_subscriber_receives_and_evicts_cache():
    """Verify that background subscriber thread receives messages and evicts entries locally without loop republishes."""
    with patch.dict(
        os.environ, {"REDIS_HOST": "mock.redis.local", "REDIS_CHANNEL": "test_channel"}
    ):
        # We want to test that the background thread listens and evicts.
        # We can mock the Redis instance returned in _run_subscriber.
        mock_r = MagicMock()
        mock_pubsub = MagicMock()
        mock_r.pubsub.return_value = mock_pubsub

        # We want pubsub.listen() to yield a message, then exit or sleep
        messages = [
            {
                "type": "message",
                "data": json.dumps(
                    {
                        "action": "invalidate",
                        "template_id": "t1",
                        "version_index": 1,
                        "language_code": "es",
                    }
                ),
            },
            {
                "type": "message",
                "data": json.dumps(
                    {
                        "action": "clear",
                    }
                ),
            },
            # Simulate a disconnect to exit/raise error to stop the loop
            Exception("Disconnect simulation for test"),
        ]

        def mock_listen():
            for m in messages:
                if isinstance(m, Exception):
                    raise m
                yield m

        mock_pubsub.listen = mock_listen

        # Get original _run_subscriber before entering mock patch block
        original_run_subscriber = ApprovedTranslationCache._run_subscriber

        # Mock the time.sleep in the retry loop so the test completes quickly when error is raised
        # Also patch _run_subscriber to do nothing on init, preventing the background thread from starting automatically
        with (
            patch("redis.Redis", return_value=mock_r),
            patch("time.sleep", side_effect=InterruptedError("Stop loop")),
            patch.object(
                ApprovedTranslationCache, "_run_subscriber", return_value=None
            ),
        ):
            cache = ApprovedTranslationCache()
            # Deactivate publisher publish so we can assert no republishing occurs (avoiding infinite loops)
            mock_pub_client = MagicMock()
            cache._pub_client = mock_pub_client

            # Let's seed cache entries
            cache.set_cached("t1", 1, "es", {"foo": "bar"})
            cache.set_cached("t2", 1, "en", {"hello": "world"})
            assert cache.get_status()["size"] == 2

            # Now run original subscriber loop synchronously in the main thread
            with pytest.raises(InterruptedError):
                original_run_subscriber(cache)

            # Now assert results:
            # 1. ("t1", 1, "es") should have been invalidated by the first message
            # 2. Then clear was processed, so ("t2", 1, "en") should also be cleared (size should be 0)
            assert cache.get_status()["size"] == 0

            # 3. CRITICAL: Background thread MUST NOT publish back to Redis when processing received messages.
            # So mock_pub_client.publish should NOT have been called.
            mock_pub_client.publish.assert_not_called()


def test_cache_ttl_and_env_initialization():
    # Test ttl initialization with explicit parameter
    cache = ApprovedTranslationCache(ttl=120)
    assert cache.ttl == 120.0

    # Test invalid float env var
    with patch.dict(os.environ, {"ECONSENT_CACHE_TTL": "not-a-float"}):
        cache2 = ApprovedTranslationCache()
        assert cache2.ttl == 3600.0


def test_cache_max_size_eviction():
    # Test max size reached evicts the oldest key
    cache = ApprovedTranslationCache(max_size=2)
    cache.set_cached("t1", 1, "en", {"data": "1"})
    cache.set_cached("t2", 1, "en", {"data": "2"})
    assert cache.get_status()["size"] == 2

    # Third set should evict "t1"
    cache.set_cached("t3", 1, "en", {"data": "3"})
    assert cache.get_status()["size"] == 2
    data, expired = cache.get_cached("t1", 1, "en")
    assert data is None


def test_cache_entry_expiration():
    cache = ApprovedTranslationCache(ttl=0.01)
    cache.set_cached("t1", 1, "en", {"data": "1"})
    import time

    time.sleep(0.02)
    data, expired = cache.get_cached("t1", 1, "en")
    assert data == {"data": "1"}
    assert expired


def test_redis_publish_error_handling():
    # Test exception in _publish_message doesn't crash
    with patch.dict(os.environ, {"REDIS_HOST": "mock.redis.local"}):
        mock_pub_client = MagicMock()
        mock_pub_client.publish.side_effect = Exception("Redis publish error")
        with (
            patch("redis.Redis", return_value=mock_pub_client),
            patch.object(
                ApprovedTranslationCache, "_run_subscriber", return_value=None
            ),
        ):
            cache = ApprovedTranslationCache()
            cache.invalidate(
                "t1", 1, "en"
            )  # Should log warning but not raise exception


def test_redis_subscriber_invalidate_template():
    with patch.dict(
        os.environ, {"REDIS_HOST": "mock.redis.local", "REDIS_CHANNEL": "test_channel"}
    ):
        mock_r = MagicMock()
        mock_pubsub = MagicMock()
        mock_r.pubsub.return_value = mock_pubsub

        messages = [
            {
                "type": "message",
                "data": json.dumps(
                    {
                        "action": "invalidate_template",
                        "template_id": "t1",
                    }
                ),
            },
            {
                "type": "message",
                "data": "invalid-json-payload-test",
            },
            Exception("Stop loop"),
        ]

        def mock_listen():
            for m in messages:
                if isinstance(m, Exception):
                    raise m
                yield m

        mock_pubsub.listen = mock_listen
        original_run_subscriber = ApprovedTranslationCache._run_subscriber

        with (
            patch("redis.Redis", return_value=mock_r),
            patch.object(
                ApprovedTranslationCache, "_run_subscriber", return_value=None
            ),
        ):
            cache = ApprovedTranslationCache()
            cache._stop_event.wait = MagicMock(
                side_effect=InterruptedError("Stop loop")
            )
            cache.set_cached("t1", 1, "en", {"foo": "bar"})
            cache.set_cached("t1", 2, "en", {"baz": "qux"})
            cache.set_cached("t2", 1, "en", {"other": "data"})
            assert cache.get_status()["size"] == 3

            # Run loop
            with pytest.raises(InterruptedError):
                original_run_subscriber(cache)

            # t1 keys should be gone, t2 should remain
            assert cache.get_status()["size"] == 1
            data, _ = cache.get_cached("t2", 1, "en")
            assert data == {"other": "data"}


@pytest.mark.asyncio
async def test_get_approved_template_translation_helper():
    from apps.econsent.infrastructure.cache import get_approved_template_translation

    cache = ApprovedTranslationCache()

    async def mock_fetch_db(template_id, version_index, language_code):
        return {"fetched": "from_db"}

    # Cache miss -> DB fetch
    res = await get_approved_template_translation(cache, "t1", 1, "en", mock_fetch_db)
    assert res == {"fetched": "from_db"}

    # Cache hit
    res2 = await get_approved_template_translation(cache, "t1", 1, "en", mock_fetch_db)
    assert res2 == {"fetched": "from_db"}

    # Expired cache fallback
    cache.ttl = -1.0  # Force expired

    async def mock_fetch_fail(template_id, version_index, language_code):
        raise Exception("DB offline")

    res3 = await get_approved_template_translation(
        cache, "t1", 1, "en", mock_fetch_fail
    )
    assert res3 == {"fetched": "from_db"}  # should fallback to stale cached data

    # Failure with no cache fallback
    with pytest.raises(Exception, match="DB offline"):
        await get_approved_template_translation(cache, "t2", 1, "en", mock_fetch_fail)
