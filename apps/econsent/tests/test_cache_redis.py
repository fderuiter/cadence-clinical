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

            cache = ApprovedTranslationCache()

            # Local operations still work
            cache.set_cached("t1", 1, "es", {"name": "Spanish Translation"})
            data, expired = cache.get_cached("t1", 1, "es")
            assert data == {"name": "Spanish Translation"}


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
